using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;

namespace Silmoon.AI.OpenAI.Models;

public class ResponsesResponse
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("object")]
    public string Object { get; set; }
    [JsonProperty("created_at")]
    public long CreatedAt { get; set; }
    [JsonProperty("status")]
    public string Status { get; set; }
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("output")]
    public List<ResponsesOutputItem> Output { get; set; } = [];
    [JsonProperty("usage")]
    public ResponsesUsage Usage { get; set; }
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; }
}

public class ResponsesOutputItem
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("status")]
    public string Status { get; set; }
    [JsonProperty("role")]
    public string Role { get; set; }
    [JsonProperty("content")]
    public List<ResponsesContentPart> Content { get; set; } = [];
    [JsonProperty("call_id")]
    public string CallId { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("arguments")]
    public string Arguments { get; set; }
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; }
}

public class ResponsesContentPart
{
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("text")]
    public string Text { get; set; }
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; }
}

public class ResponsesUsage
{
    [JsonProperty("input_tokens")]
    public int InputTokens { get; set; }
    [JsonProperty("output_tokens")]
    public int OutputTokens { get; set; }
    [JsonProperty("total_tokens")]
    public int TotalTokens { get; set; }
    [JsonProperty("input_tokens_details")]
    public ResponsesInputTokensDetails InputTokensDetails { get; set; }
    [JsonProperty("output_tokens_details")]
    public ResponsesOutputTokensDetails OutputTokensDetails { get; set; }

    public Usage ToUsage() => new()
    {
        PromptTokens = InputTokens,
        CompletionTokens = OutputTokens,
        TotalTokens = TotalTokens,
        PromptTokensDetails = InputTokensDetails is null ? null : new PromptTokensDetails { CachedTokens = InputTokensDetails.CachedTokens },
        CompletionTokensDetails = OutputTokensDetails is null ? null : new CompletionTokensDetails { ReasoningTokens = OutputTokensDetails.ReasoningTokens },
    };
}

public class ResponsesInputTokensDetails
{
    [JsonProperty("cached_tokens")]
    public int? CachedTokens { get; set; }
}

public class ResponsesOutputTokensDetails
{
    [JsonProperty("reasoning_tokens")]
    public int? ReasoningTokens { get; set; }
}
