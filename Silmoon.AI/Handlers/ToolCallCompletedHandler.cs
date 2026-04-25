using Newtonsoft.Json.Linq;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Handlers
{
    public delegate Task<StateSet<bool, string>> ToolCallCompletedHandler(StateSet<bool, string> toolCallResult);
}
