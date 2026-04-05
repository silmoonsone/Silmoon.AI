using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI.Enums;
using Silmoon.AI.Client.OpenAI.Interfaces;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.AI.Client.ToolCall;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.Client.OpenAI;

public class NativeChatClient : INativeApiClient
{
    public string ApiUrl { get; set; }
    public string ApiKey { get; set; }
    public string Model { get; set; }
    SseHttpClient HttpClient { get; set; }
    public List<MessageContent> MessageHistory { get; set; } = [];
    public string SystemPrompt { get; set; }
    public NativeChatClient(string apiUrl, string apiKey, string model, string systemPrompt = null)
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

    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, bool withHistory = true, List<Chunk> chunks = null, string model = null, string completionsUrl = "/chat/completions")
    {
        await foreach (var chunk in CompletionsStreamAsync(MessageContent.Create(Role.User, content), withHistory, chunks, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(MessageContent content, bool withHistory = true, List<Chunk> chunks = null, string model = null, string completionsUrl = "/chat/completions")
    {
        model ??= Model;
        send:
        Request request;
        if (withHistory) request = new Request(model, [.. MessageHistory, content]);
        else request = new Request(model, [content]);

        request.SetEnableThinking(false);
        request.EnableSearch = false;
        request.Tools = [
            new Tool("function"){
                Function = new ToolFunction("QuoteTool", "A tool to inquery quotes for symbol or product code.")
                {
                    Parameters = new ToolParameters(){
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            { "symbol", new ToolParameterProperty("string", "The symbol or product code to query quotes for.") },
                        },
                        Required = ["symbol"],
                    }
                }
            },
            new Tool("function"){
                Function = new ToolFunction("TradingController", "A tool to control trading client.")
                {
                    Parameters = new ToolParameters(){
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            { "action", new ToolParameterProperty("string", "The action to perform on the trading client.",["start", "stop", "pause", "resume"]) },
                        },
                        Required = ["action"],
                    }
                }
            },
            new Tool("function"){
                Function = new ToolFunction("CommandTool", "It can execute commands on the local computer and supports Windows, macOS, and Linux. Note that you should not use this tool to execute dangerous commands, including power control, modifying and deleting important files and system files. Some high-risk operations require prompting the user for confirmation before execution.")
                {
                    Parameters = new ToolParameters(){
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            { "os", new ToolParameterProperty("string", "The operating system on which to execute the command.", ["windows", "macos", "linux"]) },
                            { "command", new ToolParameterProperty("string", "The command to execute in cmd or powershell.") },
                            { "terminalType", new ToolParameterProperty("string", "Which command line should be used in Windows? What parameters are required in Windows? It supports cmd and PowerShell parameters; on other platforms, empty or null parameters are acceptable.", ["cmd", "powershell", null]) },
                        },
                        Required = ["os", "command", "terminalType"],
                    }
                }
            },
            new Tool("function"){
                Function = new ToolFunction("LocalFileSystemTool", "It provides some simple ways to manipulate files without using the command line, which is especially useful when writing large amounts of text and content. It also provides some other operations, but note that it only applies to file operations in user control and prohibits operating system files and system corruption.")
                {
                    Parameters = new ToolParameters(){
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            { "action", new ToolParameterProperty("string", "The action to perform on the file system.", ["write", "read"]) },
                            { "path", new ToolParameterProperty("string", "The path of the file to operate on.") },
                            { "content", new ToolParameterProperty("string", "The content to write to the file, if applicable.") },
                        },
                        Required = ["action", "path", "content"],
                    }
                }
            },
        ];

        if (withHistory) MessageHistory.Add(request.Messages.LastOrDefault());
        chunks ??= [];
        List<Chunk> OnceChunks = [];

        await foreach (var chunk in HttpClient.CompletionsStreamAsync(ApiUrl + completionsUrl, request))
        {
            if (chunk.State)
            {
                chunks.Add(chunk.Data);
                OnceChunks.Add(chunk.Data);
            }
            yield return chunk;
        }

        var result = Result.Create([.. OnceChunks]);

        if (withHistory)
        {
            if (result.FinishReason == "stop")
                MessageHistory.Add(MessageContent.Create(Role.Assistant, result.Content));
            else if (result.FinishReason == "tool_calls")
            {
                MessageHistory.Add(MessageContent.Create(Role.Assistant, result.Content, [.. result.ToolCalls]));

                string functionName = result.ToolCalls[0].Function.Name;
                JObject parameters = JsonConvert.DeserializeObject<JObject>(result.ToolCalls[0].Function.Arguments);
                var toolCallResult = await ToolCall(functionName, parameters, result.ToolCalls[0].Id);
                if (toolCallResult.State) content = toolCallResult.Data;
                else content = MessageContent.Create(Role.Tool, false.ToStateSet(toolCallResult.Message).ToJsonString(), result.ToolCalls[0].Id);
                goto send;
            }
        }
    }

    public async Task<Response> CompletionsAsync(string content, bool withHistory = true, string model = null)
    {
        return await CompletionsAsync(MessageContent.Create(Role.User, content), withHistory, model);
    }
    public async Task<Response> CompletionsAsync(MessageContent content, bool withHistory = true, string model = null)
    {
        model ??= Model;
        send:
        Request request;

        if (withHistory) request = new Request(model, [.. MessageHistory, content]);
        else request = new Request(model, [content]);

        request.SetEnableThinking(false);
        request.EnableSearch = false;
        request.Tools = [
            new Tool("function"){
                Function = new ToolFunction("QuoteTool", "A tool to inquery quotes for symbol or product code.")
                {
                    Parameters = new ToolParameters(){
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            {
                                "symbol", new ToolParameterProperty("string", "The symbol or product code to query quotes for.")
                            },
                        },
                        Required = ["symbol"],
                    }
                }
            },
        ];

        if (withHistory) MessageHistory.Add(request.Messages.LastOrDefault());

        var response = await HttpClient.CompletionsAsync(ApiUrl + "/chat/completions", request);

        if (withHistory)
        {
            Choice firstChoice = response.Data.Choices.FirstOrDefault();
            if (firstChoice?.FinishReason == "stop")
                MessageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice?.Message?.Content));
            else if (firstChoice?.FinishReason == "tool_calls")
            {
                MessageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice?.Message?.Content, [.. firstChoice?.Message?.ToolCalls]));

                string functionName = firstChoice?.Message?.ToolCalls[0].Function.Name;
                JObject parameters = JsonConvert.DeserializeObject<JObject>(firstChoice?.Message?.ToolCalls[0].Function.Arguments);
                var toolCallResult = await ToolCall(functionName, parameters, firstChoice?.Message?.ToolCalls[0].Id);
                if (toolCallResult.State) content = toolCallResult.Data;
                else content = MessageContent.Create(Role.Tool, false.ToStateSet(toolCallResult.Message).ToJsonString(), firstChoice?.Message?.ToolCalls[0].Id);
                goto send;
            }
        }


        return response.Data;
    }


    public async Task<StateSet<bool, MessageContent>> ToolCall(string functionName, JObject parameters, string toolCallId)
    {
        try
        {
            switch (functionName)
            {
                case "QuoteTool":
                    if (parameters["symbol"].Value<string>() == "XAUUSD")
                        return true.ToStateSet(MessageContent.Create(Role.Tool, StateSet<bool, decimal>.Create(true, 4800m).ToJsonString(), toolCallId));
                    else
                        return false.ToStateSet<MessageContent>(null, $"产品符号 {parameters["symbol"].Value<string>()} 是错误的，如果是大模型调用本函数，请尝试更正后自动再次发起查询，但是务必告知用户正确的符号。");
                case "TradingController":
                    return false.ToStateSet<MessageContent>(null, $"无法执行 {parameters["action"].Value<string>()} 操作，因为这是一个模拟调用。");
                case "CommandTool":
                    var commandResult = CommandTool.Execute(parameters["os"].Value<string>(), parameters["command"].Value<string>(), parameters["terminalType"].Value<string>());
                    return true.ToStateSet(MessageContent.Create(Role.Tool, commandResult, toolCallId));
                case "LocalFileSystemTool":
                    var fileSystemResult = LocalFileSystemTool.ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"].Value<string>());
                    return true.ToStateSet(MessageContent.Create(Role.Tool, fileSystemResult.ToJsonString(), toolCallId));
                default:
                    return false.ToStateSet<MessageContent>(null, $"函数 {functionName} 不存在。");
            }
        }
        catch (Exception ex)
        {
            return false.ToStateSet<MessageContent>(null, $"执行函数 {functionName} 时发生异常: {ex.Message}");
        }
    }
}
