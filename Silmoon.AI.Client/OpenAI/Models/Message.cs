using Newtonsoft.Json;
using Silmoon.AI.Client.OpenAI.Enums;

namespace Silmoon.AI.Client.OpenAI.Models;

public interface IContent
{

}
public class Message<TContent>
{
    [JsonProperty("role")]
    public Role Role { get; set; }
    [JsonProperty("content")]
    public TContent Content { get; set; }
    [JsonProperty("tool_calls")]
    public List<ToolCall> ToolCalls { get; set; }
    [JsonProperty("tool_call_id")]
    public string ToolCallId { get; set; }
    public override string ToString()
    {
        return $"Role: {Role}, Content: {Content}, ToolCallId: {ToolCallId}, ToolCalls: {(ToolCalls != null ? string.Join(", ", ToolCalls) : "null")}";
    }
}

public class MessageContent : Message<string>
{
    public static MessageContent Create(Role role, string content, string toolCallId)
    {
        return new MessageContent
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
        };
    }
    public static MessageContent Create(Role role, string content, List<ToolCall> toolCalls = null)
    {
        return new MessageContent
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls
        };
    }
}
public class MessageContents : Message<IContent[]>
{
    public static MessageContents Create(Role role, IContent[] content, string toolCallId)
    {
        return new MessageContents
        {
            Role = role,
            Content = content,
            ToolCallId = toolCallId,
        };
    }
    public static MessageContents Create(Role role, IContent[] content, List<ToolCall> toolCalls = null)
    {
        return new MessageContents
        {
            Role = role,
            Content = content,
            ToolCalls = toolCalls
        };
    }
}

public class MessageImageUrl : IContent
{
    [JsonProperty("type")]
    public string Type { get; set; } = "image_url";
    [JsonProperty("image_url")]
    public string ImageUrl { get; set; }
}
public class MessageText : IContent
{
    [JsonProperty("type")]
    public string Type { get; set; } = "text";
    [JsonProperty("text")]
    public string Text { get; set; }
}
