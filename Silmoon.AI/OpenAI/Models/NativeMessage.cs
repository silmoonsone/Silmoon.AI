using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.Extensions;

namespace Silmoon.AI.OpenAI.Models;

public interface INativeMessage
{
    [JsonProperty("hash")]
    string Hash { get; set; }
    [JsonProperty("role")]
    Role Role { get; set; }
    [JsonProperty("tool_calls")]
    List<ToolCall> ToolCalls { get; set; }
    [JsonProperty("tool_call_id")]
    string ToolCallId { get; set; }
    string GetContent();
}
public interface INativeMessage<TContent> : INativeMessage
{
    [JsonProperty("content")]
    TContent Content { get; set; }
}
public abstract class NativeMessage : INativeMessage
{
    [JsonProperty("hash")]
    public string Hash { get; set; } = Guid.NewGuid().ToString("N");
    [JsonProperty("role")]
    public Role Role { get; set; }
    [JsonProperty("tool_calls")]
    public List<ToolCall> ToolCalls { get; set; }
    [JsonProperty("tool_call_id")]
    public string ToolCallId { get; set; }
    public abstract string GetContent();
    public override string ToString()
    {
        return $"Role: {Role}, ToolCallId: {ToolCallId}, ToolCalls: {(ToolCalls != null ? string.Join(", ", ToolCalls) : "null")}";
    }
}
public abstract class NativeMessage<TContent> : NativeMessage, INativeMessage<TContent>
{
    [JsonProperty("content")]
    public TContent Content { get; set; }
    public override string ToString()
    {
        return $"Role: {Role}, Content: {Content}, ToolCallId: {ToolCallId}, ToolCalls: {(ToolCalls != null ? string.Join(", ", ToolCalls) : "null")}";
    }
}

public class NativeMessageContent : NativeMessage<string>
{
    [JsonProperty("reasoning_content")]
    public string ReasoningContent { get; set; }

    public static NativeMessageContent Create(Role role, string content, string toolCallId, string reasoningContent = null)
    {
        return new NativeMessageContent
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
            ReasoningContent = reasoningContent,
        };
    }
    public static NativeMessageContent Create(Role role, string content, List<ToolCall> toolCalls = null, string reasoningContent = null)
    {
        return new NativeMessageContent
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls,
            ReasoningContent = reasoningContent,
        };
    }
    public override string GetContent() => Content;
}
public class NativeMessageJson : NativeMessage<JObject>
{
    public static NativeMessageJson Create(Role role, JObject content, string toolCallId)
    {
        return new NativeMessageJson
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
        };
    }
    public static NativeMessageJson Create(Role role, JObject content, List<ToolCall> toolCalls = null)
    {
        return new NativeMessageJson
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls
        };
    }
    public override string GetContent() => Content.ToJsonString();
}
public class NativeMessages<TContent> : NativeMessage<TContent[]>
{
    public static NativeMessages<TContent> Create(Role role, TContent[] content, string toolCallId)
    {
        return new NativeMessages<TContent>
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
        };
    }
    public static NativeMessages<TContent> Create(Role role, TContent[] content, List<ToolCall> toolCalls = null)
    {
        return new NativeMessages<TContent>
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls
        };
    }
    public override string GetContent() => Content != null ? string.Join("\r\n", Content.Select(x => x.ToString())) : string.Empty;
}

public class NativeMessageImageUrl : NativeMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "image_url";
    [JsonProperty("image_url")]
    public string ImageUrl { get; set; }
    public override string GetContent() => ImageUrl;
}
public class NativeMessageText : NativeMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "text";
    [JsonProperty("text")]
    public string Text { get; set; }
    public override string GetContent() => Text;
}

