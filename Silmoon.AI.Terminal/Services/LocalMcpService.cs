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
        }

        void InjectSystemPrompting(NativeChatClient nativeChatClient)
        {
            string systemPrompt = SilmoonConfigureService.SystemPrompt;
            if (systemPrompt is not null) nativeChatClient.SystemPrompt += "\r\n" + systemPrompt;
        }
        void InjectTools(NativeChatClient nativeChatClient)
        {
            nativeChatClient.Tools = [
                Tool.Create("FileTool", "It provides the ability to read and write text files, serving as an alternative when writing and reading large amounts of text using methods similar to command lines or terminals. This tool is not essential; it is simply used to reduce the probability of operations when manipulating large amounts of text files. It returns a JSON object, where Data is the text content.",
                [
                    new ToolParameterProperty("string", "The action to perform on the file system.", ["write", "read"], "action", true),
                    new ToolParameterProperty("string", "The path of the file to operate on.", null, "path", true),
                    new ToolParameterProperty("string", "This parameter is ignored when reading; it can be replaced with null.", null, "content", true),
                ]),
                //Tool.Create("QuoteTool", "A tool to inquery quotes for symbol or product code.",
                //[
                //    new ToolParameterProperty("string", "The symbol or product code to query quotes for.", null, "symbol", true),
                //]),
                //Tool.Create("TradingController", "A tool to control trading client.",
                //[
                //    new ToolParameterProperty("string", "The action to perform on the trading client.", ["start", "stop", "pause", "resume"], "action", true),
                //]),
                .. CommandTool.GetTools(),
            ];
        }
    }
}
