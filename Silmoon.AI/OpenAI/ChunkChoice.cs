using System;
using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI;

public class ChunkChoice
{
    [JsonProperty("index")]
    public int Index { get; set; }
    [JsonProperty("delta")]
    public Delta Delta { get; set; }
    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }
}
