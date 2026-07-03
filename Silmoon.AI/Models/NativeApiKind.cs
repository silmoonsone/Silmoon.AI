using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Silmoon.AI.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum NativeApiKind
{
    OpenAIChatCompletions,
    AnthropicMessages,
    OpenAIResponses,
}

