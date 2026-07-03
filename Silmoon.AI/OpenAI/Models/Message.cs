using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.Extensions;

namespace Silmoon.AI.OpenAI.Models;

public interface IMessage
{
    [JsonProperty("role")]
    Role Role { get; set; }
    [JsonProperty("tool_calls")]
    List<ToolCall> ToolCalls { get; set; }
    [JsonProperty("tool_call_id")]
    string ToolCallId { get; set; }
    string GetContent();
}
public interface IMessage<TContent> : IMessage
{
    [JsonProperty("content")]
    TContent Content { get; set; }
}
public abstract class Message : IMessage
{
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
public abstract class Message<TContent> : Message, IMessage<TContent>
{
    [JsonProperty("content")]
    public TContent Content { get; set; }
    public override string ToString()
    {
        return $"Role: {Role}, Content: {Content}, ToolCallId: {ToolCallId}, ToolCalls: {(ToolCalls != null ? string.Join(", ", ToolCalls) : "null")}";
    }
}

public class MessageContent : Message<string>
{
    [JsonProperty("reasoning_content")]
    public string ReasoningContent { get; set; }

    public static MessageContent Create(Role role, string content, string toolCallId, string reasoningContent = null)
    {
        return new MessageContent
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
            ReasoningContent = reasoningContent,
        };
    }
    public static MessageContent Create(Role role, string content, List<ToolCall> toolCalls = null, string reasoningContent = null)
    {
        return new MessageContent
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls,
            ReasoningContent = reasoningContent,
        };
    }
    public override string GetContent() => Content;
}
public class MessageJson : Message<JObject>
{
    public static MessageJson Create(Role role, JObject content, string toolCallId)
    {
        return new MessageJson
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
        };
    }
    public static MessageJson Create(Role role, JObject content, List<ToolCall> toolCalls = null)
    {
        return new MessageJson
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls
        };
    }
    public override string GetContent() => Content.ToJsonString();
}
public class Messages<TContent> : Message<TContent[]>
{
    public static Messages<TContent> Create(Role role, TContent[] content, string toolCallId)
    {
        return new Messages<TContent>
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
        };
    }
    public static Messages<TContent> Create(Role role, TContent[] content, List<ToolCall> toolCalls = null)
    {
        return new Messages<TContent>
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls
        };
    }
    public override string GetContent() => Content != null ? string.Join("\r\n", Content.Select(x => x.ToString())) : string.Empty;
}

public class MessageImageUrl : Message
{
    [JsonProperty("type")]
    public string Type { get; set; } = "image_url";
    [JsonProperty("image_url")]
    public string ImageUrl { get; set; }
    public override string GetContent() => ImageUrl;
}
public class MessageText : Message
{
    [JsonProperty("type")]
    public string Type { get; set; } = "text";
    [JsonProperty("text")]
    public string Text { get; set; }
    public override string GetContent() => Text;
}

