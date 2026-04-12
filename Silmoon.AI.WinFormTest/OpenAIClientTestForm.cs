using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Client.OpenAI.Enums;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.AI.Client.ToolCall;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Services;
using Silmoon.Models;
using System;
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
            NativeChatClient = new NativeChatClient(ConfigureService.ConfigJson.Value<string>("aiApiUrl"), ConfigureService.ConfigJson.Value<string>("aiKey"), ConfigureService.ConfigJson.Value<string>("aiModelName"), systemPrompt);
            NativeChatClient.OnToolCallInvoke += NativeChatClient_OnToolCallInvoke;
            NativeChatClient.OnToolCallFinished += NativeChatClient_OnToolCallFinished;
        }
        private Task<StateSet<bool, MessageContent>> NativeChatClient_OnToolCallFinished(StateSet<bool, MessageContent> arg)
        {
            if (arg.State) Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message}", ConsoleColor.Cyan);
            else Console.WriteLineWithColor($"[TOOL RESULT] State: {arg.State}, Message: {arg.Message}", ConsoleColor.Red);
            return Task.FromResult(arg);
        }
        private async Task<StateSet<bool, MessageContent>> NativeChatClient_OnToolCallInvoke(string functionName, Newtonsoft.Json.Linq.JObject parameters, string toolCallId)
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
                case "FileTool":
                    var fileSystemResult = LocalFileSystemTool.ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"].Value<string>());
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, fileSystemResult.ToJsonString(), toolCallId));
                    break;
                default:
                    result = await CommandTool.CallTool(functionName, parameters, toolCallId);
                    if (result is null)
                        result = false.ToStateSet<MessageContent>(null, $"函数 {functionName} 不存在。");
                    break;
            }
            return result;
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
                .. CommandTool.GetTools(),
            ];
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            var userPrompt = textBox3.Text;
            textBox2.Text = string.Empty;
            textBox3.Text = string.Empty;


            List<Chunk> chunks = [];
            await foreach (var chunk in NativeChatClient.CompletionsStreamAsync(userPrompt, chunks, [.. makeTools()]))
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
            Console.WriteLine();
            var result = Result.Create([.. chunks]);
            Console.WriteLine($"FinishReason={result.FinishReason}");
            if (result.FinishReason == "tool_calls") Console.WriteWithColor(result.ToolCalls.ToFormattedJsonString(), ConsoleColor.DarkYellow);
        }
    }
}
