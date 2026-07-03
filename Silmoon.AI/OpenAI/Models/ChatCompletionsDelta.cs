using System;
using Newtonsoft.Json;
using Silmoon.AI.OpenAI.Models.Enums;

namespace Silmoon.AI.OpenAI.Models;

public class ChatCompletionsDelta
{
    [JsonProperty("role")]
    public Role? Role { get; set; }
    [JsonProperty("content")]
    public string Content { get; set; }
    [JsonProperty("reasoning")]
    public string Reasoning { get; set; }
    [JsonProperty("reasoning_content")]
    public string ReasoningContent { get; set; }
    [JsonProperty("tool_calls")]
    public List<ToolCall> ToolCalls { get; set; }
    public string GetThinking() => ReasoningContent ?? Reasoning ?? null;
}

[Obsolete("Use ChatCompletionsDelta. This alias is kept for source compatibility.")]
public class Delta : ChatCompletionsDelta
{
}

