using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Prompts;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Models;
using System;

namespace Silmoon.AI.Terminal.Services;

public class ClientService : IHostedService
{
    NativeChatClient NativeChatClient { get; set; }
    SilmoonConfigureServiceImpl SilmoonConfigureService { get; set; }
    IHostApplicationLifetime ApplicationLifetime { get; set; }
    LocalMcpService LocalMcpService { get; set; }
    public ClientService(ISilmoonConfigureService silmoonConfigureService, LocalMcpService localMcpService, IHostApplicationLifetime applicationLifetime)
    {
        ApplicationLifetime = applicationLifetime;
        LocalMcpService = localMcpService;
        ApplicationLifetime.ApplicationStarted.Register(async () => await Start());
        SilmoonConfigureService = silmoonConfigureService as SilmoonConfigureServiceImpl;

        NativeChatClient = new NativeChatClient(SilmoonConfigureService.DefaultProvider, SilmoonConfigureService.DefaultModelName, UtilPrompt.ContextPrompt);
        NativeChatClient.OnToolCallInvoke += NativeChatClient_OnToolCallInvoke;
        NativeChatClient.OnToolCallFinished += NativeChatClient_OnToolCallFinished;
        LocalMcpService.InjectMcp(NativeChatClient);
        NativeChatClient.Tools.Add(Tool.Create("ToolCallTestTool", "This is a test tool_calling test tool.", []));
        //NativeChatClient.EnableThinking = true;
    }

    private Task<StateSet<bool, string>> NativeChatClient_OnToolCallFinished(StateSet<bool, string> arg)
    {
        if (arg.State) Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message ?? "#null"}", ConsoleColor.Cyan);
        else Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message ?? "#null"}", ConsoleColor.Red);
        return Task.FromResult(arg);
    }
    private async Task<StateSet<bool, string>> NativeChatClient_OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, string> toolMessageState)
    {
        Console.WriteLine();
        Console.WriteLineWithColor($"[TOOL CALL] {functionName}", ConsoleColor.Yellow);
        switch (functionName)
        {
            case "ToolCallTestTool":
                return true.ToStateSet<string>($"这是一个工具调用环境测试，正常！");
            default:
                return null;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
                        Console.WriteLine($"【完成{result.FinishReason}】");
                        if (result.FinishReason == "tool_calls") Console.WriteWithColor(result.ToolCalls.ToFormattedJsonString(), ConsoleColor.DarkYellow);
                    }
                    else
                    {
                        Response response = await NativeChatClient.CompletionsAsync(input);
                        response.Choices.Each(x => Console.WriteWithColor(x?.Message?.Content, ConsoleColor.White));
                        Console.WriteLine($"【完成{response.Choices[0].FinishReason}】");
                        if (response.Choices[0].FinishReason == "tool_calls") Console.WriteWithColor(response.Choices[0].Message.ToolCalls?.ToFormattedJsonString(), ConsoleColor.DarkYellow);
                    }
                    Console.WriteLine();
                }
            }
        });
    }
}
