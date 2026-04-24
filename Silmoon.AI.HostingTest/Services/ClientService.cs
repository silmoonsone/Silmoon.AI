using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Prompts;
using Silmoon.AI.Tools;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
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
        NativeChatClient = new NativeChatClient(SilmoonConfigureService.ApiUrl, SilmoonConfigureService.Key, SilmoonConfigureService.ModelName, UtilPrompt.ContextPrompt);
        NativeChatClient.OnToolCallInvoke += NativeChatClient_OnToolCallInvoke;
        NativeChatClient.OnToolCallFinished += NativeChatClient_OnToolCallFinished;
        NativeChatClient.Tools.AddRange(makeTools());
        new FileTool().InjectToolCall(NativeChatClient);
        new CommandTool().InjectToolCall(NativeChatClient);
        new WaitTool().InjectToolCall(NativeChatClient);
        // Inject 须在宿主 OnToolCallInvoke 之后，使续接工具的处理排在多播链末尾，覆盖 default→CommandTool
        new MemoryTool(NativeChatClient).InjectToolCall(NativeChatClient);
        //NativeChatClient.EnableThinking = true;
    }

    private Task<StateSet<bool, string>> NativeChatClient_OnToolCallFinished(StateSet<bool, string> arg)
    {
        if (arg.State) Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message}", ConsoleColor.Cyan);
        else Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message}", ConsoleColor.Red);
        return Task.FromResult(arg);
    }
    private async Task<StateSet<bool, string>> NativeChatClient_OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, string> toolMessageState)
    {
        Console.WriteLine();
        Console.WriteLineWithColor($"[TOOL CALL] {functionName}", ConsoleColor.Yellow);
        StateSet<bool, string> result = null;

        switch (functionName)
        {
            case "QuoteTool":
                if (parameters["symbol"].Value<string>() == "XAUUSD") result = true.ToStateSet<string>(4800m.ToJsonString());
                else result = false.ToStateSet<string>(null, $"产品符号 {parameters["symbol"].Value<string>()} 是错误的，我们接受的应该是国际通用的产品符号，并且没有斜杠分割（/），如果是大模型调用本函数，请尝试更正后自动再次发起查询，但是务必告知用户正确的符号。");
                break;
            case "TradingController":
                result = false.ToStateSet<string>(null, $"无法执行 {parameters["action"].Value<string>()} 操作，因为这是一个模拟调用。");
                break;
            default:
                break;
        }
        return result;
    }

    private List<Tool> makeTools()
    {
        return [
            Tool.Create("QuoteTool", "A tool to inquery quotes for symbol or product code.",
            [
                new ToolParameterProperty("string", "symbol", "The symbol or product code to query quotes for.", null, true),
            ]),
            Tool.Create("TradingController", "A tool to control trading client.",
            [
                new ToolParameterProperty("string", "action", "The action to perform on the trading client.", ["start", "stop", "pause", "resume"], true),
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
                            NativeChatClient.ResetHistory();
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
                        await foreach (var chunk in NativeChatClient.CompletionsStreamAsync(input, chunks))
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
