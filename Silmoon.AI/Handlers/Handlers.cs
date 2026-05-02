using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Handlers
{
    public delegate Task<ToolCallResult> ToolCallCompletedHandler(ToolCallResult toolCallResult);

    public delegate Task<ToolCallResult> ToolCallStartHandler(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult);

    public delegate Task ToolExecutingHandler(ToolCallParameter toolCallParameter);

    public delegate Task ToolExecutedHandler(ToolCallResult toolCallResult);
}
