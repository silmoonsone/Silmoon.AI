using System;
using Newtonsoft.Json;

namespace Silmoon.AI.Models.OpenAI.Models;

public class Request
{
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("messages")]
    public MessageContent[] Messages { get; set; }
    [JsonProperty("stream")]
    public bool? Stream { get; set; }
    [JsonProperty("temperature")]
    public double? Temperature { get; set; }
    [JsonProperty("top_p")]
    public double? TopP { get; set; }
    [JsonProperty("top_k")]
    public int? TopK { get; set; }
    [JsonProperty("enable_thinking")]
    public bool? EnableThinking { get; set; }
    [JsonProperty("thinking")]
    public object? Thinking { get; set; }
    [JsonProperty("reasoning_effort")]
    public string? ReasoningEffort { get; set; }
    [JsonProperty("enable_search")]
    public bool EnableSearch { get; set; }
    [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
    public List<Tool> Tools { get; set; }

    public bool ShouldSerializeTools() => Tools != null && Tools.Count > 0;

    public void SetEnableThinking(bool enableThinking, string apiUrl, string provider, string modelName)
    {
        apiUrl = apiUrl?.ToLower();
        modelName = modelName?.ToLower();
        provider = provider?.ToLower();
        if (enableThinking)
        {
            EnableThinking = true;
            ReasoningEffort = "high";
        }
        else
        {
            EnableThinking = false;
            ReasoningEffort = "none";
        }

        if (apiUrl.Contains("deepseek"))
        {
            if (enableThinking)
            {
                Thinking = new { type = "enabled" };
            }
            else
            {
                Thinking = new { type = "disabled" };
                ReasoningEffort = null;
            }
        }
        else Thinking = false;
    }

    public Request(string model, MessageContent[] messages, bool stream = true)
    {
        Model = model;
        Messages = messages;
        Stream = stream;
    }
}
