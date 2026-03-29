using System.Collections.Generic;
using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI;

public class Tool
{
    [JsonProperty("type")]
    public string Type { get; set; } = "function";
    [JsonProperty("function")]
    public ToolFunction Function { get; set; } = new ToolFunction();
}

public class ToolFunction
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    [JsonProperty("description")]
    public string? Description { get; set; }
    [JsonProperty("parameters")]
    public ToolParameters Parameters { get; set; } = new ToolParameters();
}

public class ToolParameters
{
    [JsonProperty("type")]
    public string Type { get; set; } = "object";
    [JsonProperty("properties")]
    public Dictionary<string, ToolParameterProperty> Properties { get; set; } = [];
    [JsonProperty("required")]
    public List<string> Required { get; set; } = [];
    [JsonProperty("additionalProperties")]
    public bool AdditionalProperties { get; set; } = false;
}

public class ToolParameterProperty
{
    [JsonProperty("type")]
    public string Type { get; set; } = "string";
    [JsonProperty("description")]
    public string? Description { get; set; }
    [JsonProperty("enum")]
    public List<object>? Enum { get; set; }
    [JsonProperty("items")]
    public ToolParameters? Items { get; set; }
}