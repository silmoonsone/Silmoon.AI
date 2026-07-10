using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Silmoon.AI.OpenAI.Models;

public class ResponsesStreamEvent
{
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("response")]
    public ResponsesResponse Response { get; set; }
    [JsonProperty("item")]
    public ResponsesOutputItem Item { get; set; }
    [JsonProperty("output_index")]
    public int? OutputIndex { get; set; }
    [JsonProperty("item_id")]
    public string ItemId { get; set; }
    [JsonProperty("content_index")]
    public int? ContentIndex { get; set; }
    [JsonProperty("delta")]
    public string Delta { get; set; }
    [JsonProperty("text")]
    public string Text { get; set; }
    [JsonProperty("arguments")]
    public string Arguments { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; }
}
