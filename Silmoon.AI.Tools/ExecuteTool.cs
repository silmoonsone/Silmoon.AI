using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
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
        public Tool[] Tools { get; set; } = [];

        protected ExecuteTool()
        {
            Tools = GetTools();
        }
        public abstract Tool[] GetTools();
        public virtual void InjectToolCall(INativeChatClient nativeChatClient)
        {
            nativeChatClient.Tools.AddRange(Tools);
            nativeChatClient.OnToolCallStart += OnToolCallInvoke;
        }

        public abstract Task<List<ToolCallResult>> OnToolCallInvoke(ToolCallParameter[] toolCallParameters, Dictionary<string, ToolCallResult> toolCallResults);
    }
}
