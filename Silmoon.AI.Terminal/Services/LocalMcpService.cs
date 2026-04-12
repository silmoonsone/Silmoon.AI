using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.AI.Client.Prompts;
using Silmoon.AI.Client.ToolCall;
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
                .. LocalFileSystemTool.GetTools(),
                .. CommandTool.GetTools(),
            ];
        }
        void HijackToolCall(NativeChatClient nativeChatClient)
        {
            nativeChatClient.OnToolCallInvoke += async (functionName, parameters, toolCallId) =>
            {
                Console.WriteLineWithColor($"[TOOL CALL(HIJACKED)] {functionName}", ConsoleColor.Magenta);
                return await CommandTool.CallTool(functionName, parameters, toolCallId);
            };
        }
    }
}
