using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Handlers
{
    public delegate Task<StateSet<bool, MessageContent>> ToolCallInvokeHandler(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState);
}
