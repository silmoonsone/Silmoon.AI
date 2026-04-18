using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.AI.Prompts;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.Tools;

/// <summary>
/// 模型在生成续接记忆包正文后调用：清空消息历史、以重置前当前的 <see cref="INativeChatClient.SystemPrompt"/> 快照重建 System，并以正文（可加前缀）作为新的首条 User。
/// </summary>
public class ContinuationMemoryTool : ExecuteTool
{
    public INativeChatClient NativeChatClient { get; set; }

    public ContinuationMemoryTool(INativeChatClient nativeChatClient)
    {
        NativeChatClient = nativeChatClient;
        Tools = GetTools();
    }

    public static Tool[] GetTools()
    {
        return
        [
            Tool.Create(UtilPrompt.ContinuationMemoryResetToolFunctionName, UtilPrompt.ContinuationMemoryResetToolDescription,
            [
                new ToolParameterProperty("string", "continuation_memory",
                    "根据 Continuation Memory 提示词生成的续接记忆包正文（完整粘贴）。调用后此前对话全部丢弃，仅保留基准系统提示 + 本条作为新的用户首条消息。",
                    null, true),
            ]),
        ];
    }

    public override Task<StateSet<bool, MessageContent>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState)
    {
        if (functionName != UtilPrompt.ContinuationMemoryResetToolFunctionName) return Task.FromResult<StateSet<bool, MessageContent>>(null);

        var raw = parameters["continuation_memory"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(raw)) return Task.FromResult(false.ToStateSet<MessageContent>(null, "continuation_memory 不能为空：请先按 Continuation Memory 提示词生成完整续接记忆包正文，再作为本参数传入。"));

        var body = raw.Trim();
        var userText = UtilPrompt.ContinuationMemoryUserMessagePrefix.IsNullOrEmpty() ? body : UtilPrompt.ContinuationMemoryUserMessagePrefix + body;
        NativeChatClient.ResetHistory(userText);
        var payload = new JObject
        {
            ["ok"] = true,
            ["message"] = "已重置对话：系统提示已恢复为基准，续接记忆（含首段续接约束）已作为新的首条用户消息。请仅推进记忆中「进行中/待办」，勿重复执行「已完成」中的命令或工具。",
        }.ToString(Formatting.None);
        return Task.FromResult(true.ToStateSet(MessageContent.Create(Role.Tool, payload, toolCallId)));
    }
}
