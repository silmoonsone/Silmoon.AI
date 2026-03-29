using System;
using Newtonsoft.Json;
using Silmoon.AI.Enums;

namespace Silmoon.AI.OpenAI;

public class Message<T>
{
    [JsonProperty("role")]
    public Role Role { get; set; }
    [JsonProperty("content")]
    public T Content { get; set; }
    [JsonProperty("tool_calls")]
    public List<ToolCall> ToolCalls { get; set; }
}
