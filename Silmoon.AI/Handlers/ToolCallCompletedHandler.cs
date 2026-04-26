using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Handlers
{
    public delegate Task<Dictionary<string, ToolCallResult>> ToolCallCompletedHandler(Dictionary<string, ToolCallResult> toolCallResults);
}
