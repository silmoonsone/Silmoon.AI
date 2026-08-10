using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System.Threading.Channels;
using Silmoon.AI.Tools;
using Silmoon.AI.Models;
using System.Collections.Concurrent;
using Silmoon.Threading;

namespace Silmoon.AI.OpenAI;

public class ChatClient : NativeClient
{
    ChatHttpClient HttpClient { get; set; }

    public ChatClient(ModelProvider provider, string modelName, string systemPrompt = null, bool enableThinking = false, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null, double? temperature = null, double? topP = null)
    {
        ModelProvider = provider;
        ModelName = modelName;
        SystemPrompt = systemPrompt;
        EnableThinking = enableThinking;
        Temperature = temperature ?? RequestBase.DefaultTemperature;
        TopP = topP ?? RequestBase.DefaultTopP;

        ToolSetManager = new ToolSetManager(this);

        ToolSetManager.OnToolCallsStart += onToolCallsStart;
        ToolSetManager.OnToolCallInvoke += onToolCallInvoke;
        ToolSetManager.OnToolCallsFinish += onToolCallsFinish;
        ToolSetManager.OnToolExecuting += onToolCallExecuting;
        ToolSetManager.OnToolExecuted += onToolCallExecuted;

        BuildHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
    }
    public ChatClient(string apiUrl, string apiKey, string providerName, string modelName, string systemPrompt = null, bool enableThinking = false, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null, double? temperature = null, double? topP = null) : this(ModelProvider.Create(apiUrl, apiKey, providerName, modelName), modelName, systemPrompt, enableThinking, disableProxy, httpRequestTimeoutMilliseconds, temperature, topP)
    {
    }

    void BuildHttpClient(bool disableProxy, int? httpRequestTimeoutMilliseconds)
    {
        HttpClient?.Dispose();
        HttpClient = new ChatHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ModelProvider.ApiKey}");
    }
    public override void RebuildHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ModelProvider.ApiKey}");
    }
    public override void ClearHistory(string continuation = null)
    {
        var systemPrompt = SystemPrompt;
        MessageHistory.Clear();
        if (!systemPrompt.IsNullOrEmpty()) MessageHistory.Add(NativeMessageContent.Create(Role.System, systemPrompt));
        if (!continuation.IsNullOrEmpty()) MessageHistory.Add(NativeMessageContent.Create(Role.User, continuation));
    }
    public override void RollbackHistory(uint rounds = 1) => MessageHistory.RollbackRounds(rounds);

    public override async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(string content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        await foreach (var chunk in CompletionsStreamAsync(NativeMessageContent.Create(Role.User, content), chunks, tools, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public override async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(INativeMessage content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        await foreach (var chunk in CompletionsStreamAsync(MessageHistory, chunks, tools, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public override async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(NativeMessageCollection messageHistory, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        using var releaser = await BusyAsyncLock.LockAsync();
        BusyResetEvent.Reset();
        model ??= ModelName;
        chunks ??= [];
        while (true)
        {
            var request = new ChatCompletionsRequest(model, [.. messageHistory])
            {
                Temperature = Temperature,
                TopP = TopP,
                Tools = tools ?? Tools
            };
            request.SetEnableThinking(EnableThinking, ModelProvider.ApiUrl, ModelProvider.ProviderName, model);


            Channel<StateSet<bool, ChatCompletionsChunk>> channel = Channel.CreateUnbounded<StateSet<bool, ChatCompletionsChunk>>();
            bool channelClosed = false;
            var callbackTask = HttpClient.CompletionsStreamAsync(ModelProvider.ApiUrl + completionsUrl, request, async (chunkState) =>
            {
                try
                {
                    if (!channelClosed) await channel.Writer.WriteAsync(chunkState);
                    if (chunkState.State && chunkState.Data is not null)
                    {
                        chunks.Add(chunkState.Data);
                        if (!chunkState.Data.Choices.IsNullOrEmpty() && !chunkState.Data.Choices.FirstOrDefault().FinishReason.IsNullOrEmpty())
                            channelClosed = channel.Writer.TryComplete();
                    }
                    else channelClosed = channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channelClosed = channel.Writer.TryComplete(ex);
                }
            });

            await foreach (var chunk in channel.Reader.ReadAllAsync())
            {
                _ = onStreamOutput(chunk);
                yield return chunk;
            }

            var chunkStates = await callbackTask;
            if (chunkStates.State)
            {
                var result = Result.Create([.. chunkStates.Data], EnableThinking);
                _ = onStreamOutputCompleted(result);
                callTools:
                if (result.FinishReason == "stop")
                {
                    if (!result.ToolCalls.IsNullOrEmpty())
                    {
                        result.FinishReason = "tool_calls";
                        goto callTools;
                    }
                    messageHistory.Add(NativeMessageContent.Create(Role.Assistant, result.Content, reasoningContent: result.ReasoningContent));
                    break;
                }
                else if (result.FinishReason == "tool_calls")
                {
                    messageHistory.Add(NativeMessageContent.Create(Role.Assistant, result.Content, [.. result.ToolCalls], reasoningContent: result.ReasoningContent));
                    if (!result.ToolCalls.IsNullOrEmpty())
                    {
                        ToolCallParameter[] toolCallParameters = ToolCallParameter.Create(result.ToolCalls);
                        var toolCallResults = await ToolSetManager.ToolCalls(toolCallParameters);

                        if (toolCallParameters.Any(x => x.FunctionName == MemoryTool.ApplyMemoryToolFunctionName) && toolCallResults.Any(x => x.Result.State && x.Parameter.FunctionName == MemoryTool.ApplyMemoryToolFunctionName))
                        {
                            // 如果是纯记忆工具调用且成功，则不将工具调用结果添加到消息历史中，以免干扰模型的理解
                        }
                        else
                        {
                            foreach (var item in toolCallResults)
                            {
                                messageHistory.Add(NativeMessageContent.Create(Role.Tool, item.Result.ToJsonString(), item.Parameter.ToolCallId));
                            }
                        }
                        continue;
                    }
                    else break;
                }
                else break;
            }
            else break;
        }
        BusyResetEvent.Set();
    }

    public override Task<ChatCompletionsResponse> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions") => CompletionsAsync(NativeMessageContent.Create(Role.User, content), tools, model, completionsUrl);
    public override async Task<ChatCompletionsResponse> CompletionsAsync(INativeMessage content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        return await CompletionsAsync(MessageHistory, tools, model, completionsUrl);
    }
    public override async Task<ChatCompletionsResponse> CompletionsAsync(NativeMessageCollection messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        using var _ = await BusyAsyncLock.LockAsync();
        BusyResetEvent.Reset();
        model ??= ModelName;
        while (true)
        {
            var request = new ChatCompletionsRequest(model, [.. messageHistory]);
            request.Temperature = Temperature;
            request.TopP = TopP;
            request.SetEnableThinking(EnableThinking, ModelProvider.ApiUrl, ModelProvider.ProviderName, model);
            request.Tools = tools ?? Tools;

            var response = await HttpClient.CompletionsAsync(ModelProvider.ApiUrl + completionsUrl, request);

            ChatCompletionsChoice firstChoice = response.Data.Choices.FirstOrDefault();

            //temp ignore this event.
            //OnNativeClientChatFinished?.Invoke(Result.Create(response.Data.Choices));

            callTools:
            if (firstChoice?.FinishReason == "tool_calls")
            {
                messageHistory.Add(NativeMessageContent.Create(Role.Assistant, firstChoice?.Message?.Content, [.. firstChoice?.Message?.ToolCalls]));
                if (!firstChoice?.Message?.ToolCalls.IsNullOrEmpty() ?? false)
                {
                    ToolCallParameter[] toolCallParameters = ToolCallParameter.Create(firstChoice?.Message?.ToolCalls);
                    var toolCallResults = await ToolSetManager.ToolCalls(toolCallParameters);

                    if (toolCallParameters.Any(x => x.FunctionName == MemoryTool.ApplyMemoryToolFunctionName) && toolCallResults.Any(x => x.Result.State && x.Parameter.FunctionName == MemoryTool.ApplyMemoryToolFunctionName))
                    {
                        // 如果是纯记忆工具调用且成功，则不将工具调用结果添加到消息历史中，以免干扰模型的理解
                    }
                    else
                    {
                        foreach (var item in toolCallResults)
                        {
                            messageHistory.Add(NativeMessageContent.Create(Role.Tool, item.Result.ToJsonString(), item.Parameter.ToolCallId));
                        }
                    }
                    continue;
                }
            }
            else if (firstChoice?.FinishReason == "stop")
            {
                if (!firstChoice?.Message?.ToolCalls.IsNullOrEmpty() ?? false)
                {
                    firstChoice?.FinishReason = "tool_calls";
                    goto callTools;
                }
                messageHistory.Add(NativeMessageContent.Create(Role.Assistant, firstChoice?.Message?.Content));
            }

            BusyResetEvent.Set();
            return response.Data;
        }
    }


    public override void Dispose()
    {
        base.Dispose();
        HttpClient.Dispose();
        BusyResetEvent.Dispose();
        BusyAsyncLock.Dispose();
    }
}

