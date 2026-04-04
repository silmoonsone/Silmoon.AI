using System;
using Silmoon.AI.Client.OpenAI.Enums;

namespace Silmoon.AI.Client.OpenAI.Models;

public class MessageContent : Message<string>
{
    public static MessageContent Create(Role role, string content, string toolCallId)
    {
        return new MessageContent
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
        };
    }
    public static MessageContent Create(Role role, string content, List<ToolCall> toolCalls = null)
    {
        return new MessageContent
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls
        };
    }
}
