using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Tools
{
    public abstract class ToolSet : IToolSet
    {
        public Tool[] Tools { get; set; } = [];
        INativeClient NativeClient { get; set; }
        bool isInjected = false;
        protected ToolSet()
        {
            Tools = GetTools();
        }
        public abstract Tool[] GetTools();
        public virtual StateSet<bool> InjectToolCall(INativeClient nativeClient)
        {
            if (isInjected) return false.ToStateSet("Tool call already injected.");
            isInjected = true;
            NativeClient = nativeClient;
            NativeClient.Tools.AddRange(Tools);
            NativeClient.ToolSetManager.OnToolCallInvoke += OnToolCallInvoke;
            return true.ToStateSet("Tool call injected successfully.");
        }

        public async Task NotifyToolExecuting(string functionName, ToolCallParameter toolCallParameter) => await NativeClient.ToolSetManager.onToolCallExecuting(functionName, toolCallParameter);
        public async Task NotifyToolExecuted(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => await NativeClient.ToolSetManager.onToolCallExecuted(functionName, toolCallParameter, toolCallResult);

        public abstract Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult);
    }
}

