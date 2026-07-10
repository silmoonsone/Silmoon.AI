using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Extensions.Hosting.Services;

namespace Silmoon.AI.AuthropicTest.Services;

public class SilmoonConfigureServiceImpl : SilmoonConfigureService
{
    public ModelProvider Provider { get; private set; }
    public string ModelName { get; private set; }
    public string SystemPrompt { get; private set; }

    public SilmoonConfigureServiceImpl(IOptions<SilmoonConfigureServiceOption> options, ILogger<ISilmoonConfigureService> logger) : base(options)
    {
        logger.LogInformation($"当前配置文件{CurrentConfigFile}");

        Provider = new ModelProvider
        {
            ProviderName = ConfigJson["providerName"]?.Value<string>() ?? "deepseek",
            ApiUrl = ConfigJson["apiUrl"]?.Value<string>() ?? "https://api.deepseek.com/anthropic",
            ApiKey = ConfigJson["apiKey"]?.Value<string>() ?? string.Empty,
            ApiKind = NativeApiKind.Authropic,
            AnthropicVersion = ConfigJson["anthropicVersion"]?.Value<string>() ?? "2023-06-01",
        };
        ModelName = ConfigJson["modelName"]?.Value<string>() ?? "deepseek-v4-flash";
        Provider.Models.Add(new Model { Name = ModelName, Enable = true });
        SystemPrompt = ConfigJson["systemPrompt"]?.Value<string>() ?? "You are a concise Anthropic Messages API test assistant.";

        logger.LogInformation($"Anthropic test provider: {Provider.ProviderName}, model: {ModelName}, ApiUrl: {Provider.ApiUrl}, KeyConfigured: {!Provider.ApiKey.IsNullOrEmpty()}");
    }
}

