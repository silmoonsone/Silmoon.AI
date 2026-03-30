using System;
using Newtonsoft.Json;

namespace Silmoon.AI.Client.OpenAI.Models;

public class Response
{
    [JsonProperty("choices")]
    public Choice[] Choices { get; set; }
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("object")]
    public string Object { get; set; }
    [JsonProperty("created")]
    public int Created { get; set; }

}
