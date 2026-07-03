using Silmoon.AI.Models;
using Newtonsoft.Json;
using Silmoon.AI.OpenAI.Models;

namespace Silmoon.AI.Anthropic.Models;

public class AnthropicResponse
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("role")]
    public string Role { get; set; }
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("content")]
    public List<AnthropicContentBlock> Content { get; set; } = [];
    [JsonProperty("stop_reason")]
    public string StopReason { get; set; }
    [JsonProperty("usage")]
    public Usage Usage { get; set; }
}


