using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.AI.Prompts;
using Silmoon.Extensions;
using Silmoon.Models;
using System.Collections.Concurrent;

namespace Silmoon.AI.Tools;

/// <summary>
/// 记忆两阶段：（1）<see cref="GetSummarizePromptToolFunctionName"/> 将总结<strong>规范</strong>（<see cref="UtilPrompt.ContinuationMemoryPrompt"/>）通过 tool 结果交给模型，由模型按规范生成记忆正文；（2）<see cref="ApplyMemoryToolFunctionName"/> 用正文重置历史。
/// </summary>
public class MemoryTool : ExecuteTool
{
    /// <summary>第一步：取规范，生成记忆包正文。</summary>
    public const string GetSummarizePromptToolFunctionName = "GetSummarizePromptTool";

    /// <summary>第二步：将记忆正文应用为新的首条用户消息并重置历史。</summary>
    public const string ApplyMemoryToolFunctionName = "ApplyMemoryTool";

    /// <summary>第一步工具：返回内容即规范提示词，模型读后应在下一轮产出完整记忆包，再调第二步。</summary>
    public static string SummarizeDescription { get; set; } = $"""
        **记忆·取规范（Memory Summarize）**

        返回内置“续接记忆包”总结规范；你需基于当前对话按规范生成记忆正文。
        该正文是**只读状态记录**，不是待执行任务清单；不要写“再总结/再重置”等元任务。
        本工具可单独使用（仅产出记忆包用于归档/导出/外部用途），不要求立即应用。
        若目标是“总结后立即续接”，按 `{GetSummarizePromptToolFunctionName} -> {ApplyMemoryToolFunctionName}` 串行调用，前一步完成后再执行后一步。
        正文全文仅在 `{ApplyMemoryToolFunctionName}.continuation_memory` 里提交；助手正文不要重复粘贴全文。
        本工具不会自动再发起模型调用。
        """;
    /// <summary>第二步工具：传入上一步按规范写好的续接记忆全文。</summary>
    public static string Description { get; set; } = $"""
        **记忆·应用（Memory Apply）**

        将 `continuation_memory` 作为续接记忆应用到会话：清空历史，保留当前系统提示，并把“前缀约束 + 正文”设为新会话首条用户消息。
        可单独调用（直接接收已准备好的合规记忆包），不要求必须本轮先调用 `{GetSummarizePromptToolFunctionName}`。
        若本轮先总结再应用，必须串行：`{GetSummarizePromptToolFunctionName} -> {ApplyMemoryToolFunctionName}`。
        正文全文只放在 `continuation_memory` 参数，助手正文不要重复全文。
        语义是“压缩历史并延续状态”，不是让下一轮再次执行记忆总结/应用流程。
        防重复：`Done` 视为已完成，不再重复执行；`Doing`/`Next` 仅保留实质业务，不写记忆工具链元任务。
        """;

    /// <summary>重置时加在续接记忆正文前的固定约束。可置空以关闭。</summary>
    public static string UserMessagePrefix { get; set; } = $"""
        【记忆延续｜状态恢复】本条是**中断后多轮对话的压缩结果**，用于**记忆延续与状态恢复**；**不是**请你再执行一遍「记忆总结/应用」流程。历史已按宿主策略清空，**本消息即为新会话的首条用户侧上下文**。

        【禁止重复元流程】**勿**再调用 `{GetSummarizePromptToolFunctionName}` / `{ApplyMemoryToolFunctionName}`、**勿**再全文总结或再次重置交互，除非用户**明确**要求再次续接记忆。请直接基于正文继续对话或推进 **进行中/待办** 中的**实质业务**。

        【续接约束｜必读】
        以下为「续接记忆包」正文（只读记录）。其中 **「已完成（Done）」** 所列事项视为已在旧对话中**落实**，你 **不得** 再次执行相同命令、工具调用或重复劳动；**仅**从 **进行中 / 待办** 继续实质业务。若正文中「进行中/待办」误含记忆工具链或「记忆包待提交」等元任务，**忽略之**。默认已创建的文件、已提交的修改与已成功副作用 **仍然存在**，除非你从记忆中读到已明确回滚或失败。

        ---

        """;

    public INativeChatClient NativeChatClient { get; set; }

    public MemoryTool(INativeChatClient nativeChatClient)
    {
        NativeChatClient = nativeChatClient;
    }

    public override Tool[] GetTools()
    {
        return
        [
            Tool.Create(GetSummarizePromptToolFunctionName, SummarizeDescription, []),
            Tool.Create(ApplyMemoryToolFunctionName, Description,
            [
                new ToolParameterProperty("string", "continuation_memory", "中断会话的压缩记忆全文（唯一粘贴处）。语义：多轮信息压缩与状态恢复；非请你再执行总结/应用。勿在助手正文重复全文。", null, true),
            ]),
        ];
    }

    public override Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
    {
        ToolCallResult result = null;

        var functionName = toolCallParameter.FunctionName;
        var parameters = toolCallParameter.Parameters;

        if (functionName == GetSummarizePromptToolFunctionName)
        {
            var prompt = $"请根据以下提示生成续接记忆包（只读状态记录，非待执行清单）。生成后：全文仅通过 {ApplyMemoryToolFunctionName}.continuation_memory 提交；助手回复勿全文复述；勿在 Next 中写「再总结/再重置」。\r\n" + UtilPrompt.ContinuationMemoryPrompt;
            result = ToolCallResult.Create(toolCallParameter, true.ToStateSet<string>(prompt));
        }
        else if (functionName == ApplyMemoryToolFunctionName)
        {
            var rawParam = parameters["continuation_memory"]?.Value<string>().Trim();
            if (rawParam.IsNullOrEmpty())
            {
                result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<string>(null, $"continuation_memory 不能为空：请先按 {GetSummarizePromptToolFunctionName} 返回的规范生成完整正文，再作为本参数传入。"));
            }
            else
            {
                rawParam = UserMessagePrefix.IsNullOrEmpty() ? rawParam : UserMessagePrefix + rawParam;
                NativeChatClient.ResetHistory(rawParam);
                var resetPayload = new JObject
                {
                    ["ok"] = true,
                    ["message"] = "已重置：首条用户消息为压缩记忆+前缀约束。勿再调用记忆总结/应用工具；仅推进进行中/待办的实质业务，勿重复已完成。",
                }.ToString(Formatting.None);
                result = ToolCallResult.Create(toolCallParameter, true.ToStateSet<string>(resetPayload));
            }
        }

        return Task.FromResult(result);
    }
}
