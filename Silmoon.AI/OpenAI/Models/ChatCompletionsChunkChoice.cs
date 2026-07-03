using System;
using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI.Models;

public class ChatCompletionsChunkChoice
{
    [JsonProperty("index")]
    public int Index { get; set; }
    [JsonProperty("delta")]
    public ChatCompletionsDelta Delta { get; set; }
    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }
    [JsonProperty("tool_calls")]
    public List<ToolCall> ToolCalls { get; set; }
}

[Obsolete("Use ChatCompletionsChunkChoice. This alias is kept for source compatibility.")]
public class ChunkChoice : ChatCompletionsChunkChoice
{
}

