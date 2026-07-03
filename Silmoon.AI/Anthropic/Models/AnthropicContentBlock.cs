using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Silmoon.AI.Anthropic.Models;

public class AnthropicContentBlock
{
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("text")]
    public string Text { get; set; }
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("input")]
    public JObject Input { get; set; }
    [JsonProperty("tool_use_id")]
    public string ToolUseId { get; set; }
    [JsonProperty("content")]
    public string Content { get; set; }

    public static AnthropicContentBlock TextBlock(string text) => new()
    {
        Type = "text",
        Text = text ?? string.Empty,
    };

    public static AnthropicContentBlock ToolUse(string id, string name, JObject input) => new()
    {
        Type = "tool_use",
        Id = id,
        Name = name,
        Input = input ?? [],
    };

    public static AnthropicContentBlock ToolResult(string toolUseId, string content) => new()
    {
        Type = "tool_result",
        ToolUseId = toolUseId,
        Content = content ?? string.Empty,
    };
}

