using System;
using Newtonsoft.Json;

namespace Silmoon.AI.Models.OpenAI.Models;

public class Choice
{
    [JsonProperty("message")]
    public MessageContent Message { get; set; }
    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }
    [JsonProperty("index")]
    public int Index { get; set; }
}
