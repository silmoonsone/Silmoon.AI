using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Tools;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Terminal.Services
{
    public class LocalMcpService
    {
        public List<IExecuteTool> ExecuteTools { get; set; } = [
            new FileTool(),
            new CommandTool(),
            new WaitTool(),
            ];
        SilmoonConfigureServiceImpl SilmoonConfigureService { get; set; }
        public LocalMcpService(ISilmoonConfigureService silmoonConfigureService)
        {
            SilmoonConfigureService = silmoonConfigureService as SilmoonConfigureServiceImpl;
        }
        public void InjectMcp(NativeChatClient nativeChatClient)
        {
            ExecuteTools.Add(new DeepThinkTool(nativeChatClient));
            ExecuteTools.Add(new MemoryTool(nativeChatClient));

            string systemPrompt = SilmoonConfigureService.SystemPrompt;
            if (systemPrompt is not null) nativeChatClient.SystemPrompt += "\r\n" + systemPrompt;

            foreach (var tool in ExecuteTools)
            {
                tool.InjectToolCall(nativeChatClient);
            }
        }
    }
}
