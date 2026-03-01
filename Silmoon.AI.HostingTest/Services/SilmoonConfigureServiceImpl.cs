using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Extensions.Hosting.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Silmoon.AI.HostingTest.Services
{
    public class SilmoonConfigureServiceImpl : SilmoonConfigureService
    {
        public string AIKey { get; set; }
        public string AIModelName { get; set; }
        public Dictionary<string, string> SystemPrompts { get; set; } = [];
        ILogger<ISilmoonConfigureService> Logger { get; set; }

        public SilmoonConfigureServiceImpl(IOptions<SilmoonConfigureServiceOption> options, ILogger<ISilmoonConfigureService> logger) : base(options)
        {
            Logger = logger;
            Logger.LogInformation($"当前配置文件{CurrentConfigFilePath}");


            AIKey = ConfigJson["aiKey"]?.Value<string>();
            AIModelName = ConfigJson["aiModelName"]?.Value<string>();

            string defaultPrompt = """
            你是一个AI助手，旨在帮助用户解决问题和提供信息。请确保你的回答准确、简洁，并且易于理解。你可以回答各种问题，包括但不限于技术支持、常识性问题、编程帮助等。请保持专业和友好的语气。
            """;
            SystemPrompts[string.Empty] = defaultPrompt;
        }
    }
}
