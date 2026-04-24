using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Models
{
    public class ModelProviders
    {
        [JsonProperty("providerName")]
        public string ProviderName { get; set; }
        [JsonProperty("apiUrl")]
        public string ApiUrl { get; set; }
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }
        [JsonProperty("enable")]
        public bool Enable { get; set; } = true;

        [JsonProperty("models")]
        public List<Model> Models { get; set; } = [];
    }

    public class Model
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("enable")]
        public bool Enable { get; set; } = true;
    }

}
