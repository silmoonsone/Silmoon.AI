using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI.Models;
using Silmoon.AI.Prompts;
using Silmoon.AI.Tools;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;

namespace Silmoon.AI.HostingTest.Services;

public class ClientService : BackgroundService
{
    INativeClient NativeClient { get; set; }
    IHostApplicationLifetime ApplicationLifetime { get; set; }
    bool Streaming { get; set; } = true;
    public ClientService(ISilmoonConfigureService silmoonConfigureService, IHostApplicationLifetime applicationLifetime)
    {
        ApplicationLifetime = applicationLifetime;
        var configure = silmoonConfigureService as SilmoonConfigureServiceImpl;
        NativeClient = NativeClientFactory.Create(configure.Provider, configure.ModelName, UtilPrompt.ContextPrompt);
        NativeClient.OnToolCallsStart += NativeClient_OnToolCallsStart;
        NativeClient.OnToolExecuting += NativeClient_OnToolExecuting;
        NativeClient.OnToolExecuted += NativeClient_OnToolExecuted;
        NativeClient.OnToolCallsFinish += NativeClient_OnToolCallsFinish;
        NativeClient.OnStreamOutputCompleted += NativeClient_OnStreamOutputCompleted;
        NativeClient.Tools.AddRange(makeTools());
        NativeClient.AddExecuteTools([new FileTool(), new CommandTool(), new WaitTool(), new WorldStateTool()]);
        // Inject 须在宿主 OnToolCallInvoke 之后，使续接工具的处理排在多播链末尾，覆盖 default→CommandTool
        NativeClient.AddExecuteTools([new MemoryTool(NativeClient)]);
    }

    private async Task NativeClient_OnToolCallsStart(ToolCallParameter[] toolCallParameters)
    {
        Console.WriteLineWithColor($"[TOOL CALLS] {string.Join(',', toolCallParameters.Select(x => x.FunctionName))}", ConsoleColor.Yellow);
    }
    private Task NativeClient_OnToolExecuting(string functionName, ToolCallParameter toolCallParameter)
    {
        Console.WriteLineWithColor($"[Tool Executing] ({functionName}) is executing.", ConsoleColor.Cyan);
        return Task.CompletedTask;
    }
    private Task NativeClient_OnToolExecuted(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
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
    private Task<ToolCallResult[]> NativeClient_OnToolCallsFinish(ToolCallParameter[] toolCallParameters, ToolCallResult[] toolCallResults)
    {
        Console.WriteLineWithColor($"[TOOL CALLS RESULTS] {string.Join(", ", toolCallParameters.Select(x => $"{x.FunctionName}: {toolCallResults.FirstOrDefault(y => y.Parameter.FunctionName == x.FunctionName)?.Result.State}"))}", ConsoleColor.Yellow);
        return Task.FromResult(toolCallResults);
    }

    private async Task NativeClient_OnStreamOutputCompleted(Result result)
    {
        Console.WriteLine();
        Console.WriteLine("stop reason: " + result.FinishReason);
        await Task.CompletedTask;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(500, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Write(Role.User + ": ");
            string input;
            try
            {
                input = await Console.In.ReadLineAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (input.IsNullOrEmpty())
            {
                if (input is null)
                {
                    Console.WriteLine("Input terminated");
                    break;
                }
                continue;
            }

            if (input.FirstOrDefault() == '@')
            {
                string command = input[1..].Trim();
                switch (command)
                {
                    case "clear":
                        NativeClient.ClearHistory();
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

                if (Streaming)
                {
                    List<ChatCompletionsChunk> chunks = [];
                    await foreach (var chunk in NativeClient.CompletionsStreamAsync(input, chunks))
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
                    ChatCompletionsResponse response = await NativeClient.CompletionsAsync(input);
                    response.Choices.Each(x => Console.WriteWithColor(x?.Message?.Content, ConsoleColor.White));
                    Console.WriteLine($"FinishReason={response.Choices[0].FinishReason}");
                    if (response.Choices[0].FinishReason == "tool_calls") Console.WriteWithColor(response.Choices[0].Message.ToolCalls?.ToFormattedJsonString(), ConsoleColor.DarkYellow);
                }
                Console.WriteLine();
            }
        }
    }
}


