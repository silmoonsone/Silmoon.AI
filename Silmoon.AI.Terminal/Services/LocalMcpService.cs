using Silmoon.AI.Client.OpenAI;
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
        SilmoonConfigureServiceImpl SilmoonConfigureService { get; set; }
        public LocalMcpService(ISilmoonConfigureService silmoonConfigureService)
        {
            SilmoonConfigureService = silmoonConfigureService as SilmoonConfigureServiceImpl;
        }
        public void InjectMcp(NativeChatClient nativeChatClient)
        {
            InjectSystemPrompting(nativeChatClient);
            InjectTools(nativeChatClient);
            HijackToolCall(nativeChatClient);
        }

        void InjectSystemPrompting(NativeChatClient nativeChatClient)
        {
            string systemPrompt = SilmoonConfigureService.SystemPrompt;
            if (systemPrompt is not null) nativeChatClient.SystemPrompt += "\r\n" + systemPrompt;
        }
        void InjectTools(NativeChatClient nativeChatClient)
        {
            nativeChatClient.Tools = [
                .. nativeChatClient.Tools,
                .. FileTool.GetTools(),
                .. CommandTool.GetTools(),
            ];
        }
        void HijackToolCall(NativeChatClient nativeChatClient)
        {
            nativeChatClient.OnToolCallInvoke += async (functionName, parameters, toolCallId, toolMessageState) =>
            {
                if (toolMessageState is not null) return null;
                Console.WriteLineWithColor($"[TOOL CALL(LocalMcpService)] {functionName}", ConsoleColor.Magenta);
                var result = await CommandTool.CallTool(functionName, parameters, toolCallId);
                if (result is null) result = await FileTool.CallTool(functionName, parameters, toolCallId);
                return result;
            };
        }
    }
}
