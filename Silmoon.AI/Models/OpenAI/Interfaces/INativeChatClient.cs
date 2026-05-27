using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;

namespace Silmoon.AI.Models.OpenAI.Interfaces;

public interface INativeChatClient : IDisposable
{
    event ToolCallsStartHandler OnToolCallsStart;
    event ToolCallInvokeHandler OnToolCallInvoke;
    event ToolExecutingHandler OnToolExecuting;
    event ToolExecutedHandler OnToolExecuted;
    event ToolCallsFinishHandler OnToolCallsFinish;
    event StreamOutputHandler OnStreamOutput;
    event StreamOutputCompletedHandler OnStreamOutputCompleted;

    ModelProvider ModelProvider { get; set; }
    string ModelName { get; set; }
    string SystemPrompt { get; set; }
    List<Tool> Tools { get; set; }
    List<IMessage> MessageHistory { get; set; }
    ExecuteToolManager ExecuteToolManager { get; set; }

    /// <summary>
    /// 重置消息历史：无续接正文时仅保留当前 <see cref="SystemPrompt"/> 对应的一条 System（若无 System 则整表清空）；有正文时再追加一条 User。
    /// </summary>
    /// <param name="continuation">续接记忆正文；为空或未提供时不追加 User，效果为「清掉多轮，只留 System」。</param>
    void ClearHistory(string? continuation = null);
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(IMessage content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(List<IMessage> messageHistory, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");

    Task<Response> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    Task<Response> CompletionsAsync(IMessage content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    Task<Response> CompletionsAsync(List<IMessage> messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
}
