using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.Extensions;

namespace Silmoon.AI.OpenAI.Models;

public class ChatCompletionsRequest
{
    [JsonProperty("model")]
    public string Model { get; set; }
    [JsonProperty("messages")]
    public IMessage[] Messages { get; set; }
    [JsonProperty("stream")]
    public bool? Stream { get; set; }
    [JsonProperty("temperature")]
    public double? Temperature { get; set; }
    [JsonProperty("top_p")]
    public double? TopP { get; set; }
    [JsonProperty("top_k")]
    public int? TopK { get; set; }
    [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
    public List<Tool> Tools { get; set; }
    [JsonIgnore]
    public JObject ExtraBody { get; set; } = new JObject();
    public bool ShouldSerializeTools() => Tools != null && Tools.Count > 0;
    public ChatCompletionsRequest(string model, IMessage[] messages, bool stream = true)
    {
        Model = model;
        Messages = messages;
        Stream = stream;
    }

    public void SetEnableThinking(bool enableThinking, string apiUrl, string provider, string modelName)
    {
        apiUrl = apiUrl?.ToLower();
        modelName = modelName?.ToLower();
        provider = provider?.ToLower();
        if (apiUrl.Contains("aliyun"))
        {
            ExtraBody["enable_thinking"] = enableThinking;
        }

        if (apiUrl.Contains("deepseek"))
        {
            if (enableThinking)
            {
                ExtraBody["thinking"] = JObject.FromObject(new { type = "enabled" });
                //ExtraBody["reasoning_effort"] = null;
            }
            else ExtraBody["thinking"] = JObject.FromObject(new { type = "disabled" });
        }

        //if (provider == "lmstudio")
        //{
        //    if (enableThinking)
        //    {
        //        ExtraBody["thinking"] = JObject.FromObject(new { type = "enabled" });
        //        //ExtraBody["reasoning_effort"] = null;
        //    }
        //    else ExtraBody["thinking"] = JObject.FromObject(new { type = "disabled" });
        //}
    }
    public string ToJsonRequestString(JsonSerializerSettings settings = null)
    {
        settings ??= new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
        };
        var jsonObject = JObject.FromObject(this, JsonSerializer.Create(settings));
        if (ExtraBody != null && ExtraBody.Count > 0)
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

[Obsolete("Use ChatCompletionsRequest. This alias is kept for source compatibility.")]
public class Request : ChatCompletionsRequest
{
    public Request(string model, IMessage[] messages, bool stream = true) : base(model, messages, stream)
    {
    }
}

