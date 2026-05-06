using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.AI.OpenAI;
using Silmoon.AI.Tools;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Services;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Silmoon.AI.WinFormTest
{
    public partial class OpenAIClientTestForm : Form
    {
        NativeChatClient NativeChatClient { get; set; }
        SilmoonConfigureService ConfigureService { get; set; }

        public OpenAIClientTestForm()
        {
            InitializeComponent();
            SilmoonConfigureServiceOption option = new SilmoonConfigureServiceOption();
#if DEBUG
            option.DebugConfig();
#else
            option.ReleaseConfig();
#endif
            ConfigureService = SilmoonConfigureService.CreateSingleton(option);

            var systemPrompt = textBox1.Text;
            systemPrompt = MakeSystemPrompt();
            NativeChatClient = new NativeChatClient(ConfigureService.ConfigJson.Value<string>("apiUrl"), ConfigureService.ConfigJson.Value<string>("apiKey"), ConfigureService.ConfigJson.Value<string>("modelName"), systemPrompt);
            NativeChatClient.OnToolCallsStart += NativeChatClient_OnToolCallsStart;
            NativeChatClient.OnToolExecuting += NativeChatClient_OnToolExecuting;
            NativeChatClient.OnToolExecuted += NativeChatClient_OnToolExecuted;
            NativeChatClient.OnToolCallsFinish += NativeChatClient_OnToolCallsFinish;
            NativeChatClient.OnStreamOutputCompleted += NativeChatClient_OnStreamOutputCompleted;
            NativeChatClient.Tools.AddRange(makeTools());
            new FileTool().InjectToolCall(NativeChatClient);
            new CommandTool().InjectToolCall(NativeChatClient);
            new WaitTool().InjectToolCall(NativeChatClient);
            new WorldStateTool().InjectToolCall(NativeChatClient);
            // Inject 须在宿主 OnToolCallStart 之后，使续接工具的处理排在多播链末尾，覆盖 default→CommandTool 对未知函数名的结果
            new MemoryTool(NativeChatClient).InjectToolCall(NativeChatClient);
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

        private async Task NativeChatClient_OnStreamOutputCompleted(Result result)
        {
            Console.WriteLine();
            Console.WriteLine("stop reason: " + result.FinishReason);
            await Task.CompletedTask;
        }

        public string MakeSystemPrompt()
        {
            return $"""
            你是一个人工智能助手，协助用户完成各种任务，以下是一些关于当前环境的信息：
            操作系统: {Environment.OSVersion.VersionString}
            当前目录: {Environment.CurrentDirectory}
            当前时间: {DateTime.Now}
            当前用户: {Environment.UserName}
            当前用户主目录: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}
            """;
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

        private async void button1_Click(object sender, EventArgs e)
        {
            var userPrompt = textBox3.Text;
            textBox2.Text = string.Empty;
            textBox3.Text = string.Empty;


            List<Chunk> chunks = [];
            await foreach (var chunk in NativeChatClient.CompletionsStreamAsync(userPrompt, chunks))
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
                            textBox2.Text += x?.Delta?.GetThinking() + x?.Delta?.Content;
                        }
                    });
                }
                else Console.WriteLineWithColor(chunk.Message, ConsoleColor.Red);
            }
            var result = Result.Create([.. chunks]);
            if (result.FinishReason == "tool_calls") Console.WriteWithColor(result.ToolCalls.ToFormattedJsonString(), ConsoleColor.DarkYellow);
        }
    }
}
