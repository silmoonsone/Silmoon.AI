using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using Silmoon.AspNetCore.Interfaces;
using Silmoon.Extension;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Silmoon.AI.HostingTest.Services
{
    public class AIService : IHostedService
    {
        OpenAIClient AIClient { get; set; }
        IChatClient ChatClient { get; set; }
        Dictionary<string, List<ChatMessage>> ChatList { get; set; } = [];
        ILogger<AIService> Logger { get; set; }
        SilmoonConfigureServiceImpl SilmoonConfigureService { get; set; }
        CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();

        public AIService(ISilmoonConfigureService silmoonConfigureService, ILogger<AIService> logger)
        {
            Logger = logger;
            SilmoonConfigureService = silmoonConfigureService as SilmoonConfigureServiceImpl;
            AIClient = new OpenAIClient(new ApiKeyCredential(SilmoonConfigureService.AIKey), new OpenAIClientOptions() { Endpoint = new Uri("https://dashscope.aliyuncs.com/compatible-mode/v1"), });
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await StartAI();
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource.Cancel();
            await Task.CompletedTask;
        }


        private async Task ReadyConsoleInput()
        {
            await Task.Run(async () =>
            {
                while (!CancellationTokenSource.IsCancellationRequested)
                {
                    string input = Console.ReadLine();
                    if (input.IsNullOrEmpty()) continue;
                    if (input.StartsWith('@'))
                    {
                        switch (input)
                        {
                            case "@clear":
                                if (ChatList.ContainsKey(string.Empty)) ChatList.Remove(string.Empty);
                                Console.WriteLine("已清除当前会话的聊天记录。");
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        if (!ChatList.ContainsKey(string.Empty)) ChatList[string.Empty] = [];
                        var chatHistory = ChatList[string.Empty];

                        if (chatHistory.Count == 0)
                        {
                            var systemPrompt = SilmoonConfigureService.SystemPrompts.GetValueOrDefault(string.Empty, null);
                            if (!systemPrompt.IsNullOrEmpty()) chatHistory.Add(new ChatMessage(ChatRole.System, systemPrompt));
                        }

                        chatHistory.Add(new ChatMessage(ChatRole.User, input));


                        var reply = string.Empty;
                        await foreach (ChatResponseUpdate update in ChatClient.GetStreamingResponseAsync(chatHistory, new ChatOptions()))
                        {
                            reply += update.ToString();
                            Console.Write(update);
                        }
                        chatHistory.Add(new ChatMessage(ChatRole.Assistant, reply.Trim()));
                        Console.WriteLine();
                    }
                }
            });
        }

        public async Task StartAI()
        {
            Logger.LogInformation($"正在准备AI客户端，模型{SilmoonConfigureService.AIModelName}...");
            var model = await AIClient.GetOpenAIModelClient().GetModelAsync(SilmoonConfigureService.AIModelName);
            ChatClient = AIClient.GetChatClient(model.Value.Id).AsIChatClient();
            Logger.LogInformation("AI客户端已启动");
            _ = ReadyConsoleInput();
        }
    }
}
