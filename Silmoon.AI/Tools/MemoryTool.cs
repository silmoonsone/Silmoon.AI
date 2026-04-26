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
    public static string SummarizeDescription { get; set; } = """
        **记忆·取规范（Memory Summarize）**

        调用后，宿主会返回**内置的记忆包总结规范**全文。请**仔细阅读**该规范，结合当前对话历史，**自行生成**符合格式的续接记忆包正文。

        **文档性质：** 你产出的是**记忆记录 / 状态快照**（只读、供下一轮续接用），**不是**待你在写完后再去执行的任务书；勿在正文里给自己布置「再总结、再重置」类元操作。

        **下一步：** 再调用 <c>MemoryApplyTool</c>，将**完整记忆正文仅**填入参数 <c>continuation_memory</c> 以重置历史。**不要**在助手可见回复里再把全文复述一遍后再调工具（等于同一内容输出两次）；助手正文可留空或**一句话**带过。

        本工具**不会**替你在后台再请求一次模型。
        """;
    /// <summary>第二步工具：传入上一步按规范写好的续接记忆全文。</summary>
    public static string Description { get; set; } = """
        **记忆·应用（Memory Apply）**

        将续接记忆包正文重置聊天历史：丢弃此前消息，系统提示保持为当前配置，将正文（加内置前缀）作为新的首条用户消息。

        **语义：** 本次提交 = **多轮对话被中断后的信息压缩 + 记忆延续 + 状态恢复**；**不是**要求下一轮模型再执行一遍「总结/重置」流程。

        **唯一载体：** 完整正文**只**放在本工具的 <c>continuation_memory</c> 参数中；**勿**在同一轮助手回复正文里再贴一遍全文。

        **前置：** 通常先调用 <c>MemorySummarizeTool</c> 取得规范，按规范写好正文后，将**全文**传入 <c>continuation_memory</c>。

        **防重复：** 记忆中「已完成（Done）」勿再执行；仅推进进行中/待办。

        **State 书写：** `Doing` / `Next` 只写实质业务；**勿**把「撰写/提交本记忆包、调用记忆工具」写进进行中或待办——正文交付时记忆包已写成，否则续接会误再跑一遍记忆流程。
        """;

    /// <summary>重置时加在续接记忆正文前的固定约束。可置空以关闭。</summary>
    public static string UserMessagePrefix { get; set; } = """
        【记忆延续｜状态恢复】本条是**中断后多轮对话的压缩结果**，用于**记忆延续与状态恢复**；**不是**请你再执行一遍「记忆总结/应用」流程。历史已按宿主策略清空，**本消息即为新会话的首条用户侧上下文**。

        【禁止重复元流程】**勿**再调用 `MemorySummarizeTool` / `MemoryApplyTool`、**勿**再全文总结或再次重置交互，除非用户**明确**要求再次续接记忆。请直接基于正文继续对话或推进 **进行中/待办** 中的**实质业务**。

        【续接约束｜必读】
        以下为「续接记忆包」正文（只读记录）。其中 **「已完成（Done）」** 所列事项视为已在旧对话中**落实**，你 **不得** 再次执行相同命令、工具调用或重复劳动；**仅**从 **进行中 / 待办** 继续实质业务。若正文中「进行中/待办」误含记忆工具链或「记忆包待提交」等元任务，**忽略之**。默认已创建的文件、已提交的修改与已成功副作用 **仍然存在**，除非你从记忆中读到已明确回滚或失败。

        ---

        """;

    public INativeChatClient NativeChatClient { get; set; }

    public Tool[] Tools => throw new NotImplementedException();

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

    public override Task<List<ToolCallResult>> OnToolCallInvoke(ToolCallParameter[] toolCallParameters, ConcurrentDictionary<string, ToolCallResult> toolCallResults)
    {
        List<ToolCallResult> results = [];

        foreach (var parameter in toolCallParameters)
        {
            var functionName = parameter.FunctionName;
            var parameters = parameter.Parameters;

            if (functionName == GetSummarizePromptToolFunctionName)
            {
                var prompt = "请根据以下提示生成续接记忆包（只读状态记录，非待执行清单）。生成后：全文仅通过 MemoryApplyTool.continuation_memory 提交；助手回复勿全文复述；勿在 Next 中写「再总结/再重置」。\r\n" + UtilPrompt.ContinuationMemoryPrompt;
                results.Add(ToolCallResult.Create(parameter, true.ToStateSet<string>(prompt)));
            }
            else if (functionName == ApplyMemoryToolFunctionName)
            {
                var rawParam = parameters["continuation_memory"]?.Value<string>().Trim();
                if (rawParam.IsNullOrEmpty())
                {
                    results.Add(ToolCallResult.Create(parameter, false.ToStateSet<string>(null, $"continuation_memory 不能为空：请先按 MemorySummarizeTool 返回的规范生成完整正文，再作为本参数传入。")));
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
                    results.Add(ToolCallResult.Create(parameter, true.ToStateSet<string>(resetPayload)));
                }
            }
        }

        return Task.FromResult(results);
    }

    public void InjectToolCall(INativeChatClient nativeChatClient)
    {
        throw new NotImplementedException();
    }
}
