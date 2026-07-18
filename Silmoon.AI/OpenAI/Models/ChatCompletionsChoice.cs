using System;
using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI.Models;

public class ChatCompletionsChoice
{
    [JsonProperty("message")]
    public NativeMessageContent Message { get; set; }
    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }
    [JsonProperty("index")]
    public int Index { get; set; }
}

[Obsolete("Use ChatCompletionsChoice. This alias is kept for source compatibility.")]
public class Choice : ChatCompletionsChoice
{
}

