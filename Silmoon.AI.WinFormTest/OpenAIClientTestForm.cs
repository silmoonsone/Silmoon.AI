using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Tools;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
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
            NativeChatClient = new NativeChatClient(ConfigureService.ConfigJson.Value<string>("apiUrl"), ConfigureService.ConfigJson.Value<string>("apiKey"), ConfigureService.ConfigJson.Value<string>("modelName"), systemPrompt);
            NativeChatClient.OnToolCallInvoke += NativeChatClient_OnToolCallInvoke;
            NativeChatClient.OnToolCallFinished += NativeChatClient_OnToolCallFinished;
            NativeChatClient.Tools.AddRange(makeTools());
            new FileTool().InjectToolCall(NativeChatClient);
            new CommandTool().InjectToolCall(NativeChatClient);
            new WaitTool().InjectToolCall(NativeChatClient);
            // Inject 须在宿主 OnToolCallInvoke 之后，使续接工具的处理排在多播链末尾，覆盖 default→CommandTool 对未知函数名的结果
            new MemoryTool(NativeChatClient).InjectToolCall(NativeChatClient);
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
            Console.WriteLine();
            var result = Result.Create([.. chunks]);
            Console.WriteLine($"FinishReason={result.FinishReason}");
            if (result.FinishReason == "tool_calls") Console.WriteWithColor(result.ToolCalls.ToFormattedJsonString(), ConsoleColor.DarkYellow);
        }
    }
}
