using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Interfaces
{
    public interface IExecuteTool
    {
        Tool[] Tools { get; }
        void InjectToolCall(INativeChatClient nativeChatClient);
        Task<StateSet<bool, MessageContent>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState);
    }
}
