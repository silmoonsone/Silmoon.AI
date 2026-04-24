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
        public string ApiUrl { get; set; }
        public string Key { get; set; }
        public ModelProviders DefaultProvider { get; set; }
        public string DefaultModelName { get; set; }

        public Dictionary<string, ModelProviders> Models { get; set; } = [];
        public string SystemPrompt { get; set; }
        ILogger<ISilmoonConfigureService> Logger { get; set; }

        public SilmoonConfigureServiceImpl(IOptions<SilmoonConfigureServiceOption> options, ILogger<ISilmoonConfigureService> logger) : base(options)
        {
            Logger = logger;
            Logger.LogInformation($"当前配置文件{CurrentConfigFilePath}");

            SystemPrompt = ConfigJson.GetValue("systemPrompt")?.Value<string>();
            var modelObj = ConfigJson["modelProviders"];
            foreach (JProperty item in modelObj)
            {
                Models.Add(item.Name, item.Value.ToObject<ModelProviders>());
            }

            DefaultProvider = Models[ConfigJson["defaultModel"]["defaultProvider"].Value<string>()];
            DefaultModelName = ConfigJson["defaultModel"]["defaultModelName"].Value<string>();
            ApiUrl = DefaultProvider.ApiUrl;
            Key = DefaultProvider.ApiKey;

            logger.LogInformation($"Model: {DefaultModelName}, ApiUrl: {ApiUrl}");
        }
    }
}
