using Silmoon.AI.Handlers;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;

namespace Silmoon.AI.Models.OpenAI.Interfaces;

public interface INativeChatClient
{
    event ToolCallInvokeHandler OnToolCallInvoke;
    string ApiUrl { get; set; }
    string ApiKey { get; set; }
    string Model { get; set; }
    string SystemPrompt { get; set; }
    List<Tool> Tools { get; set; }
    List<MessageContent> MessageHistory { get; set; }
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(MessageContent content, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(List<MessageContent> messageHistory, List<Chunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");

    Task<Response> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    Task<Response> CompletionsAsync(MessageContent content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
    Task<Response> CompletionsAsync(List<MessageContent> messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
}
