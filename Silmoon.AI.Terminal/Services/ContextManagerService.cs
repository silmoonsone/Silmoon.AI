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
        public List<IToolSet> ToolSets { get; set; } = [
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
        public void InjectMcp(ChatClient nativeClient)
        {
            ToolSets.Add(new DeepThinkTool(nativeClient));
            ToolSets.Add(new MemoryTool(nativeClient));

            string systemPrompt = SilmoonConfigureService.SystemPrompt;
            if (systemPrompt is not null) nativeClient.SystemPrompt += "\r\n" + systemPrompt;

            foreach (var toolSet in ToolSets)
            {
                toolSet.InjectToolCall(nativeClient);
            }
        }
    }
}



