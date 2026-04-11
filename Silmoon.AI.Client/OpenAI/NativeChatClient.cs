using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI.Enums;
using Silmoon.AI.Client.OpenAI.Interfaces;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.AI.Client.ToolCall;
using Silmoon.Extensions;
using Silmoon.Models;
using System.Threading.Channels;

namespace Silmoon.AI.Client.OpenAI;

public class NativeChatClient : INativeApiClient
{
    public event ToolCallInvokeHandler OnToolCallInvoke;
    public event Func<StateSet<bool, MessageContent>, Task<StateSet<bool, MessageContent>>> OnToolCallFinished;
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
    public Tool[] Tools { get; set; } = [];

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
    public void ClearHistory()
    {
        var systemMessage = MessageHistory.FirstOrDefault(m => m.Role == Role.System);
        if (systemMessage is not null) MessageHistory = [systemMessage];
        else MessageHistory.Clear();
    }


    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, List<Chunk> chunks = null, Tool[] tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        await foreach (var chunk in CompletionsStreamAsync(MessageContent.Create(Role.User, content), chunks, tools, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(MessageContent content, List<Chunk> chunks = null, Tool[] tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        await foreach (var chunk in CompletionsStreamAsync(MessageHistory, chunks, tools, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(List<MessageContent> messageHistory, List<Chunk> chunks = null, Tool[] tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        chunks ??= [];
        while (true)
        {
            var request = new Request(model ?? Model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking);
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
                        if (toolCallResult.State) messageHistory.Add(toolCallResult.Data);
                        else messageHistory.Add(MessageContent.Create(Role.Tool, toolCallResult.ToJsonString(), result.ToolCalls[0].Id));
                        continue;
                    }
                    else break;
                }
                else break;
            }
            else break;
        }
    }

    public async Task<Response> CompletionsAsync(string content, Tool[] tools = null, string model = null, string completionsUrl = "/chat/completions") => await CompletionsAsync(MessageContent.Create(Role.User, content), tools, model, completionsUrl);
    public async Task<Response> CompletionsAsync(MessageContent content, Tool[] tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        return await CompletionsAsync(MessageHistory, tools, model, completionsUrl);
    }
    public async Task<Response> CompletionsAsync(List<MessageContent> messageHistory, Tool[] tools = null, string model = null, string completionsUrl = "/chat/completions")
    {
        model ??= Model;
        while (true)
        {
            Request request = new Request(model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking);
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
                    if (toolCallResult.State) messageHistory.Add(toolCallResult.Data);
                    else messageHistory.Add(MessageContent.Create(Role.Tool, false.ToStateSet(toolCallResult.Message).ToJsonString(), firstChoice?.Message?.ToolCalls[0].Id));
                    continue;
                }
                return response.Data;
            }
        }
    }

    public async Task<StateSet<bool, MessageContent>> ToolCall(string functionName, JObject parameters, string toolCallId)
    {
        try
        {
            var result = await (OnToolCallInvoke?.Invoke(functionName, parameters, toolCallId) ?? Task.FromResult<StateSet<bool, MessageContent>>(null));
            result = await (OnToolCallFinished?.Invoke(result) ?? Task.FromResult(result));
            return result ?? false.ToStateSet<MessageContent>(null, $"function {functionName} not implemented.");
        }
        catch (Exception ex)
        {
            return false.ToStateSet<MessageContent>(null, $"执行函数 {functionName} 时发生异常: {ex.Message}");
        }
    }

}

public delegate Task<StateSet<bool, MessageContent>> ToolCallInvokeHandler(string functionName, JObject parameters, string toolCallId);