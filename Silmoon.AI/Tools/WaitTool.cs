using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;

namespace Silmoon.AI.Tools;

/// <summary>
/// 供模型显式等待一段时间（节流轮询、部署/重启后就绪检测等），不执行 shell。
/// </summary>
public class WaitTool : ExecuteTool
{
    public const string WaitFunctionName = "Wait_Delay";

    /// <summary>允许的最短等待，避免误传 0 导致 tight loop。</summary>
    public const int MinDurationMs = 100;
    /// <summary>单次等待上限（5 分钟），防止误填过大值长时间阻塞。</summary>
    public const int MaxDurationMs = 300_000;


    public override Tool[] GetTools()
    {
        return [
            Tool.Create(WaitFunctionName, """
            Wait for the specified milliseconds, then return.
            This tool has no side effects; it only delays response completion for this call.
            Important: multiple tool calls in the same assistant interaction are executed in parallel by default; three `3000 ms` waits run together and finish in about `3` seconds wall-clock (not `9` seconds).
            Limits: duration is clamped to **100 ms–300 s**.
            Return JSON object with `State`, `Message`, `Data` (`Data.waitedMilliseconds`, optional `Data.reason`).
            """,
            [
                new ToolParameterProperty("integer", "durationMilliseconds", $"Ms to wait (clamped {MinDurationMs}–{MaxDurationMs}). **Same turn:** tell the user this value + seconds before/around the call.", null, true),
                new ToolParameterProperty("string", "reason", "Optional; can mirror why you wait (still state duration in user text).", null, false),
            ]),
        ];
    }

    public override async Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
    {
        ToolCallResult result = null;
        var functionName = toolCallParameter.FunctionName;
        var parameters = toolCallParameter.Parameters;

        if (functionName == WaitFunctionName)
        {
            await NotifyToolExecuting(functionName, toolCallParameter);
            var token = parameters["durationMilliseconds"];
            if (token is null || token.Type == JTokenType.Null)
            {
                result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, "durationMilliseconds is required."));
            }
            else
            {
                try
                {
                    int ms = token.Type == JTokenType.Integer ? token.Value<int>() : (int)Math.Round(token.Value<double>());
                    if (ms < MinDurationMs) ms = MinDurationMs;
                    if (ms > MaxDurationMs) ms = MaxDurationMs;

                    await Task.Delay(ms).ConfigureAwait(false);

                    string? reason = parameters["reason"]?.Type == JTokenType.String ? parameters["reason"]?.Value<string>() : parameters["reason"]?.ToString();
                    var payload = JsonConvert.SerializeObject(new
                    {
                        waitedMilliseconds = ms,
                        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                    });
                    result = ToolCallResult.Create(toolCallParameter, true.ToStateSet<object>(payload));
                }
                catch
                {
                    result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, "durationMilliseconds must be a number."));
                }
            }
            await NotifyToolExecuted(functionName, toolCallParameter, result);
        }
        return result;
    }
}
