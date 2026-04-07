using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI.Enums;
using Silmoon.AI.Client.OpenAI.Interfaces;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.AI.Client.ToolCall;
using Silmoon.Extensions;
using Silmoon.Models;
using System.Threading.Channels;

namespace Silmoon.AI.Client.OpenAI;

public class NativeChatClient : INativeApiClient
{
    public event ToolCallInvokeHandler OnToolCallInvoke;
    public string ApiUrl { get; set; }
    public string ApiKey { get; set; }
    public string Model { get; set; }
    SseHttpClient HttpClient { get; set; }
    public bool EnableThinking { get; set; } = false;
    public bool EnableSearch { get; set; } = false;
    public List<MessageContent> MessageHistory { get; set; } = [];


    public NativeChatClient(string apiUrl, string apiKey, string model, string systemPrompt = null)
    {
        ApiUrl = apiUrl;
        ApiKey = apiKey;
        Model = model;
        SetSystemPrompt(systemPrompt);

        BuildHttpClient();
    }
    public void SetSystemPrompt(string systemPrompt)
    {
        if (systemPrompt is null) return;
        var systemMessage = MessageHistory.FirstOrDefault(m => m.Role == Role.System);
        if (systemMessage is null) MessageHistory.Insert(0, MessageContent.Create(Role.System, systemPrompt));
        else systemMessage.Content = systemPrompt;
    }

    void BuildHttpClient()
    {
        HttpClient?.Dispose();
        HttpClient = new SseHttpClient();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }
    public void ClearHistory()
    {
        var systemMessage = MessageHistory.FirstOrDefault(m => m.Role == Role.System);
        if (systemMessage is not null) MessageHistory = [systemMessage];
        else MessageHistory.Clear();
    }


    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string content, List<Chunk> chunks = null, string model = null, string completionsUrl = "/chat/completions")
    {
        await foreach (var chunk in CompletionsStreamAsync(MessageContent.Create(Role.User, content), chunks, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(MessageContent content, List<Chunk> chunks = null, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        await foreach (var chunk in CompletionsStreamAsync(MessageHistory, chunks, model, completionsUrl))
        {
            yield return chunk;
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(List<MessageContent> messageHistory, List<Chunk> chunks = null, string model = null, string completionsUrl = "/chat/completions")
    {
        chunks ??= [];
        while (true)
        {
            var request = new Request(model ?? Model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking);
            request.EnableSearch = EnableSearch;
            request.Tools = makeTools();


            Channel<StateSet<bool, Chunk>> channel = Channel.CreateUnbounded<StateSet<bool, Chunk>>();

            var callbackTask = HttpClient.CompletionsStreamAsync(ApiUrl + completionsUrl, request, async (chunkState) =>
            {
                try
                {
                    await channel.Writer.WriteAsync(chunkState);
                    if (chunkState.State && chunkState.Data is not null)
                    {
                        chunks.Add(chunkState.Data);
                        if (!chunkState.Data.Choices.IsNullOrEmpty() && !chunkState.Data.Choices.FirstOrDefault().FinishReason.IsNullOrEmpty()) channel.Writer.TryComplete();
                    }
                    else channel.Writer.TryComplete();
                }
                catch (Exception ex) { channel.Writer.TryComplete(ex); }
            });

            await foreach (var chunk in channel.Reader.ReadAllAsync())
            {
                yield return chunk;
            }

            var chunkStates = await callbackTask;
            if (chunkStates.State)
            {
                var result = Result.Create([.. chunkStates.Data]);
                if (result.FinishReason == "stop")
                {
                    messageHistory.Add(MessageContent.Create(Role.Assistant, result.Content));
                    break;
                }
                else if (result.FinishReason == "tool_calls")
                {
                    messageHistory.Add(MessageContent.Create(Role.Assistant, result.Content, [.. result.ToolCalls]));
                    if (!result.ToolCalls.IsNullOrEmpty())
                    {
                        string functionName = result.ToolCalls[0].Function.Name;
                        JObject parameters = result.ToolCalls[0].Function.Arguments.IsNullOrEmpty() ? [] : JsonConvert.DeserializeObject<JObject>(result.ToolCalls[0].Function.Arguments);
                        var toolCallResult = await ToolCall(functionName, parameters, result.ToolCalls[0].Id);
                        if (toolCallResult.State) messageHistory.Add(toolCallResult.Data);
                        else messageHistory.Add(MessageContent.Create(Role.Tool, toolCallResult.ToJsonString(), result.ToolCalls[0].Id));
                        continue;
                    }
                    else break;
                }
                else break;
            }
            else break;
        }
    }

    public async Task<Response> CompletionsAsync(string content, string model = null, string completionsUrl = "/chat/completions") => await CompletionsAsync(MessageContent.Create(Role.User, content), model, completionsUrl);
    public async Task<Response> CompletionsAsync(MessageContent content, string model = null, string completionsUrl = "/chat/completions")
    {
        MessageHistory.Add(content);
        return await CompletionsAsync(MessageHistory, model, completionsUrl);
    }
    public async Task<Response> CompletionsAsync(List<MessageContent> messageHistory, string model = null, string completionsUrl = "/chat/completions")
    {
        model ??= Model;
        while (true)
        {
            Request request = new Request(model, [.. messageHistory]);
            request.SetEnableThinking(EnableThinking);
            request.EnableSearch = EnableSearch;
            request.Tools = makeTools();

            var response = await HttpClient.CompletionsAsync(ApiUrl + completionsUrl, request);

            Choice firstChoice = response.Data.Choices.FirstOrDefault();
            if (firstChoice?.FinishReason == "stop")
            {
                messageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice?.Message?.Content));
                return response.Data;
            }
            else if (firstChoice?.FinishReason == "tool_calls")
            {
                messageHistory.Add(MessageContent.Create(Role.Assistant, firstChoice?.Message?.Content, [.. firstChoice?.Message?.ToolCalls]));
                if (!firstChoice?.Message?.ToolCalls.IsNullOrEmpty() ?? false)
                {
                    string functionName = firstChoice?.Message?.ToolCalls[0].Function.Name;
                    JObject parameters = firstChoice?.Message?.ToolCalls[0].Function.Arguments.IsNullOrEmpty() ?? false ? [] : JsonConvert.DeserializeObject<JObject>(firstChoice?.Message?.ToolCalls[0].Function.Arguments);
                    var toolCallResult = await ToolCall(functionName, parameters, firstChoice?.Message?.ToolCalls[0].Id);
                    if (toolCallResult.State) messageHistory.Add(toolCallResult.Data);
                    else messageHistory.Add(MessageContent.Create(Role.Tool, false.ToStateSet(toolCallResult.Message).ToJsonString(), firstChoice?.Message?.ToolCalls[0].Id));
                    continue;
                }
                return response.Data;
            }
        }
    }

    public async Task<StateSet<bool, MessageContent>> ToolCall(string functionName, JObject parameters, string toolCallId)
    {
        try
        {
            Console.WriteLine();
            Console.WriteLineWithColor($"[TOOL CALL] {functionName}", ConsoleColor.Yellow);
            var result = await (OnToolCallInvoke?.Invoke(functionName, parameters, toolCallId) ?? Task.FromResult<StateSet<bool, MessageContent>>(null));
            if (result is not null && result.State) return result;

            switch (functionName)
            {
                case "QuoteTool":
                    if (parameters["symbol"].Value<string>() == "XAUUSD") return true.ToStateSet(MessageContent.Create(Role.Tool, StateSet<bool, decimal>.Create(true, 4800m).ToJsonString(), toolCallId));
                    else result = false.ToStateSet<MessageContent>(null, $"产品符号 {parameters["symbol"].Value<string>()} 是错误的，我们接受的应该是国际通用的产品符号，并且没有斜杠分割（/），如果是大模型调用本函数，请尝试更正后自动再次发起查询，但是务必告知用户正确的符号。");
                    break;
                case "TradingController":
                    result = false.ToStateSet<MessageContent>(null, $"无法执行 {parameters["action"].Value<string>()} 操作，因为这是一个模拟调用。");
                    break;
                case "CommandTool":
                    var commandResult = CommandTool.Execute(parameters["os"].Value<string>(), parameters["command"].Value<string>(), parameters["terminalType"].Value<string>());
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, commandResult, toolCallId));
                    break;
                case "FileTool":
                    var fileSystemResult = LocalFileSystemTool.ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"].Value<string>());
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, fileSystemResult.ToJsonString(), toolCallId));
                    break;
                default:
                    result = false.ToStateSet<MessageContent>(null, $"函数 {functionName} 不存在。");
                    break;
            }
            Console.WriteLineWithColor($"[TOOL CALL RESULT] State: {result.State}, Message: {result.Data?.Content}", ConsoleColor.Yellow);
            return result;
        }
        catch (Exception ex)
        {
            return false.ToStateSet<MessageContent>(null, $"执行函数 {functionName} 时发生异常: {ex.Message}");
        }
    }

    private List<Tool> makeTools()
    {
        return [
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
                Function = new ToolFunction("CommandTool", "It can execute commands on the local computer and supports Windows, macOS, and Linux systems. Please note that you should not use this tool to execute dangerous commands, including power control, modifying or deleting important files and system files. Some high-risk operations require user confirmation before execution. Also note that CommandTool does not save context with each call; each command is independent. For example, calling `cd ...` will render the previous `cd` command's directory-changing behavior meaningless if called a second time.")
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
                Function = new ToolFunction("FileTool", "It provides the ability to read and write text files, serving as an alternative when writing and reading large amounts of text using methods similar to command lines or terminals. This tool is not essential; it is simply used to reduce the probability of operations when manipulating large amounts of text files. It returns a JSON object, where Data is the text content.")
                {
                    Parameters = new ToolParameters(){
                        Properties = new Dictionary<string, ToolParameterProperty>(){
                            { "action", new ToolParameterProperty("string", "The action to perform on the file system.", ["write", "read"]) },
                            { "path", new ToolParameterProperty("string", "The path of the file to operate on.") },
                            { "content", new ToolParameterProperty("string", "This parameter is ignored when reading; it can be replaced with null.") },
                        },
                        Required = ["action", "path", "content"],
                    }
                }
            },
        ];
    }
}

public delegate Task<StateSet<bool, MessageContent>> ToolCallInvokeHandler(string functionName, JObject parameters, string toolCallId);