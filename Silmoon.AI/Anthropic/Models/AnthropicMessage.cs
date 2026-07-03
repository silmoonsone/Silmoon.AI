using Newtonsoft.Json;

namespace Silmoon.AI.Anthropic.Models;

public class AnthropicMessage
{
    [JsonProperty("role")]
    public string Role { get; set; }
    [JsonProperty("content")]
    public List<AnthropicContentBlock> Content { get; set; } = [];
}

