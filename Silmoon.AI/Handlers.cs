using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI
{
    public delegate Task ToolCallsStartHandler(ToolCallParameter[] toolCallParameters);

    public delegate Task<ToolCallResult> ToolCallInvokeHandler(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult);

    public delegate Task<ToolCallResult[]> ToolCallsFinishHandler(ToolCallParameter[] toolCallParameters, ToolCallResult[] toolCallResults);

    public delegate Task ToolExecutingHandler(string functionName, ToolCallParameter toolCallParameter);

    public delegate Task ToolExecutedHandler(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult);


    public delegate Task StreamOutputHandler(StateSet<bool, Chunk> chunkState);

    public delegate Task StreamOutputCompletedHandler(Result result);
}
