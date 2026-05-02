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
    /// <summary>允许的最短等待，避免误传 0 导致 tight loop。</summary>
    public const int MinDurationMs = 100;
    /// <summary>单次等待上限（5 分钟），防止误填过大值长时间阻塞。</summary>
    public const int MaxDurationMs = 300_000;


    public override Tool[] GetTools()
    {
        return [
            Tool.Create("WaitTool", """
            Pause for a fixed wall-clock duration, then return.
            Use for retry spacing / throttling; not for shell output waiting (use `StatefulCommand*`).
            In the same assistant turn, always state exact wait (`durationMilliseconds` and seconds); avoid vague “wait a bit”.
            Parallel waits are possible but usually not useful and can be ambiguous; prefer one call with total duration (e.g., `6000` once, not two parallel `3000`).
            Default: one wait call per decision step; multiple waits only for truly independent branches.
            Limits: clamped to **100 ms–300 s**.
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

        if (functionName == "WaitTool")
        {
            var token = parameters["durationMilliseconds"];
            if (token is null || token.Type == JTokenType.Null)
            {
                result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<string>(null, "durationMilliseconds is required."));
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
                    result = ToolCallResult.Create(toolCallParameter, true.ToStateSet<string>(payload));
                }
                catch
                {
                    result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<string>(null, "durationMilliseconds must be a number."));
                }
            }
        }
        return result;
    }
}
