using System;
using Newtonsoft.Json;
using Silmoon.AI.Enums;

namespace Silmoon.AI.OpenAI;

public class Delta
{
    [JsonProperty("role")]
    public Role? Role { get; set; }
    [JsonProperty("content")]
    public string Content { get; set; }
    [JsonProperty("reasoning")]
    public string Reasoning { get; set; }
    [JsonProperty("reasoning_content")]
    public string ReasoningContent { get; set; }
    public string GetThinking() => ReasoningContent ?? Reasoning ?? null;
}
