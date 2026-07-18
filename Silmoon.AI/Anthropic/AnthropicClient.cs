using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Anthropic.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI.Models;
using Silmoon.AI.Tools;
using Silmoon.Extensions;
using Silmoon.Models;
using Silmoon.Threading;

namespace Silmoon.AI.Anthropic;

public class AnthropicClient : INativeClient
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
    public List<Tool> Tools { get; set; } = [];
    public MessageCollection MessageHistory { get; set; } = [];
    public bool EnableThinking { get; set; } = false;
    public ManualResetEvent BusyResetEvent { get; private set; } = new(true);
    public AsyncLock BusyAsyncLock { get; private set; } = new();

    AnthropicHttpClient HttpClient { get; set; }

    public string SystemPrompt
    {
        get => (MessageHistory.FirstOrDefault(m => m.Role == Role.System) as MessageContent)?.Content;
        set
        {
            var systemMessage = MessageHistory.FirstOrDefault(m => m.Role == Role.System) as MessageContent;
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
    }

    public AnthropicClient(ModelProvider provider, string modelName, string systemPrompt = null, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null)
    {
        ModelProvider = provider;
        ModelName = modelName;
        SystemPrompt = systemPrompt;
        ExecuteToolManager = new ExecuteToolManager(this);
        ExecuteToolManager.OnToolCallsStart += async p => await (OnToolCallsStart?.Invoke(p) ?? Task.CompletedTask);
        ExecuteToolManager.OnToolCallInvoke += async (p, r) => OnToolCallInvoke is null ? r : await OnToolCallInvoke.Invoke(p, r);
        ExecuteToolManager.OnToolCallsFinish += async (p, r) => OnToolCallsFinish is null ? r : await OnToolCallsFinish.Invoke(p, r);
        ExecuteToolManager.OnToolExecuting += async (name, p) => await (OnToolExecuting?.Invoke(name, p) ?? Task.CompletedTask);
        ExecuteToolManager.OnToolExecuted += async (name, p, r) => await (OnToolExecuted?.Invoke(name, p, r) ?? Task.CompletedTask);
        BuildHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
    }
    public AnthropicClient(string apiUrl, string apiKey, string providerName, string modelName, string systemPrompt = null, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null)
        : this(ModelProvider.Create(apiUrl, apiKey, providerName, modelName), modelName, systemPrompt, disableProxy, httpRequestTimeoutMilliseconds)
    {
    }

    void BuildHttpClient(bool disableProxy, int? httpRequestTimeoutMilliseconds)
    {
        HttpClient?.Dispose();
        HttpClient = new AnthropicHttpClient(disableProxy, httpRequestTimeoutMilliseconds);
        RebuildHttpClient();
    }

    public void RebuildHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        if (!ModelProvider.ApiKey.IsNullOrEmpty())
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ModelProvider.ApiKey}");
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", ModelProvider.AnthropicVersion.IsNullOrEmpty() ? "2023-06-01" : ModelProvider.AnthropicVersion);
    }

    public void ClearHistory(string? continuation = null)
    {
        var systemPrompt = SystemPrompt;
        MessageHistory.Clear();
        if (!systemPrompt.IsNullOrEmpty()) MessageHistory.Add(MessageContent.Create(Role.System, systemPrompt));
        if (!continuation.IsNullOrEmpty()) MessageHistory.Add(MessageContent.Create(Role.User, continuation));
    }

    public void RollbackHistory(uint rounds = 1) => MessageHistory.RollbackRounds(rounds);
    public async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(string content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/messages")
    {
        await foreach (var chunk in CompletionsStreamAsync(MessageContent.Create(Role.User, content), chunks, tools, model, completionsUrl))
            yield return chunk;
    }
    public async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(IMessage content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/messages")
    {
        MessageHistory.Add(content);
        await foreach (var chunk in CompletionsStreamAsync(MessageHistory, chunks, tools, model, completionsUrl))
            yield return chunk;
    }
    public async IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(MessageCollection messageHistory, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/messages")
    {
        using var _ = await BusyAsyncLock.LockAsync();
        BusyResetEvent.Reset();
        model ??= ModelName;
        chunks ??= [];
        try
        {
            while (true)
            {
                var request = AnthropicMessageAdapter.CreateRequest(model, messageHistory, SystemPrompt, tools ?? Tools);
                Channel<StateSet<bool, ChatCompletionsChunk>> channel = Channel.CreateUnbounded<StateSet<bool, ChatCompletionsChunk>>();
                List<AnthropicStreamEvent> streamEvents = [];
                var callbackTask = HttpClient.MessagesStreamAsync(BuildUrl(completionsUrl), request, async state =>
                {
                    if (!state.State)
                    {
                        await channel.Writer.WriteAsync(false.ToStateSet<ChatCompletionsChunk>(null, state.Message));
                        channel.Writer.TryComplete();
                        return;
                    }

                    streamEvents.Add(state.Data);
                    var chunk = ConvertStreamEvent(state.Data, model);
                    if (chunk is not null)
                    {
                        chunks.Add(chunk);
                        await channel.Writer.WriteAsync(true.ToStateSet(chunk));
                    }
                    if (state.Data.Type == "message_stop") channel.Writer.TryComplete();
                });

                await foreach (var item in channel.Reader.ReadAllAsync())
                {
                    await (OnStreamOutput?.Invoke(item) ?? Task.CompletedTask);
                    yield return item;
                }

                var nativeStates = await callbackTask;
                if (!nativeStates.State) break;

                var result = BuildResultFromAnthropicEvents(streamEvents);
                await (OnStreamOutputCompleted?.Invoke(result) ?? Task.CompletedTask);
                if (result.FinishReason == "tool_calls" && !result.ToolCalls.IsNullOrEmpty())
                {
                    messageHistory.Add(MessageContent.Create(Role.Assistant, result.Content, [.. result.ToolCalls]));
                    var parameters = ToolCallParameter.Create(result.ToolCalls);
                    var toolCallResults = await ExecuteToolManager.ToolCalls(parameters);
                    if (parameters.Any(x => x.FunctionName == MemoryTool.ApplyMemoryToolFunctionName) && toolCallResults.Any(x => x.Result.State && x.Parameter.FunctionName == MemoryTool.ApplyMemoryToolFunctionName))
                    {
                    }
                    else
                    {
                        foreach (var item in toolCallResults)
                            messageHistory.Add(MessageContent.Create(Role.Tool, item.Result.ToJsonString(), item.Parameter.ToolCallId));
                    }
                    continue;
                }

                messageHistory.Add(MessageContent.Create(Role.Assistant, result.Content));
                break;
            }
        }
        finally
        {
            BusyResetEvent.Set();
        }
    }

    public Task<ChatCompletionsResponse> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/messages") => CompletionsAsync(MessageContent.Create(Role.User, content), tools, model, completionsUrl);
    public async Task<ChatCompletionsResponse> CompletionsAsync(IMessage content, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/messages")
    {
        MessageHistory.Add(content);
        return await CompletionsAsync(MessageHistory, tools, model, completionsUrl);
    }
    public async Task<ChatCompletionsResponse> CompletionsAsync(MessageCollection messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/messages")
    {
        using var _ = await BusyAsyncLock.LockAsync();
        BusyResetEvent.Reset();
        model ??= ModelName;
        try
        {
            while (true)
            {
                var request = AnthropicMessageAdapter.CreateRequest(model, messageHistory, SystemPrompt, tools ?? Tools);
                var responseState = await HttpClient.MessagesAsync(BuildUrl(completionsUrl), request);
                if (!responseState.State)
                    return new ChatCompletionsResponse { Choices = [new ChatCompletionsChoice { FinishReason = "error", Message = MessageContent.Create(Role.Assistant, responseState.Message) }], Model = model };

                var response = ConvertResponse(responseState.Data);
                var firstChoice = response.Choices.FirstOrDefault();
                if (firstChoice?.FinishReason == "tool_calls" && !firstChoice.Message.ToolCalls.IsNullOrEmpty())
                {
                    messageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice.Message.Content, [.. firstChoice.Message.ToolCalls]));
                    var parameters = ToolCallParameter.Create(firstChoice.Message.ToolCalls);
                    var toolCallResults = await ExecuteToolManager.ToolCalls(parameters);
                    foreach (var item in toolCallResults)
                        messageHistory.Add(MessageContent.Create(Role.Tool, item.Result.ToJsonString(), item.Parameter.ToolCallId));
                    continue;
                }

                if (firstChoice is not null)
                    messageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice.Message.Content));
                return response;
            }
        }
        finally
        {
            BusyResetEvent.Set();
        }
    }

    string BuildUrl(string completionsUrl)
    {
        var baseUrl = ModelProvider.ApiUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(completionsUrl)) completionsUrl = "/v1/messages";
        return baseUrl + "/" + completionsUrl.TrimStart('/');
    }

    static ChatCompletionsResponse ConvertResponse(AnthropicResponse response)
    {
        var result = BuildResultFromBlocks(response.Content, response.StopReason, response.Usage);
        return new ChatCompletionsResponse
        {
            Id = response.Id,
            Model = response.Model,
            Object = response.Type,
            Choices =
            [
                new ChatCompletionsChoice
                {
                    Index = 0,
                    FinishReason = result.FinishReason,
                    Message = MessageContent.Create(Role.Assistant, result.Content, result.ToolCalls),
                }
            ],
        };
    }

    static Result BuildResultFromAnthropicEvents(IEnumerable<AnthropicStreamEvent> events)
    {
        Dictionary<int, AnthropicContentBlock> blocks = [];
        Dictionary<int, string> toolInputFragments = [];
        string stopReason = null;
        Usage usage = null;

        foreach (var item in events)
        {
            var index = item.Index ?? 0;
            if (item.Type == "content_block_start")
            {
                if (item.ContentBlock is not null) blocks[index] = item.ContentBlock;
            }
            else if (item.Type == "content_block_delta" && blocks.TryGetValue(index, out var current))
            {
                if (item.Delta?.Type == "text_delta") current.Text += item.Delta.Text;
                else if (item.Delta?.Type == "input_json_delta") toolInputFragments[index] = (toolInputFragments.GetValueOrDefault(index) ?? string.Empty) + item.Delta.PartialJson;
            }
            else if (item.Type == "message_delta")
            {
                stopReason = item.Delta?.StopReason;
                usage = item.Usage;
            }
            else if (item.Type == "message_start")
            {
                usage = item.Message?.Usage;
            }
        }

        foreach (var item in toolInputFragments)
        {
            if (!blocks.TryGetValue(item.Key, out var block)) continue;
            try { block.Input = JsonConvert.DeserializeObject<JObject>(item.Value) ?? block.Input ?? []; }
            catch { block.Input ??= []; }
        }

        return BuildResultFromBlocks(blocks.OrderBy(x => x.Key).Select(x => x.Value), stopReason, usage);
    }
    static Result BuildResultFromBlocks(IEnumerable<AnthropicContentBlock> blocks, string stopReason, Usage usage)
    {
        var result = new Result
        {
            Role = Role.Assistant,
            FinishReason = stopReason == "tool_use" ? "tool_calls" : "stop",
            Usage = usage,
        };
        int index = 0;
        foreach (var block in blocks ?? [])
        {
            if (block.Type == "text") result.Content += block.Text;
            else if (block.Type == "tool_use")
            {
                result.ToolCalls.Add(new ToolCall
                {
                    Index = index++,
                    Id = block.Id,
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = block.Name,
                        Arguments = block.Input?.ToString(Formatting.None) ?? "{}",
                    },
                });
            }
        }
        if (!result.ToolCalls.IsNullOrEmpty()) result.FinishReason = "tool_calls";
        return result;
    }

    static ChatCompletionsChunk ConvertStreamEvent(AnthropicStreamEvent item, string model)
    {
        if (item.Type == "content_block_start" && item.ContentBlock?.Type == "tool_use")
        {
            return CreateChunk(model, new ChatCompletionsDelta
            {
                ToolCalls =
                [
                    new ToolCall
                    {
                        Index = item.Index ?? 0,
                        Id = item.ContentBlock.Id,
                        Type = "function",
                        Function = new ToolCallFunction { Name = item.ContentBlock.Name, Arguments = string.Empty },
                    }
                ],
            });
        }
        if (item.Type == "content_block_delta")
        {
            if (item.Delta?.Type == "text_delta")
                return CreateChunk(model, new ChatCompletionsDelta { Content = item.Delta.Text });
            if (item.Delta?.Type == "input_json_delta")
                return CreateChunk(model, new ChatCompletionsDelta
                {
                    ToolCalls =
                    [
                        new ToolCall
                        {
                            Index = item.Index ?? 0,
                            Function = new ToolCallFunction { Arguments = item.Delta.PartialJson },
                        }
                    ],
                });
        }
        if (item.Type == "message_delta")
            return CreateChunk(model, new ChatCompletionsDelta(), item.Delta?.StopReason == "tool_use" ? "tool_calls" : "stop", item.Usage);
        return null;
    }
    static ChatCompletionsChunk CreateChunk(string model, ChatCompletionsDelta delta, string finishReason = null, Usage usage = null) => new()
    {
        Model = model,
        Object = "chat.completion.chunk",
        Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds() > int.MaxValue ? int.MaxValue : (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
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


