using Silmoon.AI.Models;
using System;
using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI.Models;

public class ChatCompletionsChunk
{
    [JsonProperty("choices")]
    public ChatCompletionsChunkChoice[] Choices { get; set; }
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("object")]
    public string Object { get; set; }
    [JsonProperty("created")]
    public int Created { get; set; }
    [JsonProperty("usage")]
    public Usage Usage { get; set; }
}

[Obsolete("Use ChatCompletionsChunk. This alias is kept for source compatibility.")]
public class Chunk : ChatCompletionsChunk
{
}


