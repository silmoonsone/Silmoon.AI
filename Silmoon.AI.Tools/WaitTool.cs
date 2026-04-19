using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;

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
                **Purpose:** Pause for a fixed **wall-clock** duration, then return. Use to **space retries**, slow loops, or pause before a follow-up check. **Not** for shell/output waits → **StatefulCommand***.

                **User-visible transparency (mandatory):** In the **same turn** as this call, your message to the human **must** state **how long** you will wait: give **`durationMilliseconds`** and **seconds** (e.g. `durationMilliseconds=5000` → 5s). **Forbidden:** only vague text (“等一下”“稍后再测”) **without** the numeric duration. Prefer **asking the user** to confirm the wait when it is not obvious; if they already chose a duration, **repeat** it when calling.

                **Limits:** Clamped server-side to **100 ms–300 s** (5 min).
                """,
                [
                    new ToolParameterProperty("integer", "durationMilliseconds", $"Ms to wait (clamped {MinDurationMs}–{MaxDurationMs}). **Same turn:** tell the user this value + seconds before/around the call.", null, true),
                    new ToolParameterProperty("string", "reason", "Optional; can mirror why you wait (still state duration in user text).", null, false),
                ]),
        ];
    }

    public override async Task<StateSet<bool, MessageContent>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState)
    {
        if (functionName != "WaitTool") return null;

        var token = parameters["durationMilliseconds"];
        if (token is null || token.Type == JTokenType.Null) return false.ToStateSet<MessageContent>(null, "durationMilliseconds is required.");

        int ms;
        try
        {
            ms = token.Type == JTokenType.Integer ? token.Value<int>() : (int)Math.Round(token.Value<double>());
        }
        catch
        {
            return false.ToStateSet<MessageContent>(null, "durationMilliseconds must be a number.");
        }

        if (ms < MinDurationMs) ms = MinDurationMs;
        if (ms > MaxDurationMs) ms = MaxDurationMs;

        await Task.Delay(ms).ConfigureAwait(false);

        string? reason = parameters["reason"]?.Type == JTokenType.String ? parameters["reason"]?.Value<string>() : parameters["reason"]?.ToString();
        var payload = JsonConvert.SerializeObject(new
        {
            waitedMilliseconds = ms,
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        });
        return true.ToStateSet(MessageContent.Create(Role.Tool, payload, toolCallId));
    }
}
