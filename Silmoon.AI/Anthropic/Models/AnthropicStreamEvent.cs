using Silmoon.AI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Silmoon.AI.Anthropic.Models;

public class AnthropicStreamEvent
{
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("message")]
    public AnthropicResponse Message { get; set; }
    [JsonProperty("index")]
    public int? Index { get; set; }
    [JsonProperty("content_block")]
    public AnthropicContentBlock ContentBlock { get; set; }
    [JsonProperty("delta")]
    public AnthropicDelta Delta { get; set; }
    [JsonProperty("usage")]
    public Usage Usage { get; set; }
}

public class AnthropicDelta
{
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("text")]
    public string Text { get; set; }
    [JsonProperty("partial_json")]
    public string PartialJson { get; set; }
    [JsonProperty("stop_reason")]
    public string StopReason { get; set; }
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; }
}


