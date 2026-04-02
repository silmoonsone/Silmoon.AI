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
        NativeApiClient NativeApiClient { get; set; }
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
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            var systemPrompt = textBox1.Text;
            var userPrompt = textBox3.Text;
            textBox2.Text = string.Empty;
            textBox3.Text = string.Empty;
            NativeApiClient = new NativeApiClient(ConfigureService.ConfigJson.Value<string>("aiApiUrl"), ConfigureService.ConfigJson.Value<string>("aiKey"), ConfigureService.ConfigJson.Value<string>("aiModelName"), systemPrompt);

            List<Chunk> chunks = [];
            await foreach (var chunk in NativeApiClient.CompletionsStreamAsync(userPrompt, true, chunks))
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
            Console.WriteLine($"\nFinishReason={result.FinishReason}");
            if (result.FinishReason == "tool_calls") Console.WriteWithColor(result.ToolCalls.ToFormattedJsonString(), ConsoleColor.DarkYellow);
        }
    }
}
