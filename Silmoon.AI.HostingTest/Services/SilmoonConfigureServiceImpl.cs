using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Extensions.Hosting.Services;

namespace Silmoon.AI.HostingTest.Services
{
    public class SilmoonConfigureServiceImpl : SilmoonConfigureService
    {
        public string ApiUrl { get; set; }
        public string Key { get; set; }
        public string ProviderName { get; set; }
        public string ModelName { get; set; }
        public NativeApiKind ApiKind { get; set; }
        public string AnthropicVersion { get; set; }
        public ModelProvider Provider { get; private set; }
        public Dictionary<string, string> SystemPrompts { get; set; } = [];
        ILogger<ISilmoonConfigureService> Logger { get; set; }

        public SilmoonConfigureServiceImpl(IOptions<SilmoonConfigureServiceOption> options, ILogger<ISilmoonConfigureService> logger) : base(options)
        {
            Logger = logger;
            Logger.LogInformation($"当前配置文件{CurrentConfigFile}");

            ApiUrl = ConfigJson["apiUrl"]?.Value<string>();
            Key = ConfigJson["apiKey"]?.Value<string>();
            ProviderName = ConfigJson["providerName"]?.Value<string>() ?? "openai";
            ModelName = ConfigJson["modelName"]?.Value<string>();
            ApiKind = Enum.TryParse(ConfigJson["apiKind"]?.Value<string>(), true, out NativeApiKind apiKind) ? apiKind : NativeApiKind.OpenAIChatCompletions;
            AnthropicVersion = ConfigJson["anthropicVersion"]?.Value<string>() ?? "2023-06-01";

            Provider = new ModelProvider
            {
                ProviderName = ProviderName,
                ApiUrl = ApiUrl,
                ApiKey = Key,
                ApiKind = ApiKind,
                AnthropicVersion = AnthropicVersion,
                Models = [new Model { Name = ModelName, Enable = true }]
            };

            string defaultPrompt = """
            你是一个AI助手，旨在帮助用户解决问题和提供信息。请确保你的回答准确、简洁，并且易于理解。你可以回答各种问题，包括但不限于技术支持、常识性问题、编程帮助等。请保持专业和友好的语气。
            """;
            SystemPrompts[string.Empty] = defaultPrompt;
            Logger.LogInformation($"Provider: {ProviderName}, ApiKind: {ApiKind}, Model: {ModelName}, ApiUrl: {ApiUrl}");
        }
    }
}


