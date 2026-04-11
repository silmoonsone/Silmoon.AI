using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Configures;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Extensions.Hosting.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Silmoon.AI.Terminal.Services
{
    public class SilmoonConfigureServiceImpl : SilmoonConfigureService
    {
        public string ApiUrl { get; set; }
        public string Key { get; set; }
        public string ModelName { get; set; }

        public Dictionary<string, ModelConfig> Models { get; set; } = [];
        public string SystemPrompt { get; set; }
        ILogger<ISilmoonConfigureService> Logger { get; set; }

        public SilmoonConfigureServiceImpl(IOptions<SilmoonConfigureServiceOption> options, ILogger<ISilmoonConfigureService> logger) : base(options)
        {
            Logger = logger;
            Logger.LogInformation($"当前配置文件{CurrentConfigFilePath}");

            SystemPrompt = ConfigJson.GetValue("systemPrompt")?.Value<string>();
            var modelObj = ConfigJson["models"];
            foreach (JProperty item in modelObj)
            {
                Models.Add(item.Name, item.Value.ToObject<ModelConfig>());
            }



            var defaultModelName = ConfigJson["defaultModel"]?.Value<string>();

            ApiUrl = Models[defaultModelName].ApiUrl;
            Key = Models[defaultModelName].ApiKey;
            ModelName = Models[defaultModelName].ModelName;
        }
    }
}
