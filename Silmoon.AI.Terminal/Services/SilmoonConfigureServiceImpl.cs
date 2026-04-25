using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
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
        public ModelProvider DefaultProvider { get; set; }
        public string DefaultModelName { get; set; }

        public Dictionary<string, ModelProvider> ModelProviders { get; set; } = [];
        public string SystemPrompt { get; set; }
        ILogger<ISilmoonConfigureService> Logger { get; set; }

        public SilmoonConfigureServiceImpl(IOptions<SilmoonConfigureServiceOption> options, ILogger<ISilmoonConfigureService> logger) : base(options)
        {
            Logger = logger;
            Logger.LogInformation($"当前配置文件{CurrentConfigFilePath}");

            SystemPrompt = ConfigJson.GetValue("systemPrompt")?.Value<string>();
            var modelObj = ConfigJson["modelProviders"];
            foreach (JObject item in modelObj)
            {
                var modelProvider = item.ToObject<ModelProvider>();
                ModelProviders.Add(modelProvider.ProviderName, modelProvider);
            }

            DefaultProvider = ModelProviders[ConfigJson["defaultModel"]["defaultProvider"].Value<string>()];
            DefaultModelName = ConfigJson["defaultModel"]["defaultModelName"].Value<string>();

            logger.LogInformation($"Model: {DefaultModelName}, ApiUrl: {DefaultProvider.ApiUrl}");
        }
    }
}
