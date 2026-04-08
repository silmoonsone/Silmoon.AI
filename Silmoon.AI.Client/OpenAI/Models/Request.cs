using System;
using Newtonsoft.Json;

namespace Silmoon.AI.Client.OpenAI.Models;

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
    [JsonProperty("reasoning_effort")]
    public string? ReasoningEffort { get; set; }
    [JsonProperty("enable_search")]
    public bool EnableSearch { get; set; }
    [JsonProperty("tools")]
    public Tool[] Tools { get; set; }

    public void SetEnableThinking(bool enableThinking)
    {
        if (enableThinking)
        {
            EnableThinking = true;
            ReasoningEffort = "high";
            ReasoningEffort = null;
        }
        else
        {
            EnableThinking = false;
            ReasoningEffort = "none";
        }
    }

    public Request(string model, MessageContent[] messages, bool stream = true)
    {
        Model = model;
        Messages = messages;
        Stream = stream;
    }
}
