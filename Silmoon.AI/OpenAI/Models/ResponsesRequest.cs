using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.Extensions;

namespace Silmoon.AI.OpenAI.Models;

public class ResponsesRequest
{
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("input")]
    public JArray Input { get; set; } = [];
    [JsonProperty("instructions")]
    public string Instructions { get; set; }
    [JsonProperty("stream")]
    public bool? Stream { get; set; }
    [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
    public JArray Tools { get; set; }
    [JsonProperty("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; set; } = true;
    [JsonIgnore]
    public JObject ExtraBody { get; set; } = new();

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

    public string ToJsonRequestString(JsonSerializerSettings settings = null)
    {
        settings ??= NativeApiJson.SerializerSettings;
        var jsonObject = JObject.FromObject(this, JsonSerializer.Create(settings));
        if (ExtraBody is not null && ExtraBody.Count > 0)
        {
            jsonObject.Merge(ExtraBody, new JsonMergeSettings
            {
                MergeNullValueHandling = settings.NullValueHandling == NullValueHandling.Ignore ? MergeNullValueHandling.Ignore : MergeNullValueHandling.Merge,
                MergeArrayHandling = MergeArrayHandling.Union
            });
        }
        return jsonObject.ToJsonString(settings);
    }
}
