using Silmoon.AI.Models;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Models;

namespace Silmoon.AI.Responses;

#pragma warning disable CS0067
public class NativeResponsesClient : INativeChatClient
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
    public string SystemPrompt { get; set; }
    public bool EnableThinking { get; set; } = false;
    public List<Tool> Tools { get; set; } = [];
    public List<IMessage> MessageHistory { get; set; } = [];
    public ExecuteToolManager ExecuteToolManager { get; set; }

    public NativeResponsesClient(ModelProvider modelProvider, string modelName, string systemPrompt = null)
    {
        ModelProvider = modelProvider;
        ModelName = modelName;
        SystemPrompt = systemPrompt;
        ExecuteToolManager = new ExecuteToolManager(this);
    }

    public void ClearHistory(string? continuation = null)
    {
        MessageHistory.Clear();
        if (!string.IsNullOrEmpty(continuation))
            MessageHistory.Add(MessageContent.Create(OpenAI.Models.Enums.Role.User, continuation));
    }

    public void RollbackHistory(uint rounds = 1)
    {
        while (rounds > 0 && MessageHistory.Count > 0)
        {
            if (MessageHistory.LastOrDefault().Role == OpenAI.Models.Enums.Role.System) break;
            MessageHistory.RemoveAt(MessageHistory.Count - 1);
            rounds--;
        }
    }

    public void RebuildHttpClient()
    {
    }
    public IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(string content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses") =>
        throw new NotImplementedException("OpenAI Responses API support is reserved here; use NativeChatCompletionsClient or NativeAnthropicClient until the Responses protocol is implemented.");

    public IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(IMessage content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses") =>
        throw new NotImplementedException("OpenAI Responses API support is reserved here; use NativeChatCompletionsClient or NativeAnthropicClient until the Responses protocol is implemented.");

    public IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(List<IMessage> messageHistory, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses") =>
        throw new NotImplementedException("OpenAI Responses API support is reserved here; use NativeChatCompletionsClient or NativeAnthropicClient until the Responses protocol is implemented.");

    public Task<ChatCompletionsResponse> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses") =>
        throw new NotImplementedException("OpenAI Responses API support is reserved here; use NativeChatCompletionsClient or NativeAnthropicClient until the Responses protocol is implemented.");

    public Task<ChatCompletionsResponse> CompletionsAsync(IMessage content, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses") =>
        throw new NotImplementedException("OpenAI Responses API support is reserved here; use NativeChatCompletionsClient or NativeAnthropicClient until the Responses protocol is implemented.");

    public Task<ChatCompletionsResponse> CompletionsAsync(List<IMessage> messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/v1/responses") =>
        throw new NotImplementedException("OpenAI Responses API support is reserved here; use NativeChatCompletionsClient or NativeAnthropicClient until the Responses protocol is implemented.");

    public void Dispose()
    {
        Tools.Clear();
        MessageHistory.Clear();
    }
}
#pragma warning restore CS0067




