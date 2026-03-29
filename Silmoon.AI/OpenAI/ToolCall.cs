using Newtonsoft.Json;

namespace Silmoon.AI.OpenAI;

public class ToolCall
{
    // 流式增量里会有 index，用于标识第几个 tool call。
    // 非流式完整结果里通常可以忽略，但保留更通用。
    [JsonProperty("index")]
    public int? Index { get; set; }

    // 流式增量里首段通常有 id，后续段可能为空或缺失。
    [JsonProperty("id")]
    public string? Id { get; set; }

    // 目前通用场景基本是 function。
    [JsonProperty("type")]
    public string? Type { get; set; }

    // function 本身在流式增量里也可能是分段返回，因此要允许为空。
    [JsonProperty("function")]
    public ToolCallFunction? Function { get; set; }
}

public class ToolCallFunction
{
    // 首段通常会给 name，后续段可能缺失。
    [JsonProperty("name")]
    public string? Name { get; set; }

    // arguments 在流式里是分段字符串，需要外部自行拼接。
    [JsonProperty("arguments")]
    public string? Arguments { get; set; }
}
