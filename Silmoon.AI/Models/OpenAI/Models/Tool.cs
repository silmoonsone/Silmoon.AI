using System.Collections.Generic;
using Newtonsoft.Json;
using Silmoon.Extensions;

namespace Silmoon.AI.Models.OpenAI.Models;

public class Tool
{
    [JsonProperty("type")]
    public string Type { get; set; } = "function";
    [JsonProperty("function")]
    public ToolFunction Function { get; set; }
    public Tool(string type = "function")
    {
        Type = type;
    }
    public static Tool Create(string functionName, string functionDescription, ToolParameterProperty[] toolParameterProperties)
    {
        var result = new Tool("function")
        {
            Function = new ToolFunction(functionName, functionDescription)
            {
                Parameters = new ToolParameters()
                {
                    Type = "object",
                    Properties = [],
                    Required = [],
                    AdditionalProperties = false
                }
            }
        };

        foreach (var item in toolParameterProperties)
        {
            result.Function.Parameters.Properties.Add(item.Name, item);
            if (result.Function.Parameters.Required != null && item.IsRequired) result.Function.Parameters.Required.Add(item.Name);
        }

        return result;
    }
}

public class ToolFunction
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    [JsonProperty("description")]
    public string? Description { get; set; }
    [JsonProperty("parameters")]
    public ToolParameters Parameters { get; set; } = new ToolParameters();
    public ToolFunction(string name, string description)
    {
        Name = name;
        Description = description;
    }
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


    [JsonIgnore]
    public string Name { get; set; }
    [JsonIgnore]
    public bool IsRequired { get; set; }

    private static string NormalizeJsonSchemaType(string type)
    {
        if (type.IsNullOrEmpty()) return "string";
        return type.ToLowerInvariant() switch
        {
            "bool" => "boolean",
            _ => type
        };
    }

    public ToolParameterProperty(string type, string name, string description, List<object> @enum = null, bool isRequired = false)
    {
        Type = NormalizeJsonSchemaType(type);
        Name = name;
        Description = description;
        Enum = @enum;
        IsRequired = isRequired;
    }
}