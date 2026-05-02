using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Handlers
{
    public delegate Task<ToolCallResult> ToolCallCompletedHandler(ToolCallResult toolCallResult);
}
