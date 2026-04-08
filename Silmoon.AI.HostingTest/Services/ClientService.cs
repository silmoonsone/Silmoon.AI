using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Client.OpenAI.Enums;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.AI.Client.Prompts;
using Silmoon.AI.Client.ToolCall;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Models;
using System;

namespace Silmoon.AI.HostingTest.Services;

public class ClientService : IHostedService
{
    NativeChatClient NativeChatClient { get; set; }
    SilmoonConfigureServiceImpl SilmoonConfigureService { get; set; }
    IHostApplicationLifetime ApplicationLifetime { get; set; }
    public ClientService(ISilmoonConfigureService silmoonConfigureService, IHostApplicationLifetime applicationLifetime)
    {
        ApplicationLifetime = applicationLifetime;
        ApplicationLifetime.ApplicationStarted.Register(async () => await Start());
        SilmoonConfigureService = silmoonConfigureService as SilmoonConfigureServiceImpl;
        NativeChatClient = new NativeChatClient(SilmoonConfigureService.AIApiUrl, SilmoonConfigureService.AIKey, SilmoonConfigureService.AIModelName, UtilPrompt.LocalMachineAgentPrompt);
        NativeChatClient.OnToolCallInvoke += NativeChatClient_OnToolCallInvoke;
        NativeChatClient.OnToolCallFinished += NativeChatClient_OnToolCallFinished;
        //NativeChatClient.EnableThinking = true;
    }

    private Task<StateSet<bool, MessageContent>> NativeChatClient_OnToolCallFinished(StateSet<bool, MessageContent> arg)
    {
        if (arg.State) Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message}", ConsoleColor.Cyan);
        else Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message}", ConsoleColor.Red);
        return Task.FromResult(arg);
    }
    private Task<StateSet<bool, MessageContent>> NativeChatClient_OnToolCallInvoke(string functionName, JObject parameters, string toolCallId)
    {
        Console.WriteLine();
        Console.WriteLineWithColor($"[TOOL CALL] {functionName}", ConsoleColor.Yellow);
        StateSet<bool, MessageContent> result;

        switch (functionName)
        {
            case "QuoteTool":
                if (parameters["symbol"].Value<string>() == "XAUUSD") result = true.ToStateSet(MessageContent.Create(Role.Tool, StateSet<bool, decimal>.Create(true, 4800m).ToJsonString(), toolCallId));
                else result = false.ToStateSet<MessageContent>(null, $"产品符号 {parameters["symbol"].Value<string>()} 是错误的，我们接受的应该是国际通用的产品符号，并且没有斜杠分割（/），如果是大模型调用本函数，请尝试更正后自动再次发起查询，但是务必告知用户正确的符号。");
                break;
            case "TradingController":
                result = false.ToStateSet<MessageContent>(null, $"无法执行 {parameters["action"].Value<string>()} 操作，因为这是一个模拟调用。");
                break;
            case "CommandTool":
                var commandResult = CommandTool.Execute(parameters["os"].Value<string>(), parameters["command"].Value<string>(), parameters["terminalType"].Value<string>());
                result = true.ToStateSet(MessageContent.Create(Role.Tool, commandResult, toolCallId));
                break;
            case "FileTool":
                var fileSystemResult = LocalFileSystemTool.ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"].Value<string>());
                result = true.ToStateSet(MessageContent.Create(Role.Tool, fileSystemResult.ToJsonString(), toolCallId));
                break;
            default:
                result = false.ToStateSet<MessageContent>(null, $"函数 {functionName} 不存在。");
                break;
        }
        return Task.FromResult(result);
    }

    private List<Tool> makeTools()
    {
        return [
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
            Tool.Create("QuoteTool", "A tool to inquery quotes for symbol or product code.",
            [
                new ToolParameterProperty("string", "The symbol or product code to query quotes for.", null, "symbol", true),
            ]),
            Tool.Create("TradingController", "A tool to control trading client.",
            [
                new ToolParameterProperty("string", "The action to perform on the trading client.", ["start", "stop", "pause", "resume"], "action", true),
            ]),
        ];
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task Start(bool stream = true)
    {
        await Task.Run(async () =>
        {
            await Task.Delay(500);
            while (true)
            {
                Console.Write(Role.User + ": ");
                string input = Console.ReadLine();
                if (input.IsNullOrEmpty()) continue;
                if (input.FirstOrDefault() == '@')
                {
                    string command = input[1..].Trim();
                    switch (command)
                    {
                        case "clear":
                            NativeChatClient.ClearHistory();
                            Console.WriteLine("Message history cleared.");
                            break;
                        case "exit":
                            Console.WriteLine("Exiting application...");
                            ApplicationLifetime.StopApplication();
                            return;
                        default:
                            Console.WriteLine($"Unknown command: {command}");
                            break;
                    }
                }
                else
                {
                    Console.Write(Role.Assistant + ": ");

                    if (stream)
                    {
                        List<Chunk> chunks = [];
                        await foreach (var chunk in NativeChatClient.CompletionsStreamAsync(input, chunks, [.. makeTools()]))
                        {
                            if (chunk.State)
                            {
                                chunk.Data.Choices.Each(x =>
                                {
                                    if (x.Delta?.ToolCalls is not null) Console.Write(".");
                                    else
                                    {
                                        Console.WriteWithColor(x?.Delta?.GetThinking(), ConsoleColor.DarkGray);
                                        Console.WriteWithColor(x?.Delta?.Content, ConsoleColor.White);
                                    }
                                });
                            }
                            else Console.WriteLineWithColor(chunk.Message, ConsoleColor.Red);
                        }
                        Console.WriteLine();
                        var result = Result.Create([.. chunks]);
                        Console.WriteLine($"FinishReason={result.FinishReason}");
                        if (result.FinishReason == "tool_calls") Console.WriteWithColor(result.ToolCalls.ToFormattedJsonString(), ConsoleColor.DarkYellow);
                    }
                    else
                    {
                        Response response = await NativeChatClient.CompletionsAsync(input);
                        response.Choices.Each(x => Console.WriteWithColor(x?.Message?.Content, ConsoleColor.White));
                        Console.WriteLine($"FinishReason={response.Choices[0].FinishReason}");
                        if (response.Choices[0].FinishReason == "tool_calls") Console.WriteWithColor(response.Choices[0].Message.ToolCalls?.ToFormattedJsonString(), ConsoleColor.DarkYellow);
                    }
                    Console.WriteLine();
                }
            }
        });
    }
}
