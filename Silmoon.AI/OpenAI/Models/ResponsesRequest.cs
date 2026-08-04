using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Extensions;

namespace Silmoon.AI.OpenAI.Models;

public class ResponsesRequest : RequestBase
{
    [JsonProperty("input")]
    public JArray Input { get; set; } = [];
    [JsonProperty("instructions")]
    public string Instructions { get; set; }
    [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
    public JArray Tools { get; set; }
    [JsonProperty("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; set; } = true;

    public bool ShouldSerializeTools() => Tools is not null && Tools.Count > 0;

    public ResponsesRequest(string model, JArray input, string instructions = null, bool stream = false)
    {
        Model = model;
        Input = input;
        Instructions = instructions;
        Stream = stream;
    }

    public void SetEnableThinking(bool enableThinking, string apiUrl, string provider, string modelName)
    {
        if (enableThinking)
            ExtraBody["reasoning"] = JObject.FromObject(new { effort = "medium" }, JsonSerializer.Create(NativeApiJson.SerializerSettings));
    }

}
