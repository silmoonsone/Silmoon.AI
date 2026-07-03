using Newtonsoft.Json;

namespace Silmoon.AI.Anthropic.Models;

public class AnthropicRequest
{
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("messages")]
    public AnthropicMessage[] Messages { get; set; } = [];
    [JsonProperty("system")]
    public string System { get; set; }
    [JsonProperty("max_tokens")]
    public int MaxTokens { get; set; } = 4096;
    [JsonProperty("stream")]
    public bool Stream { get; set; }
    [JsonProperty("temperature")]
    public double? Temperature { get; set; }
    [JsonProperty("top_p")]
    public double? TopP { get; set; }
    [JsonProperty("tools")]
    public List<AnthropicTool> Tools { get; set; }

    public bool ShouldSerializeTools() => Tools is { Count: > 0 };
}

