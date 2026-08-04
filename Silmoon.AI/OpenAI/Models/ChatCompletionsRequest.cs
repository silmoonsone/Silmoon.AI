using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Extensions;

namespace Silmoon.AI.OpenAI.Models;

public class ChatCompletionsRequest : RequestBase
{
    [JsonProperty("messages")]
    public INativeMessage[] Messages { get; set; }
    [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
    public List<Tool> Tools { get; set; }
    public bool ShouldSerializeTools() => Tools != null && Tools.Count > 0;
    public ChatCompletionsRequest(string model, INativeMessage[] messages, bool stream = true)
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
}

[Obsolete("Use ChatCompletionsRequest. This alias is kept for source compatibility.")]
public class Request : ChatCompletionsRequest
{
    public Request(string model, INativeMessage[] messages, bool stream = true) : base(model, messages, stream)
    {
    }
}

