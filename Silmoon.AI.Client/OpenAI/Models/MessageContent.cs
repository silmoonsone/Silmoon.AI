using System;
using Silmoon.AI.Client.OpenAI.Enums;

namespace Silmoon.AI.Client.OpenAI.Models;

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
