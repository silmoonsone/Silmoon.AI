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
        Tool[] GetTools();
        void InjectToolCall(INativeChatClient nativeChatClient);
        Task<StateSet<bool, string>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, string> toolMessageState);
    }
}
