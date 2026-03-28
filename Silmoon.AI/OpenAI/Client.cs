using System;
using Silmoon.AI.Enums;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Network;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.OpenAI;

public class Client : IClient
{
    public string ApiUrl { get; set; }
    public string ApiKey { get; set; }
    SseHttpClient HttpClient { get; set; }
    public string Model { get; set; } = "qwen-plus";
    public List<MessageContent> MessageHistory { get; set; } = [];

    public Client(string apiUrl, string apiKey, string model)
    {
        ApiUrl = apiUrl;
        ApiKey = apiKey;
        Model = model;
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
    // public async Task<StateSet<bool, Result>> CompletionsAsync(string content, string model = null)
    // {
    //     model ??= Model;
    //     var request = new Request(model, [MessageContent.Create(Role.User, content)], true);
    //     List<Chunk> responses = [];
    //     var result = await HttpClient.CompletionsAsync(ApiUrl + "/chat/completions", request);
    //     return true.ToStateSet(result);
    // }


    public async IAsyncEnumerable<Chunk> CompletionsStreamAsync(string content, bool withHistory = true, List<Chunk> chunks = null, string model = null)
    {
        model ??= Model;
        Request request;
        chunks ??= [];

        if (withHistory) request = new Request(model, [.. MessageHistory, MessageContent.Create(Role.User, content)]);
        else request = new Request(model, [MessageContent.Create(Role.User, content)]);

        request.SetEnableThinking(true);
        request.EnableSearch = true;

        await foreach (var chunk in HttpClient.CompletionsStreamAsync(ApiUrl + "/chat/completions", request))
        {
            if (withHistory) chunks.Add(chunk);
            yield return chunk;
        }

        var result = Result.Create([.. chunks]);

        if (withHistory)
        {
            MessageHistory.Add(request.Messages.FirstOrDefault());
            MessageHistory.Add(MessageContent.Create(Role.Assistant, result.Content));
        }

        if (result.FinishReason == "stop") yield break;
    }
    public async Task<Response> CompletionsAsync(string content, bool withHistory = true, string model = null)
    {
        model ??= Model;
        Request request;

        if (withHistory) request = new Request(model, [.. MessageHistory, MessageContent.Create(Role.User, content)]);
        else request = new Request(model, [MessageContent.Create(Role.User, content)]);

        var response = await HttpClient.CompletionsAsync(ApiUrl + "/chat/completions", request);

        if (withHistory)
        {
            MessageHistory.Add(request.Messages.FirstOrDefault());
            MessageHistory.Add(MessageContent.Create(Role.Assistant, response.Data.Choices.FirstOrDefault()?.Message?.Content));
        }

        return response.Data;
    }
}
