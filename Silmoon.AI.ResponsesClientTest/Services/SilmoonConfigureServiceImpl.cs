using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Extensions.Hosting.Services;

namespace Silmoon.AI.ResponsesClientTest.Services;

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
            ProviderName = ConfigJson["providerName"]?.Value<string>(),
            ApiUrl = ConfigJson["apiUrl"]?.Value<string>(),
            ApiKey = ConfigJson["apiKey"]?.Value<string>(),
            ApiKind = NativeApiKind.Responses,
        };
        ModelName = ConfigJson["modelName"]?.Value<string>();
        Provider.Models.Add(new Model { Name = ModelName, Enable = true });
        SystemPrompt = ConfigJson["systemPrompt"]?.Value<string>();

        logger.LogInformation($"Responses test provider: {Provider.ProviderName}, model: {ModelName}, ApiUrl: {Provider.ApiUrl}, KeyConfigured: {!Provider.ApiKey.IsNullOrEmpty()}");
    }
}
