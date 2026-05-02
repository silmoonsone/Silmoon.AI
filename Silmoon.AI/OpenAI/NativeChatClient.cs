using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Handlers;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System.Threading.Channels;
using Silmoon.AI.Tools;
using Silmoon.AI.Models;
using System.Collections.Concurrent;

namespace Silmoon.AI.OpenAI;

public class NativeChatClient : INativeChatClient
{
    public event ToolCallStartHandler OnToolCallStart;
    public event ToolCallCompletedHandler OnToolCallCompleted;
    public event StreamOutputCompletedHandler OnStreamOutputCompleted;
    public event StreamOutputHandler OnStreamOutput;
    public ModelProvider ModelProvider { get; set; }
    public string ModelName { get; set; }
    SseHttpClient HttpClient { get; set; }
    public ExecuteToolManager ExecuteToolManager { get; set; }

    public bool EnableThinking { get; set; } = false;
    public bool EnableSearch { get; set; } = false;
    public List<MessageContent> MessageHistory { get; set; } = [];

    public string SystemPrompt
    {
        set
        {
            var systemMessage = MessageHistory.FirstOrDefault(m => m.Role == Role.System);
            if (value is null)
            {
                if (systemMessage is not null) MessageHistory.Remove(systemMessage);
            }
            else
            {
                if (systemMessage is null) MessageHistory.Insert(0, MessageContent.Create(Role.System, value));
                else systemMessage.Content = value;
            }
        }
        get => MessageHistory.FirstOrDefault(m => m.Role == Role.System)?.Content;
    }
    public List<Tool> Tools { get; set; } = [];

    public NativeChatClient(ModelProvider provider, string modelName, string systemPrompt = null, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null)
    {
        ModelProvider = provider;
        ModelName = modelName;
        SystemPrompt = systemPrompt;
        ExecuteToolManager = new ExecuteToolManager(this);

        BuildHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
    }
    public NativeChatClient(string apiUrl, string apiKey, string modelName, string systemPrompt = null, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null) : this(ModelProvider.Create(apiUrl, apiKey, modelName), modelName, systemPrompt, disableProxy, httpRequestTimeoutMilliseconds)
    {
    }

    void BuildHttpClient(bool disableProxy, int? httpRequestTimeoutMilliseconds)
    {
        HttpClient?.Dispose();
        HttpClient = new SseHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ModelProvider.ApiKey}");
    }
    public void ResetHistory(string continuation = null)
    {
        var systemPrompt = SystemPrompt;
        MessageHistory.Clear();
        if (!systemPrompt.IsNullOrEmpty()) MessageHistory.Add(MessageContent.Create(Role.System, systemPrompt));
        if (!continuation.IsNullOrEmpty()) MessageHistory.Add(MessageContent.Create(Role.User, continuation));
    }


    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        await foreach (var chunk in CompletionsStreamAsync(MessageContent.Create(Role.User, content), chunks, tools, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(MessageContent content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        await foreach (var chunk in CompletionsStreamAsync(MessageHistory, chunks, tools, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(List<MessageContent> messageHistory, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        chunks ??= [];
        model ??= ModelName;
        while (true)
        {
            var request = new Request(model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking, ModelProvider.ApiUrl, ModelProvider.ProviderName, model);
            request.EnableSearch = EnableSearch;
            request.Tools = tools ?? Tools;


            Channel<StateSet<bool, Chunk>> channel = Channel.CreateUnbounded<StateSet<bool, Chunk>>();
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
                OnStreamOutput?.Invoke(chunk);
                yield return chunk;
            }

            var chunkStates = await callbackTask;
            if (chunkStates.State)
            {
                var result = Result.Create([.. chunkStates.Data]);
                OnStreamOutputCompleted?.Invoke(result);
                if (result.FinishReason == "stop")
                {
                    messageHistory.Add(MessageContent.Create(Role.Assistant, result.Content));
                    break;
                }
                else if (result.FinishReason == "tool_calls")
                {
                    messageHistory.Add(MessageContent.Create(Role.Assistant, result.Content, [.. result.ToolCalls]));
                    if (!result.ToolCalls.IsNullOrEmpty())
                    {
                        ToolCallParameter[] toolCallParameters = ToolCallParameter.Create(result.ToolCalls);
                        var toolCallResults = await ExecuteToolManager.ToolCalls(toolCallParameters, OnToolCallStart, OnToolCallCompleted);

                        if (toolCallParameters.Any(x => x.FunctionName == MemoryTool.ApplyMemoryToolFunctionName) && toolCallResults.Any(x => x.Result.State && x.Parameter.FunctionName == MemoryTool.ApplyMemoryToolFunctionName))
                        {
                            // 如果是纯记忆工具调用且成功，则不将工具调用结果添加到消息历史中，以免干扰模型的理解
                        }
                        else
                        {
                            foreach (var item in toolCallResults)
                            {
                                messageHistory.Add(MessageContent.Create(Role.Tool, item.Result.ToJsonString(), item.Parameter.ToolCallId));
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
    }

    public async Task<Response> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions") => await CompletionsAsync(MessageContent.Create(Role.User, content), tools, model, completionsUrl);
    public async Task<Response> CompletionsAsync(MessageContent content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        return await CompletionsAsync(MessageHistory, tools, model, completionsUrl);
    }
    public async Task<Response> CompletionsAsync(List<MessageContent> messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        model ??= ModelName;
        while (true)
        {
            Request request = new Request(model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking, ModelProvider.ApiUrl, ModelProvider.ProviderName, model);
            request.EnableSearch = EnableSearch;
            request.Tools = tools ?? Tools;

            var response = await HttpClient.CompletionsAsync(ModelProvider.ApiUrl + completionsUrl, request);

            Choice firstChoice = response.Data.Choices.FirstOrDefault();

            //temp ignore this event.
            //OnNativeClientChatFinished?.Invoke(Result.Create(response.Data.Choices));

            if (firstChoice?.FinishReason == "stop")
            {
                messageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice?.Message?.Content));
                return response.Data;
            }
            else if (firstChoice?.FinishReason == "tool_calls")
            {
                messageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice?.Message?.Content, [.. firstChoice?.Message?.ToolCalls]));
                if (!firstChoice?.Message?.ToolCalls.IsNullOrEmpty() ?? false)
                {
                    ToolCallParameter[] toolCallParameters = ToolCallParameter.Create(firstChoice?.Message?.ToolCalls);
                    var toolCallResults = await ExecuteToolManager.ToolCalls(toolCallParameters, OnToolCallStart, OnToolCallCompleted);

                    if (toolCallParameters.Any(x => x.FunctionName == MemoryTool.ApplyMemoryToolFunctionName) && toolCallResults.Any(x => x.Result.State && x.Parameter.FunctionName == MemoryTool.ApplyMemoryToolFunctionName))
                    {
                        // 如果是纯记忆工具调用且成功，则不将工具调用结果添加到消息历史中，以免干扰模型的理解
                    }
                    else
                    {
                        foreach (var item in toolCallResults)
                        {
                            messageHistory.Add(MessageContent.Create(Role.Tool, item.Result.ToJsonString(), item.Parameter.ToolCallId));
                        }
                    }
                    continue;
                }
                return response.Data;
            }
        }
    }



    public void Dispose()
    {
        OnToolCallStart = null;
        OnToolCallCompleted = null;
        OnStreamOutput = null;
        OnStreamOutputCompleted = null;
        Tools.Clear();
        Tools = null;
        MessageHistory.Clear();
        MessageHistory = null;
        HttpClient.Dispose();
    }
}