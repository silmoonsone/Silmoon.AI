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

namespace Silmoon.AI.Client.OpenAI;

public class NativeChatClient : INativeChatClient
{
    public event ToolCallInvokeHandler OnToolCallInvoke;
    public event Func<StateSet<bool, string>, Task<StateSet<bool, string>>> OnToolCallFinished;
    public string ApiUrl { get; set; }
    public string ApiKey { get; set; }
    public string Model { get; set; }
    SseHttpClient HttpClient { get; set; }
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

    public NativeChatClient(string apiUrl, string apiKey, string model, string systemPrompt = null)
    {
        ApiUrl = apiUrl;
        ApiKey = apiKey;
        Model = model;
        SystemPrompt = systemPrompt;

        BuildHttpClient();
    }

    void BuildHttpClient()
    {
        HttpClient?.Dispose();
        HttpClient = new SseHttpClient();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
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
        model ??= Model;
        while (true)
        {
            var request = new Request(model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking, ApiUrl, string.Empty, model);
            request.EnableSearch = EnableSearch;
            request.Tools = tools ?? Tools;


            Channel<StateSet<bool, Chunk>> channel = Channel.CreateUnbounded<StateSet<bool, Chunk>>();
            bool channelClosed = false;
            var callbackTask = HttpClient.CompletionsStreamAsync(ApiUrl + completionsUrl, request, async (chunkState) =>
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
                yield return chunk;
            }

            var chunkStates = await callbackTask;
            if (chunkStates.State)
            {
                var result = Result.Create([.. chunkStates.Data]);
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
                        string functionName = result.ToolCalls[0].Function.Name;
                        JObject parameters = result.ToolCalls[0].Function.Arguments.IsNullOrEmpty() ? [] : JsonConvert.DeserializeObject<JObject>(result.ToolCalls[0].Function.Arguments);
                        var toolCallResult = await ToolCall(functionName, parameters, result.ToolCalls[0].Id);

                        if (functionName != MemoryTool.ApplyMemoryToolFunctionName || !toolCallResult.State)
                        {
                            messageHistory.Add(MessageContent.Create(Role.Tool, toolCallResult.ToJsonString(), result.ToolCalls[0].Id));
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
        model ??= Model;
        while (true)
        {
            Request request = new Request(model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking, ApiUrl, string.Empty, model);
            request.EnableSearch = EnableSearch;
            request.Tools = tools ?? Tools;

            var response = await HttpClient.CompletionsAsync(ApiUrl + completionsUrl, request);

            Choice firstChoice = response.Data.Choices.FirstOrDefault();
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
                    string functionName = firstChoice?.Message?.ToolCalls[0].Function.Name;
                    JObject parameters = firstChoice?.Message?.ToolCalls[0].Function.Arguments.IsNullOrEmpty() ?? false ? [] : JsonConvert.DeserializeObject<JObject>(firstChoice?.Message?.ToolCalls[0].Function.Arguments);
                    var toolCallResult = await ToolCall(functionName, parameters, firstChoice?.Message?.ToolCalls[0].Id);

                    if (functionName != MemoryTool.ApplyMemoryToolFunctionName || !toolCallResult.State)
                    {
                        messageHistory.Add(MessageContent.Create(Role.Tool, toolCallResult.ToJsonString(), firstChoice?.Message?.ToolCalls[0].Id));
                    }
                    continue;
                }
                return response.Data;
            }
        }
    }

    async Task<StateSet<bool, string>> ToolCall(string functionName, JObject parameters, string toolCallId)
    {
        try
        {
            StateSet<bool, string> result = null;
            foreach (ToolCallInvokeHandler handler in OnToolCallInvoke.GetInvocationList().Cast<ToolCallInvokeHandler>())
            {
                var tmpResult = await handler(functionName, parameters, toolCallId, result);
                if (tmpResult is not null) result = tmpResult;
            }
            result ??= false.ToStateSet<string>(null, $"function {functionName} not implemented.");
            result = await (OnToolCallFinished?.Invoke(result) ?? Task.FromResult(result));
            return result;
        }
        catch (Exception ex)
        {
            return false.ToStateSet<string>(null, $"执行函数 {functionName} 时发生异常: {ex.Message}");
        }
    }

}