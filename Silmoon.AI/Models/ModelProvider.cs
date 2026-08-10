using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Models
{
    public class ModelProvider
    {
        [JsonProperty("providerName")]
        public string ProviderName { get; set; }
        [JsonProperty("providerDescription")]
        public string ProviderDescription { get; set; }
        [JsonProperty("apiUrl")]
        public string ApiUrl { get; set; }
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }
        [JsonProperty("apiKind")]
        public NativeApiKind ApiKind { get; set; } = NativeApiKind.Chat;
        [JsonProperty("anthropicVersion")]
        public string AnthropicVersion { get; set; } = "2023-06-01";
        [JsonProperty("enable")]
        public bool Enable { get; set; } = true;
        [JsonProperty("models")]
        public List<Model> Models { get; set; } = [];

        public static ModelProvider Create(string apiUrl, string apiKey, string providerName, string modelName)
        {
            return new ModelProvider
            {
                ApiUrl = apiUrl,
                ApiKey = apiKey,
                ProviderName = providerName,
                Models = [Model.Create(modelName)]
            };
        }
    }

    public class Model
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("enable")]
        public bool Enable { get; set; } = true;
        public static Model Create(string name, bool enable = true) => new Model
        {
            Name = name,
            Enable = enable
        };
    }
}

