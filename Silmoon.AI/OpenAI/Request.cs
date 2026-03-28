using System;
using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI;

public class Request
{
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("messages")]
    public MessageContent[] Messages { get; set; }
    [JsonProperty("stream")]
    public bool Stream { get; set; }

    public Request(string model, MessageContent[] messages, bool stream = true)
    {
        Model = model;
        Messages = messages;
        Stream = stream;
    }
}
