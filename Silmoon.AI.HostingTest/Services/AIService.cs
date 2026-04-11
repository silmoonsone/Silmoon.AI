using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using Silmoon.Extensions.Hosting.Interfaces;
using Silmoon.Extensions;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
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
            AIClient = new OpenAIClient(new ApiKeyCredential(SilmoonConfigureService.Key), new OpenAIClientOptions() { Endpoint = new Uri("https://dashscope.aliyuncs.com/compatible-mode/v1"), });
            //AIClient = new OpenAIClient(new ApiKeyCredential(SilmoonConfigureService.AIKey), new OpenAIClientOptions() { Endpoint = new Uri("http://localhost:11434/v1"), });
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
                                ChatList.Remove(string.Empty);
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

                        try
                        {
                            var chatOptions = new ChatOptions
                            {
                                Tools = [
                                    AIFunctionFactory.Create(PriceTool.GetPrice, "get_price", "获取指定 symbol/ticker（如股票代码、加密货币代码等）的当前价格"),
                                    AIFunctionFactory.Create(PriceTool.GetPrice2, "get_price_2", "获取指定 symbol/ticker（如股票代码、加密货币代码等）的当前价格"),
                                ],
                            };
                            //chatOptions.AdditionalProperties = [];
                            //chatOptions.AdditionalProperties["enable_thinking"] = true;
                            //chatOptions.AdditionalProperties["enable_search"] = true;


                            var reply = string.Empty;
                            Console.WriteLine("<<<START>>>");

                            await foreach (ChatResponseUpdate update in ChatClient.GetStreamingResponseAsync(chatHistory, chatOptions))
                            {
                                global::OpenAI.Chat.StreamingChatCompletionUpdate streamingChatCompletionUpdate = update.RawRepresentation as global::OpenAI.Chat.StreamingChatCompletionUpdate;
                                //Console.WriteLine(">" + streamingChatCompletionUpdate?.ToolCallUpdates.Count);
                                //Console.WriteLine(">" + update.FinishReason);
                                if (streamingChatCompletionUpdate?.ToolCallUpdates != null)
                                {
                                    if (streamingChatCompletionUpdate?.ToolCallUpdates.Count > 0 || update.FinishReason == ChatFinishReason.ToolCalls)
                                    {
                                        //foreach (var toolCall in streamingChatCompletionUpdate.ToolCallUpdates)
                                        //{
                                        //    Console.WriteLine($"TOOL > 工具调用：{toolCall?.FunctionName}，参数：{toolCall?.FunctionArgumentsUpdate}");
                                        //}
                                        continue;
                                    }
                                }
                                reply += update.Text;
                                Console.Write(update);
                            }
                            chatHistory.Add(new ChatMessage(ChatRole.Assistant, reply.Trim()));
                            Console.WriteLine();
                            Console.WriteLine("<<<END>>>");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"发生错误：{ex}");
                        }
                    }
                }
            });
        }

        public async Task StartAI()
        {
            Logger.LogInformation($"正在准备AI客户端，模型{SilmoonConfigureService.ModelName}...");
            //var models = await AIClient.GetOpenAIModelClient().GetModelsAsync();
            var model = await AIClient.GetOpenAIModelClient().GetModelAsync(SilmoonConfigureService.ModelName);
            var chatClient = AIClient.GetChatClient(model.Value.Id).AsIChatClient();
            ChatClient = new ChatClientBuilder(chatClient).UseFunctionInvocation().Build();
            Logger.LogInformation("AI客户端已启动");
            _ = ReadyConsoleInput();
        }
    }
    public class PriceTool
    {
        [Description("获取指定 symbol/ticker（如股票代码、加密货币代码等）的当前价格")]
        public static string GetPrice(string symbol)
        {
            return new { Error = true, Message = "需要调用get_price_2获取黄金价格，如果命中这个问题，需要告诉用户正在调用get_price_xauusd！" }.ToJsonString();
        }
        [Description("获取指定 symbol/ticker（如股票代码、加密货币代码等）的当前价格")]
        public static decimal GetPrice2(string symbol)
        {
            return 4420.00m;
        }
    }
}
