using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.AI.Client.Prompts;
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
                Tool.Create("CommandTool", "It can execute commands on the local computer and supports Windows, macOS, and Linux systems. Please note that you should not use this tool to execute dangerous commands, including power control, modifying or deleting important files and system files. Some high-risk operations require user confirmation before execution. Also, note that CommandTool does not save context with each call; each command is independent. For example, calling `cd ...` a second time renders the previous `cd` command's directory-changing behavior meaningless. Therefore, the `cd` command cannot be used alone. Consider using the \"&&\" in CMD and the \";;\" in PowerShell to pipe the `cd` command in conjunction with other commands. However, note that when using the `cd` command directly in CMD to change directories, switching between drives is ineffective; you must first change the drive letter and then use `cd` to change directories. If multiple commands are to be executed together, it is recommended to use PowerShell throughout!",
                [
                    new ToolParameterProperty("string", "The operating system on which to execute the command.", ["windows", "macos", "linux"], "os", true),
                    new ToolParameterProperty("string", "The command to execute in cmd or powershell.", null, "command", true),
                    new ToolParameterProperty("string", "Which command line should be used in Windows? What parameters are required in Windows? It supports cmd and PowerShell parameters; on other platforms, empty parameters or null parameters can be used, but PowerShell is recommended by default.", ["cmd", "powershell", null], "terminalType", true),
                ]),
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
            ];
        }
    }
}
