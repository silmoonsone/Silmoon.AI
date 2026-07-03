using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI;
using Silmoon.AI.Tools;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Terminal.Services
{
    public class ContextManagerService
    {
        public List<IExecuteTool> ExecuteTools { get; set; } = [
            new FileTool(),
            new CommandTool(),
            new WaitTool(),
            new WorldStateTool(),
            ];
        SilmoonConfigureServiceImpl SilmoonConfigureService { get; set; }
        public ContextManagerService(ISilmoonConfigureService silmoonConfigureService)
        {
            SilmoonConfigureService = silmoonConfigureService as SilmoonConfigureServiceImpl;
        }
        public void InjectMcp(NativeChatCompletionsClient chatCompletionsClient)
        {
            ExecuteTools.Add(new DeepThinkTool(chatCompletionsClient));
            ExecuteTools.Add(new MemoryTool(chatCompletionsClient));

            string systemPrompt = SilmoonConfigureService.SystemPrompt;
            if (systemPrompt is not null) chatCompletionsClient.SystemPrompt += "\r\n" + systemPrompt;

            foreach (var tool in ExecuteTools)
            {
                tool.InjectToolCall(chatCompletionsClient);
            }
        }
    }
}



