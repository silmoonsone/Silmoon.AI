using Silmoon.AI.Handlers;
using Silmoon.AI.Models.OpenAI.Models;
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
}
