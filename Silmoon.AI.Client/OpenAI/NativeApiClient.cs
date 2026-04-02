using System;
using Silmoon.AI.Client.OpenAI.Enums;
using Silmoon.AI.Client.OpenAI.Interfaces;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.Client.OpenAI;

public class NativeApiClient : INativeApiClient
{
    public string ApiUrl { get; set; }
    public string ApiKey { get; set; }
    SseHttpClient HttpClient { get; set; }
    public string Model { get; set; } = "qwen-plus";
    public List<MessageContent> MessageHistory { get; set; } = [];
    public string SystemPrompt { get; set; }
    public NativeApiClient(string apiUrl, string apiKey, string model, string systemPrompt = null)
    {
        ApiUrl = apiUrl;
        ApiKey = apiKey;
        Model = model;
        SystemPrompt = systemPrompt;
        if (!SystemPrompt.IsNullOrEmpty()) MessageHistory.Add(MessageContent.Create(Role.System, SystemPrompt));
        BuildHttpClient();
    }
    void BuildHttpClient()
    {
        HttpClient?.Dispose();
        HttpClient = new SseHttpClient();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }
    public void ClearHistory()
    {
        MessageHistory.Clear();
    }


    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, bool withHistory = true, List<Chunk> chunks = null, string model = null)
    {
        model ??= Model;

        Request request;
        if (withHistory) request = new Request(model, [.. MessageHistory, MessageContent.Create(Role.User, content)]);
        else request = new Request(model, [MessageContent.Create(Role.User, content)]);

        request.SetEnableThinking(false);
        request.EnableSearch = false;
        request.Tools = [
            new Tool(){
                Type = "function",
                Function = new ToolFunction()
                {
                    Name = "QuoteTool",
                    Description = "A tool to inquery quotes for symbol or product code.",
                    Parameters = new ToolParameters(){
                        Type = "object",
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            {
                                "symbol", new ToolParameterProperty(){ Type = "string", Description = "The symbol or product code to query quotes for." }
                            },
                        },
                        Required = ["symbol"],
                    }
                }
            },
            new Tool(){
                Type = "function",
                Function = new ToolFunction()
                {
                    Name = "Control",
                    Description = "A tool to control trading client.",
                    Parameters = new ToolParameters(){
                        Type = "object",
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            {
                                "action", new ToolParameterProperty(){ Type = "string", Description = "The action to perform on the trading client.", Enum = ["start", "stop", "pause", "resume"] }
                            },
                        },
                        Required = ["action"],
                    }
                }
            }
        ];

        if (withHistory) MessageHistory.Add(request.Messages.FirstOrDefault());
        chunks ??= [];
        await foreach (var chunk in HttpClient.CompletionsStreamAsync(ApiUrl + "/chat/completions", request))
        {
            if (chunk.State) chunks.Add(chunk.Data);
            yield return chunk;
        }

        var result = Result.Create([.. chunks]);

        if (withHistory && result.FinishReason == "stop") MessageHistory.Add(MessageContent.Create(Role.Assistant, result.Content));
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(MessageContent[] messageHistory, List<Chunk> chunks = null, string model = null)
    {
        model ??= Model;
        chunks ??= [];

        var request = new Request(model, messageHistory);
        request.SetEnableThinking(false);
        request.EnableSearch = false;

        await foreach (var chunk in HttpClient.CompletionsStreamAsync(ApiUrl + "/chat/completions", request))
        {
            if (chunk.State) chunks.Add(chunk.Data);
            yield return chunk;
        }

        var result = Result.Create([.. chunks]);

        if (result.FinishReason == "stop") yield break;
    }
    public async Task<Response> CompletionsAsync(string content, bool withHistory = true, string model = null)
    {
        model ??= Model;
        Request request;

        if (withHistory) request = new Request(model, [.. MessageHistory, MessageContent.Create(Role.User, content)]);
        else request = new Request(model, [MessageContent.Create(Role.User, content)]);

        request.SetEnableThinking(false);
        request.EnableSearch = false;
        request.Tools = [
            new Tool(){
                Type = "function",
                Function = new ToolFunction()
                {
                    Name = "QuoteTool",
                    Description = "A tool to inquery quotes for symbol or product code.",
                    Parameters = new ToolParameters(){
                        Type = "object",
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            {
                                "symbol", new ToolParameterProperty(){ Type = "string", Description = "The symbol or product code to query quotes for." }
                            }
                        }
                    }
                }
            }
        ];


        var response = await HttpClient.CompletionsAsync(ApiUrl + "/chat/completions", request);

        if (withHistory)
        {
            MessageHistory.Add(request.Messages.FirstOrDefault());
            MessageHistory.Add(MessageContent.Create(Role.Assistant, response.Data.Choices.FirstOrDefault()?.Message?.Content));
        }

        return response.Data;
    }
}
