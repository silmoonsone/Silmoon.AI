using System;
using Silmoon.AI.Enums;

namespace Silmoon.AI.OpenAI;

public class MessageContent : Message<string>
{
    public static MessageContent Create(Role role, string content)
    {
        return new MessageContent
        {
            Role = role,
            Content = content
        };
    }
}
