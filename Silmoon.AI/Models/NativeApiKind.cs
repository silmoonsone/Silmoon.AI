using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Silmoon.AI.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum NativeApiKind
{
    Chat = 0,
    Responses = 1,
    Anthropic = 2,
}

