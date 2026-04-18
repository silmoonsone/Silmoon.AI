using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Tools
{
    public abstract class ExecuteTool : IExecuteTool
    {
        public Tool[] Tools { get; internal set; } = [];

        public virtual void InjectToolCall(INativeChatClient nativeChatClient)
        {
            nativeChatClient.Tools.AddRange(Tools);
            nativeChatClient.OnToolCallInvoke += OnToolCallInvoke;
        }

        public abstract Task<StateSet<bool, MessageContent>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState);
    }
}
