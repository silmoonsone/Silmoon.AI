using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Anthropic.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.OpenAI.Models;
using Silmoon.AI.OpenAI;
using Silmoon.Extensions;

namespace Silmoon.AI.Anthropic;

public static class AnthropicMessageAdapter
{
    public static AnthropicRequest CreateRequest(string model, IEnumerable<IMessage> history, string systemPrompt, IEnumerable<Tool> tools)
    {
        List<AnthropicMessage> messages = [];
        foreach (var item in history ?? [])
        {
            if (item.Role == Role.System) continue;
            var converted = ConvertMessage(item);
            if (converted is not null) messages.Add(converted);
        }

        return new AnthropicRequest
        {
            Model = model,
            System = systemPrompt,
            Messages = [.. messages],
            Tools = ConvertTools(tools),
        };
    }

    static AnthropicMessage ConvertMessage(IMessage message)
    {
        if (message.Role == Role.Tool)
        {
            return new AnthropicMessage
            {
                Role = "user",
                Content = [AnthropicContentBlock.ToolResult(message.ToolCallId, message.GetContent())],
            };
        }

        var role = message.Role == Role.Assistant ? "assistant" : "user";
        List<AnthropicContentBlock> blocks = [];
        var text = message.GetContent();
        if (!text.IsNullOrEmpty()) blocks.Add(AnthropicContentBlock.TextBlock(text));

        if (message.Role == Role.Assistant && !message.ToolCalls.IsNullOrEmpty())
        {
            foreach (var toolCall in message.ToolCalls)
            {
                var args = toolCall.Function?.Arguments;
                JObject input = [];
                if (!args.IsNullOrEmpty())
                {
                    try { input = JsonConvert.DeserializeObject<JObject>(args) ?? []; }
                    catch { input = []; }
                }
                blocks.Add(AnthropicContentBlock.ToolUse(toolCall.Id, toolCall.Function?.Name, input));
            }
        }

        return new AnthropicMessage
        {
            Role = role,
            Content = blocks.Count == 0 ? [AnthropicContentBlock.TextBlock(string.Empty)] : blocks,
        };
    }

    static List<AnthropicTool> ConvertTools(IEnumerable<Tool> tools)
    {
        List<AnthropicTool> results = [];
        foreach (var tool in tools ?? [])
        {
            if (tool?.Function is null) continue;
            results.Add(new AnthropicTool
            {
                Name = tool.Function.Name,
                Description = tool.Function.Description,
                InputSchema = JObject.Parse(JsonConvert.SerializeObject(tool.Function.Parameters, NativeApiJson.SerializerSettings)),
            });
        }
        return results;
    }
}



