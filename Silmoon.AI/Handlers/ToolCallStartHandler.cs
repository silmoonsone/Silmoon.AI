using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Handlers
{
    public delegate Task<List<ToolCallResult>> ToolCallStartHandler(ToolCallParameter[] toolCallParameters, Dictionary<string, ToolCallResult> toolCallResults);
}
