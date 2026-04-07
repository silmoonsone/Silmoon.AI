using Microsoft.Extensions.Options;
using Silmoon.AI.Client.OpenAI;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Extensions.Hosting.Services;
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
