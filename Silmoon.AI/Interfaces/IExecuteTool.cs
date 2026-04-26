using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
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
        Task<List<ToolCallResult>> OnToolCallInvoke(ToolCallParameter[] toolCallParameters, Dictionary<string, ToolCallResult> toolCallResults);
    }
}
