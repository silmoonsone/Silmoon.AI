using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.Tools;
using Silmoon.Extensions;
using Silmoon.Models;
using Silmoon.Threading;

namespace Silmoon.AI.OpenAI;

public class ResponsesClient : INativeClient
{
    public event ToolCallsStartHandler OnToolCallsStart;
    public event ToolCallInvokeHandler OnToolCallInvoke;
    public event ToolExecutingHandler OnToolExecuting;
    public event ToolExecutedHandler OnToolExecuted;
    public event ToolCallsFinishHandler OnToolCallsFinish;
    public event StreamOutputHandler OnStreamOutput;
    public event StreamOutputCompletedHandler OnStreamOutputCompleted;

    public ModelProvider ModelProvider { get; set; }
    public string ModelName { get; set; }
    public ExecuteToolManager ExecuteToolManager { get; set; }
    public bool EnableThinking { get; set; } = false;
    public List<Tool> Tools { get; set; } = [];
    public NativeMessageCollection MessageHistory { get; set; } = [];
    public ManualResetEvent BusyResetEvent { get; private set; } = new(true);
    public AsyncLock BusyAsyncLock { get; private set; } = new();

    ResponsesHttpClient HttpClient { get; set; }

    public string SystemPrompt
    {
        get => (MessageHistory.FirstOrDefault(m => m.Role == Role.System) as NativeMessageContent)?.Content;
        set
        {
            var systemMessage = MessageHistory.FirstOrDefault(m => m.Role == Role.System) as NativeMessageContent;
            if (value is null)
            {
                if (systemMessage is not null) MessageHistory.Remove(systemMessage);
            }
            else
            {
                if (systemMessage is null) MessageHistory.Insert(0, NativeMessageContent.Create(Role.System, value));
                else systemMessage.Content = value;
            }
        }
    }

    public ResponsesClient(ModelProvider modelProvider, string modelName, string systemPrompt = null, bool enableThinking = false, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null)
    {
        ModelProvider = modelProvider;
        ModelName = modelName;
        SystemPrompt = systemPrompt;
        EnableThinking = enableThinking;
        ExecuteToolManager = new ExecuteToolManager(this);
        ExecuteToolManager.OnToolCallsStart += async p => await (OnToolCallsStart?.Invoke(p) ?? Task.CompletedTask);
        ExecuteToolManager.OnToolCallInvoke += async (p, r) => OnToolCallInvoke is null ? r : await OnToolCallInvoke.Invoke(p, r);
        ExecuteToolManager.OnToolCallsFinish += async (p, r) => OnToolCallsFinish is null ? r : await OnToolCallsFinish.Invoke(p, r);
        ExecuteToolManager.OnToolExecuting += async (name, p) => await (OnToolExecuting?.Invoke(name, p) ?? Task.CompletedTask);
        ExecuteToolManager.OnToolExecuted += async (name, p, r) => await (OnToolExecuted?.Invoke(name, p, r) ?? Task.CompletedTask);
        BuildHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
    }
    public ResponsesClient(string apiUrl, string apiKey, string providerName, string modelName, string systemPrompt = null, bool enableThinking = false, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null)
        : this(ModelProvider.Create(apiUrl, apiKey, providerName, modelName), modelName, systemPrompt, enableThinking, disableProxy, httpRequestTimeoutMilliseconds)
    {
    }

    void BuildHttpClient(bool disableProxy, int? httpRequestTimeoutMilliseconds)
    {
        HttpClient?.Dispose();
        HttpClient = new ResponsesHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
        RebuildHttpClient();
    }

    public void RebuildHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        if (!ModelProvider.ApiKey.IsNullOrEmpty())
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ModelProvider.ApiKey}");
    }

    public void ClearHistory(string? continuation = null)
    {
        var systemPrompt = SystemPrompt;
        MessageHistory.Clear();
        if (!systemPrompt.IsNullOrEmpty()) MessageHistory.Add(NativeMessageContent.Create(Role.System, systemPrompt));
        if (!continuation.IsNullOrEmpty()) MessageHistory.Add(NativeMessageContent.Create(Role.User, continuation));
    }

    public void RollbackHistory(uint rounds = 1) => MessageHistory.RollbackRounds(rounds);

    public async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(string content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses")
    {
        await foreach (var chunk in CompletionsStreamAsync(NativeMessageContent.Create(Role.User, content), chunks, tools, model, completionsUrl))
            yield return chunk;
    }

    public async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(INativeMessage content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses")
    {
        MessageHistory.Add(content);
        await foreach (var chunk in CompletionsStreamAsync(MessageHistory, chunks, tools, model, completionsUrl))
            yield return chunk;
    }

    public async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(NativeMessageCollection messageHistory, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses")
    {
        using var _ = await BusyAsyncLock.LockAsync();
        BusyResetEvent.Reset();
        model ??= ModelName;
        chunks ??= [];
        try
        {
            while (true)
            {
                var request = CreateRequest(model, messageHistory, tools ?? Tools, true);
                Channel<StateSet<bool, ChatCompletionsChunk>> channel = Channel.CreateUnbounded<StateSet<bool, ChatCompletionsChunk>>();
                List<ResponsesStreamEvent> streamEvents = [];
                ResponsesResponse completedResponse = null;

                var callbackTask = HttpClient.ResponsesStreamAsync(BuildUrl(completionsUrl), request, async state =>
                {
                    if (!state.State)
                    {
                        await channel.Writer.WriteAsync(false.ToStateSet<ChatCompletionsChunk>(null, state.Message));
                        channel.Writer.TryComplete();
                        return;
                    }

                    if (state.Data is null)
                    {
                        channel.Writer.TryComplete();
                        return;
                    }

                    streamEvents.Add(state.Data);
                    if (state.Data.Type == "response.completed") completedResponse = state.Data.Response;
                    var chunk = ConvertStreamEvent(state.Data, model);
                    if (chunk is not null)
                    {
                        chunks.Add(chunk);
                        await channel.Writer.WriteAsync(true.ToStateSet(chunk));
                    }
                    if (state.Data.Type is "response.completed" or "response.failed" or "response.incomplete")
                        channel.Writer.TryComplete();
                });

                await foreach (var item in channel.Reader.ReadAllAsync())
                {
                    await (OnStreamOutput?.Invoke(item) ?? Task.CompletedTask);
                    yield return item;
                }

                var nativeStates = await callbackTask;
                if (!nativeStates.State) break;

                var result = completedResponse is not null ? BuildResult(completedResponse) : BuildResultFromStreamEvents(streamEvents);
                await (OnStreamOutputCompleted?.Invoke(result) ?? Task.CompletedTask);
                if (result.FinishReason == "tool_calls" && !result.ToolCalls.IsNullOrEmpty())
                {
                    messageHistory.Add(NativeMessageContent.Create(Role.Assistant, result.Content, [.. result.ToolCalls]));
                    var parameters = ToolCallParameter.Create(result.ToolCalls);
                    var toolCallResults = await ExecuteToolManager.ToolCalls(parameters);
                    AddToolResults(messageHistory, parameters, toolCallResults);
                    continue;
                }

                messageHistory.Add(NativeMessageContent.Create(Role.Assistant, result.Content, reasoningContent: result.ReasoningContent));
                break;
            }
        }
        finally
        {
            BusyResetEvent.Set();
        }
    }

    public Task<ChatCompletionsResponse> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses") => CompletionsAsync(NativeMessageContent.Create(Role.User, content), tools, model, completionsUrl);
    public async Task<ChatCompletionsResponse> CompletionsAsync(INativeMessage content, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses")
    {
        MessageHistory.Add(content);
        return await CompletionsAsync(MessageHistory, tools, model, completionsUrl);
    }

    public async Task<ChatCompletionsResponse> CompletionsAsync(NativeMessageCollection messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses")
    {
        using var _ = await BusyAsyncLock.LockAsync();
        BusyResetEvent.Reset();
        model ??= ModelName;
        try
        {
            while (true)
            {
                var request = CreateRequest(model, messageHistory, tools ?? Tools, false);
                var responseState = await HttpClient.ResponsesAsync(BuildUrl(completionsUrl), request);
                if (!responseState.State)
                    return CreateErrorResponse(model, responseState.Message);

                var response = ConvertResponse(responseState.Data);
                var firstChoice = response.Choices.FirstOrDefault();
                if (firstChoice?.FinishReason == "tool_calls" && !firstChoice.Message.ToolCalls.IsNullOrEmpty())
                {
                    messageHistory.Add(NativeMessageContent.Create(Role.Assistant, firstChoice.Message.Content, [.. firstChoice.Message.ToolCalls]));
                    var parameters = ToolCallParameter.Create(firstChoice.Message.ToolCalls);
                    var toolCallResults = await ExecuteToolManager.ToolCalls(parameters);
                    AddToolResults(messageHistory, parameters, toolCallResults);
                    continue;
                }

                if (firstChoice is not null)
                    messageHistory.Add(NativeMessageContent.Create(Role.Assistant, firstChoice.Message.Content));
                return response;
            }
        }
        finally
        {
            BusyResetEvent.Set();
        }
    }

    ResponsesRequest CreateRequest(string model, NativeMessageCollection messageHistory, List<Tool> tools, bool stream)
    {
        var request = new ResponsesRequest(model, CreateInput(messageHistory), SystemPrompt, stream);
        request.SetEnableThinking(EnableThinking, ModelProvider.ApiUrl, ModelProvider.ProviderName, model);
        request.Tools = CreateTools(tools);
        return request;
    }

    static JArray CreateInput(IEnumerable<INativeMessage> messageHistory)
    {
        JArray input = [];
        foreach (var message in messageHistory ?? [])
        {
            if (message.Role == Role.System) continue;
            if (message.Role == Role.Tool)
            {
                input.Add(new JObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = message.ToolCallId,
                    ["output"] = message.GetContent() ?? string.Empty,
                });
                continue;
            }

            if (message.Role == Role.Assistant && !message.ToolCalls.IsNullOrEmpty())
            {
                if (!message.GetContent().IsNullOrEmpty())
                    input.Add(CreateMessageInput("assistant", message.GetContent()));
                foreach (var toolCall in message.ToolCalls)
                    input.Add(CreateFunctionCallInput(toolCall));
                continue;
            }

            input.Add(CreateMessageInput(message.Role.ToString().ToLowerInvariant(), message.GetContent() ?? string.Empty));
        }
        return input;
    }

    static JObject CreateMessageInput(string role, string content) => new()
    {
        ["role"] = role,
        ["content"] = content,
    };

    static JObject CreateFunctionCallInput(ToolCall toolCall) => new()
    {
        ["type"] = "function_call",
        ["call_id"] = toolCall.Id,
        ["name"] = toolCall.Function?.Name,
        ["arguments"] = toolCall.Function?.Arguments ?? "{}",
    };

    static JArray CreateTools(List<Tool> tools)
    {
        if (tools.IsNullOrEmpty()) return null;
        JArray result = [];
        foreach (var tool in tools)
        {
            if (tool.Type == "function" && tool.Function is not null)
            {
                result.Add(new JObject
                {
                    ["type"] = "function",
                    ["name"] = tool.Function.Name,
                    ["description"] = tool.Function.Description,
                    ["parameters"] = JObject.FromObject(tool.Function.Parameters, JsonSerializer.Create(NativeApiJson.SerializerSettings)),
                });
            }
            else result.Add(JObject.FromObject(tool, JsonSerializer.Create(NativeApiJson.SerializerSettings)));
        }
        return result;
    }

    void AddToolResults(NativeMessageCollection messageHistory, ToolCallParameter[] parameters, ToolCallResult[] toolCallResults)
    {
        if (parameters.Any(x => x.FunctionName == MemoryTool.ApplyMemoryToolFunctionName) && toolCallResults.Any(x => x.Result.State && x.Parameter.FunctionName == MemoryTool.ApplyMemoryToolFunctionName))
            return;

        foreach (var item in toolCallResults)
            messageHistory.Add(NativeMessageContent.Create(Role.Tool, item.Result.ToJsonString(), item.Parameter.ToolCallId));
    }

    string BuildUrl(string completionsUrl)
    {
        var baseUrl = ModelProvider.ApiUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(completionsUrl)) completionsUrl = "/v1/responses";
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) && completionsUrl.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            completionsUrl = completionsUrl[3..];
        return baseUrl + "/" + completionsUrl.TrimStart('/');
    }

    static ChatCompletionsResponse ConvertResponse(ResponsesResponse response)
    {
        var result = BuildResult(response);
        return new ChatCompletionsResponse
        {
            Id = response.Id,
            Model = response.Model,
            Object = response.Object,
            Created = ToUnixCreated(response.CreatedAt),
            Choices =
            [
                new ChatCompletionsChoice
                {
                    Index = 0,
                    FinishReason = result.FinishReason,
                    Message = NativeMessageContent.Create(Role.Assistant, result.Content, result.ToolCalls, result.ReasoningContent),
                }
            ],
        };
    }

    static ChatCompletionsResponse CreateErrorResponse(string model, string message) => new()
    {
        Model = model,
        Object = "response",
        Created = ToUnixCreated(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        Choices =
        [
            new ChatCompletionsChoice
            {
                Index = 0,
                FinishReason = "error",
                Message = NativeMessageContent.Create(Role.Assistant, message),
            }
        ],
    };

    static Result BuildResult(ResponsesResponse response)
    {
        var result = new Result
        {
            Role = Role.Assistant,
            FinishReason = response?.Status == "completed" ? "stop" : response?.Status ?? "stop",
            Usage = response?.Usage?.ToUsage(),
        };

        int index = 0;
        foreach (var item in response?.Output ?? [])
        {
            if (item.Type == "message")
            {
                foreach (var part in item.Content ?? [])
                {
                    if (part.Type == "output_text" || part.Type == "text")
                        result.Content += part.Text;
                }
            }
            else if (item.Type == "function_call")
            {
                result.ToolCalls.Add(new ToolCall
                {
                    Index = index++,
                    Id = item.CallId,
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = item.Name,
                        Arguments = item.Arguments ?? "{}",
                    },
                });
            }
        }

        if (!result.ToolCalls.IsNullOrEmpty()) result.FinishReason = "tool_calls";
        return result;
    }

    static Result BuildResultFromStreamEvents(IEnumerable<ResponsesStreamEvent> events)
    {
        var response = events.LastOrDefault(x => x.Response is not null)?.Response;
        if (response is not null) return BuildResult(response);

        var result = new Result { Role = Role.Assistant, FinishReason = "stop" };
        Dictionary<int, ToolCall> toolCalls = [];
        foreach (var item in events ?? [])
        {
            if (item.Type == "response.output_text.delta") result.Content += item.Delta;
            else if (item.Type == "response.output_item.added" && item.Item?.Type == "function_call")
            {
                var index = item.OutputIndex ?? toolCalls.Count;
                toolCalls[index] = new ToolCall
                {
                    Index = index,
                    Id = item.Item.CallId,
                    Type = "function",
                    Function = new ToolCallFunction { Name = item.Item.Name, Arguments = string.Empty },
                };
            }
            else if (item.Type == "response.function_call_arguments.delta")
            {
                var index = item.OutputIndex ?? 0;
                if (!toolCalls.TryGetValue(index, out var toolCall))
                {
                    toolCall = new ToolCall { Index = index, Type = "function", Function = new ToolCallFunction { Arguments = string.Empty } };
                    toolCalls[index] = toolCall;
                }
                toolCall.Function ??= new ToolCallFunction();
                toolCall.Function.Arguments += item.Delta;
            }
            else if (item.Type == "response.function_call_arguments.done")
            {
                var index = item.OutputIndex ?? 0;
                if (!toolCalls.TryGetValue(index, out var toolCall))
                {
                    toolCall = new ToolCall { Index = index, Type = "function", Function = new ToolCallFunction() };
                    toolCalls[index] = toolCall;
                }
                toolCall.Function ??= new ToolCallFunction();
                toolCall.Function.Name ??= item.Name;
                toolCall.Function.Arguments = item.Arguments ?? toolCall.Function.Arguments ?? "{}";
            }
            else if (item.Type == "response.output_item.done" && item.Item?.Type == "function_call")
            {
                var index = item.OutputIndex ?? toolCalls.Count;
                toolCalls[index] = new ToolCall
                {
                    Index = index,
                    Id = item.Item.CallId,
                    Type = "function",
                    Function = new ToolCallFunction { Name = item.Item.Name, Arguments = item.Item.Arguments ?? "{}" },
                };
            }
        }

        result.ToolCalls = [.. toolCalls.OrderBy(x => x.Key).Select(x => x.Value)];
        if (!result.ToolCalls.IsNullOrEmpty()) result.FinishReason = "tool_calls";
        return result;
    }

    static ChatCompletionsChunk ConvertStreamEvent(ResponsesStreamEvent item, string model)
    {
        if (item.Type == "response.output_text.delta")
            return CreateChunk(model, new ChatCompletionsDelta { Content = item.Delta });

        if (item.Type == "response.output_item.added" && item.Item?.Type == "function_call")
            return CreateChunk(model, new ChatCompletionsDelta
            {
                ToolCalls =
                [
                    new ToolCall
                    {
                        Index = item.OutputIndex ?? 0,
                        Id = item.Item.CallId,
                        Type = "function",
                        Function = new ToolCallFunction { Name = item.Item.Name, Arguments = string.Empty },
                    }
                ],
            });

        if (item.Type == "response.function_call_arguments.delta")
            return CreateChunk(model, new ChatCompletionsDelta
            {
                ToolCalls =
                [
                    new ToolCall
                    {
                        Index = item.OutputIndex ?? 0,
                        Function = new ToolCallFunction { Arguments = item.Delta },
                    }
                ],
            });

        if (item.Type == "response.completed")
        {
            var result = BuildResult(item.Response);
            return CreateChunk(model, new ChatCompletionsDelta(), result.FinishReason, result.Usage);
        }

        if (item.Type is "response.failed" or "response.incomplete")
            return CreateChunk(model, new ChatCompletionsDelta(), item.Type.Replace("response.", string.Empty));

        return null;
    }

    static ChatCompletionsChunk CreateChunk(string model, ChatCompletionsDelta delta, string finishReason = null, Usage usage = null) => new()
    {
        Model = model,
        Object = "chat.completion.chunk",
        Created = ToUnixCreated(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        Usage = usage,
        Choices =
        [
            new ChatCompletionsChunkChoice
            {
                Index = 0,
                Delta = delta,
                FinishReason = finishReason,
            }
        ],
    };

    static int ToUnixCreated(long value)
    {
        if (value <= 0) return 0;
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    public void Dispose()
    {
        OnToolCallsStart = null;
        OnToolCallInvoke = null;
        OnToolCallsFinish = null;
        OnToolExecuting = null;
        OnToolExecuted = null;
        OnStreamOutput = null;
        OnStreamOutputCompleted = null;
        Tools?.Clear();
        MessageHistory?.Clear();
        HttpClient?.Dispose();
        BusyResetEvent?.Dispose();
        BusyAsyncLock?.Dispose();
    }
}
