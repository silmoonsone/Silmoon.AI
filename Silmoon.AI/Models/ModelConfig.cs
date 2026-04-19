using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Models
{
    public class ModelConfig
    {
        [JsonProperty("apiUrl")]
        public string ApiUrl { get; set; }
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }
        [JsonProperty("modelName")]
        public string ModelName { get; set; }
        [JsonProperty("enable")]
        public bool Enable { get; set; } = true;
    }
}
