using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.Extensions;

namespace Silmoon.AI.Models;

public class RequestBase
{
    public static double? DefaultTemperature { get; set; } = 0.6;
    public static double? DefaultTopP { get; set; } = 0.95;

    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("stream")]
    public bool? Stream { get; set; }
    [JsonProperty("temperature")]
    public double? Temperature { get; set; } = DefaultTemperature;
    [JsonProperty("top_p")]
    public double? TopP { get; set; } = DefaultTopP;
    [JsonIgnore]
    public JObject ExtraBody { get; set; } = new();

    public string ToJsonRequestString(JsonSerializerSettings settings = null)
    {
        settings ??= NativeApiJson.RequestSerializerSettings;
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
