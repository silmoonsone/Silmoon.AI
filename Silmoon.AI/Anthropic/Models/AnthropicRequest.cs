using Newtonsoft.Json;
using Silmoon.AI.Models;

namespace Silmoon.AI.Anthropic.Models;

public class AnthropicRequest : RequestBase
{
    [JsonProperty("messages")]
    public AnthropicMessage[] Messages { get; set; } = [];
    [JsonProperty("system")]
    public string System { get; set; }
    [JsonProperty("max_tokens")]
    public int MaxTokens { get; set; } = 4096;
    [JsonProperty("tools")]
    public List<AnthropicTool> Tools { get; set; }

    public bool ShouldSerializeTools() => Tools is { Count: > 0 };
}

