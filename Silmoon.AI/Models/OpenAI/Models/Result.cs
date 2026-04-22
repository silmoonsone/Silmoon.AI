using System;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.Extensions;
using Newtonsoft.Json;

namespace Silmoon.AI.Models.OpenAI.Models;

public class Result
{
    public Role Role { get; set; }
    public string Content { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string ReasoningContent { get; set; }
    public string FinishReason { get; set; }
    public List<ToolCall> ToolCalls { get; set; } = [];
    public static Result Create(ChunkChoice[] chunkChoices, bool includeReasonContent = false)
    {
        Result result = new Result();
        Dictionary<int, ToolCall> toolCallsByIndex = [];
        if (chunkChoices is not null && chunkChoices.Length > 0)
        {
            foreach (ChunkChoice choice in chunkChoices)
            {
                if (choice.Delta is not null)
                {
                    if (choice.Delta.Content is not null) result.Content += choice.Delta.Content;

                    if (includeReasonContent)
                    {
                        if (choice.Delta.ReasoningContent is not null) result.ReasoningContent += choice.Delta.ReasoningContent;
                        else if (choice.Delta.Reasoning is not null) result.ReasoningContent += choice.Delta.Reasoning;
                    }

                    if (choice.Delta.Role is not null) result.Role = choice.Delta.Role.Value;
                    if (!choice.FinishReason.IsNullOrEmpty()) result.FinishReason = choice.FinishReason;
                    AccumulateToolCallFragments(toolCallsByIndex, choice);
                }
            }
        }
        AssignOrderedToolCalls(result, toolCallsByIndex);
        return result;
    }
    public static Result Create(Chunk[] responses, bool includeReasonContent = false)
    {
        List<ChunkChoice> chunkChoices = [];
        foreach (var response in responses)
        {
            if (!response.Choices.IsNullOrEmpty()) chunkChoices.AddRange(response.Choices);
        }

        return Create([.. chunkChoices], includeReasonContent);
    }


    /// <summary>
    /// 将流式片段按 OpenAI 的 <c>index</c> 合并：同一 index 下拼接 <c>arguments</c>，并合并首段出现的 id/type/name。
    /// 兼容 <c>delta.tool_calls</c>（标准）以及少数实现在 <c>choices[].tool_calls</c> 上的增量。
    /// </summary>
    static void AccumulateToolCallFragments(Dictionary<int, ToolCall> byIndex, ChunkChoice choice)
    {
        var partials = choice.Delta?.ToolCalls;
        if (partials.IsNullOrEmpty()) partials = choice.ToolCalls;
        if (partials.IsNullOrEmpty()) return;

        foreach (var partial in partials)
        {
            var idx = partial.Index ?? 0;
            if (!byIndex.TryGetValue(idx, out var merged))
            {
                merged = new ToolCall { Index = idx };
                byIndex[idx] = merged;
            }
            if (!string.IsNullOrEmpty(partial.Id)) merged.Id = partial.Id;
            if (!string.IsNullOrEmpty(partial.Type)) merged.Type = partial.Type;
            if (partial.Function is null) continue;
            merged.Function ??= new ToolCallFunction();
            if (!string.IsNullOrEmpty(partial.Function.Name)) merged.Function.Name = partial.Function.Name;
            if (partial.Function.Arguments is not null) merged.Function.Arguments += partial.Function.Arguments;
        }
    }
    static void AssignOrderedToolCalls(Result result, Dictionary<int, ToolCall> byIndex)
    {
        if (!byIndex.IsNullOrEmpty())
            result.ToolCalls = [.. byIndex.OrderBy(kv => kv.Key).Select(kv => kv.Value)];
    }
}
