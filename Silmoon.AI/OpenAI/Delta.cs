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
}
