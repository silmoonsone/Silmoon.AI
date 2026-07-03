using System;
using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI.Models;

public class ChatCompletionsResponse
{
    [JsonProperty("choices")]
    public ChatCompletionsChoice[] Choices { get; set; }
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("object")]
    public string Object { get; set; }
    [JsonProperty("created")]
    public int Created { get; set; }

}

[Obsolete("Use ChatCompletionsResponse. This alias is kept for source compatibility.")]
public class Response : ChatCompletionsResponse
{
}

