using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Silmoon.AI.Anthropic.Models;

public class AnthropicTool
{
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("description")]
    public string Description { get; set; }
    [JsonProperty("input_schema")]
    public JObject InputSchema { get; set; }
}

