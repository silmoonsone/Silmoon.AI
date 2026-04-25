using Silmoon.AI.Handlers;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;

namespace Silmoon.AI.Models.OpenAI.Interfaces;

public interface INativeChatClient
{
    event ToolCallStartHandler OnToolCallStart;
    event ToolCallCompletedHandler OnToolCallCompleted;
    ModelProvider ModelProvider { get; set; }
    string ModelName { get; set; }
    string SystemPrompt { get; set; }
    List<Tool> Tools { get; set; }
    List<MessageContent> MessageHistory { get; set; }

    /// <summary>
    /// 重置消息历史：无续接正文时仅保留当前 <see cref="SystemPrompt"/> 对应的一条 System（若无 System 则整表清空）；有正文时再追加一条 User。
    /// </summary>
    /// <param name="continuation">续接记忆正文；为空或未提供时不追加 User，效果为「清掉多轮，只留 System」。</param>
    void ResetHistory(string? continuation = null);
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(MessageContent content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(List<MessageContent> messageHistory, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");

    Task<Response> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    Task<Response> CompletionsAsync(MessageContent content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    Task<Response> CompletionsAsync(List<MessageContent> messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
}
