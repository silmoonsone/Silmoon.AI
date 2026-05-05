using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Tools
{
    public abstract class ExecuteTool : IExecuteTool
    {
        public Tool[] Tools { get; set; } = [];
        INativeChatClient NativeChatClient { get; set; }
        protected ExecuteTool()
        {
            Tools = GetTools();
        }
        public abstract Tool[] GetTools();
        public virtual void InjectToolCall(INativeChatClient nativeChatClient)
        {
            NativeChatClient = nativeChatClient;
            NativeChatClient.Tools.AddRange(Tools);
            NativeChatClient.ExecuteToolManager.OnToolCallInvoke += OnToolCallInvoke;
        }

        public async Task NotifyToolExecuting(string functionName, ToolCallParameter toolCallParameter) => await NativeChatClient.ExecuteToolManager.onToolCallExecuting(functionName, toolCallParameter);
        public async Task NotifyToolExecuted(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => await NativeChatClient.ExecuteToolManager.onToolCallExecuted(functionName, toolCallParameter, toolCallResult);

        public abstract Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult);
    }
}
