using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.AI.OpenAI;
using Silmoon.AI.Prompts;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;

namespace Silmoon.AI.Terminal.Services;

public class ClientService : BackgroundService
{
    NativeChatClient NativeChatClient { get; set; }
    SilmoonConfigureServiceImpl SilmoonConfigureService { get; set; }
    IHostApplicationLifetime ApplicationLifetime { get; set; }
    ContextManagerService LocalMcpService { get; set; }
    public ClientService(ISilmoonConfigureService silmoonConfigureService, ContextManagerService localMcpService, IHostApplicationLifetime applicationLifetime)
    {
        ApplicationLifetime = applicationLifetime;
        LocalMcpService = localMcpService;
        SilmoonConfigureService = silmoonConfigureService as SilmoonConfigureServiceImpl;

        NativeChatClient = new NativeChatClient(SilmoonConfigureService.DefaultProvider, SilmoonConfigureService.DefaultModelName, UtilPrompt.ContextPrompt);
        NativeChatClient.OnToolCallsStart += NativeChatClient_OnToolCallsStart;
        NativeChatClient.OnToolCallsFinish += NativeChatClient_OnToolCallsFinish;

        NativeChatClient.OnToolExecuting += NativeChatClient_OnToolExecuting;
        NativeChatClient.OnToolExecuted += NativeChatClient_OnToolExecuted;
        NativeChatClient.OnStreamOutputCompleted += NativeChatClient_OnStreamOutputCompleted;
        LocalMcpService.InjectMcp(NativeChatClient);
        NativeChatClient.Tools.Add(Tool.Create("Test_ToolCallTest", "This is a test tool_calling test tool.", []));
        //NativeChatClient.EnableThinking = true;
    }


    private async Task NativeChatClient_OnToolCallsStart(ToolCallParameter[] toolCallParameters)
    {
        Console.WriteLineWithColor($"[TOOL CALLS] {string.Join(',', toolCallParameters.Select(x => x.FunctionName))}", ConsoleColor.Yellow);
    }
    private Task NativeChatClient_OnToolExecuting(string functionName, ToolCallParameter toolCallParameter)
    {
        Console.WriteLineWithColor($"[Tool Executing] ({functionName}) is executing.", ConsoleColor.Cyan);
        return Task.CompletedTask;
    }
    private Task NativeChatClient_OnToolExecuted(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
    {
        if (toolCallResult is not null)
        {
            if (toolCallResult.Result.State)
                Console.WriteLineWithColor($"[Tool Executed] ({functionName}) executed with result: State: {toolCallResult.Result.State}, Message: {toolCallResult.Result.Message}", ConsoleColor.Cyan);
            else
                Console.WriteLineWithColor($"[Tool Executed] ({functionName}) executed with result: State: {toolCallResult.Result.State}, Message: {toolCallResult.Result.Message}", ConsoleColor.Red);
        }
        else
            Console.WriteLineWithColor($"[Tool Executed] ({functionName}) executed with no any result", ConsoleColor.Red);
        return Task.CompletedTask;
    }
    private Task<ToolCallResult[]> NativeChatClient_OnToolCallsFinish(ToolCallParameter[] toolCallParameters, ToolCallResult[] toolCallResults)
    {
        Console.WriteLineWithColor($"[TOOL CALLS RESULTS] {string.Join(", ", toolCallParameters.Select(x => $"{x.FunctionName}: {toolCallResults.FirstOrDefault(y => y.Parameter.FunctionName == x.FunctionName)?.Result.State}"))}", ConsoleColor.Yellow);
        return Task.FromResult(toolCallResults);
    }
    private Task NativeChatClient_OnStreamOutputCompleted(Result result)
    {
        Console.WriteLine();
        Console.WriteLine("stop reason: " + result.FinishReason);
        return Task.CompletedTask;
    }

    bool stream = true;
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(500);
        while (true)
        {
            Console.Write(Role.User + ": ");
            string input = Console.ReadLine();
            if (input.IsNullOrEmpty())
            {
                if (input is null)
                {
                    Console.WriteLine("Input terminated");
                    break;
                }
                else continue;
            }
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
                    var result = Result.Create([.. chunks]);
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
    }
}
