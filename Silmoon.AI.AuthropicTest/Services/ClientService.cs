using Microsoft.Extensions.Hosting;
using Silmoon.AI.Anthropic;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.OpenAI.Models;
using Silmoon.AI.Prompts;
using Silmoon.AI.Tools;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Models;

namespace Silmoon.AI.AuthropicTest.Services;

public class ClientService : BackgroundService
{
    readonly NativeAnthropicClient client;
    readonly IHostApplicationLifetime applicationLifetime;

    bool Streaming { get; set; } = true;

    public ClientService(ISilmoonConfigureService silmoonConfigureService, IHostApplicationLifetime applicationLifetime)
    {
        this.applicationLifetime = applicationLifetime;
        var configure = (SilmoonConfigureServiceImpl)silmoonConfigureService;
        client = new NativeAnthropicClient(configure.Provider, configure.ModelName, $"{UtilPrompt.ContextPrompt}\r\n{configure.SystemPrompt}", disableProxy: false, httpRequestTimeoutMilliseconds: 120_000);
        client.OnToolCallsStart += Client_OnToolCallsStart;
        client.OnToolExecuting += Client_OnToolExecuting;
        client.OnToolExecuted += Client_OnToolExecuted;
        client.OnToolCallsFinish += Client_OnToolCallsFinish;
        client.OnStreamOutputCompleted += Client_OnStreamOutputCompleted;

        client.Tools.Add(Tool.Create("Test_GetSecretCode", "Return a fixed test code. Use this when the user asks to test tool calling.", []));
        client.OnToolCallInvoke += Client_OnToolCallInvoke;

        new WorldStateTool().InjectToolCall(client);
        new WaitTool().InjectToolCall(client);
        new MemoryTool(client).InjectToolCall(client);
    }

    Task Client_OnToolCallsStart(ToolCallParameter[] toolCallParameters)
    {
        Console.WriteLineWithColor($"[TOOL CALLS] {string.Join(',', toolCallParameters.Select(x => x.FunctionName))}", ConsoleColor.Yellow);
        return Task.CompletedTask;
    }

    Task<ToolCallResult> Client_OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
    {
        if (toolCallParameter.FunctionName == "Test_GetSecretCode")
            return Task.FromResult(ToolCallResult.Create(toolCallParameter, true.ToStateSet<object>("ANTHROPIC_TOOL_OK")));
        return Task.FromResult(toolCallResult);
    }

    Task Client_OnToolExecuting(string functionName, ToolCallParameter toolCallParameter)
    {
        Console.WriteLineWithColor($"[Tool Executing] {functionName}", ConsoleColor.Cyan);
        return Task.CompletedTask;
    }

    Task Client_OnToolExecuted(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
    {
        var color = toolCallResult?.Result?.State == true ? ConsoleColor.Cyan : ConsoleColor.Red;
        Console.WriteLineWithColor($"[Tool Executed] {functionName}, State={toolCallResult?.Result?.State}, Message={toolCallResult?.Result?.Message}", color);
        return Task.CompletedTask;
    }

    Task<ToolCallResult[]> Client_OnToolCallsFinish(ToolCallParameter[] toolCallParameters, ToolCallResult[] toolCallResults)
    {
        Console.WriteLineWithColor($"[TOOL CALLS RESULTS] {string.Join(", ", toolCallResults.Select(x => $"{x.Parameter.FunctionName}: {x.Result.State}"))}", ConsoleColor.Yellow);
        return Task.FromResult(toolCallResults);
    }

    Task Client_OnStreamOutputCompleted(Result result)
    {
        Console.WriteLine();
        Console.WriteLine($"stop reason: {result.FinishReason}");
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(300, stoppingToken);
        Console.WriteLine("DeepSeek Anthropic test ready. Commands: @stream, @nostream, @clear, @exit");
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

            if (string.IsNullOrEmpty(input)) continue;

            if (input[0] == '@')
            {
                switch (input[1..].Trim().ToLowerInvariant())
                {
                    case "stream":
                        Streaming = true;
                        Console.WriteLine("stream=true");
                        break;
                    case "nostream":
                        Streaming = false;
                        Console.WriteLine("stream=false");
                        break;
                    case "clear":
                        client.ClearHistory();
                        Console.WriteLine("Message history cleared.");
                        break;
                    case "exit":
                        applicationLifetime.StopApplication();
                        return;
                    default:
                        Console.WriteLine("Unknown command.");
                        break;
                }
                continue;
            }

            Console.Write(Role.Assistant + ": ");
            if (Streaming)
            {
                List<ChatCompletionsChunk> chunks = [];
                await foreach (var chunk in client.CompletionsStreamAsync(input, chunks))
                {
                    if (!chunk.State)
                    {
                        Console.WriteLineWithColor(chunk.Message, ConsoleColor.Red);
                        continue;
                    }
                    chunk.Data.Choices.Each(x =>
                    {
                        if (x.Delta?.ToolCalls is not null) Console.Write(".");
                        else Console.WriteWithColor(x.Delta?.Content, ConsoleColor.White);
                    });
                }
            }
            else
            {
                var response = await client.CompletionsAsync(input);
                response.Choices.Each(x => Console.WriteWithColor(x.Message?.Content, ConsoleColor.White));
                Console.WriteLine();
                Console.WriteLine($"FinishReason={response.Choices.FirstOrDefault()?.FinishReason}");
            }
            Console.WriteLine();
        }
    }
}

