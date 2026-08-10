using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.Models;
using Silmoon.Threading;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI
{
    public abstract class NativeClient : INativeClient
    {
        public event ToolCallsStartHandler OnToolCallsStart;
        public event ToolCallInvokeHandler OnToolCallInvoke;
        public event ToolExecutingHandler OnToolExecuting;
        public event ToolExecutedHandler OnToolExecuted;
        public event ToolCallsFinishHandler OnToolCallsFinish;
        public event StreamOutputHandler OnStreamOutput;
        public event StreamOutputCompletedHandler OnStreamOutputCompleted;
        public ModelProvider ModelProvider { get; set; }
        public string ModelName { get; set; }

        public ToolSetManager ToolSetManager { get; set; }

        public bool EnableThinking { get; set; } = false;
        public double? Temperature { get; set; } = RequestBase.DefaultTemperature;
        public double? TopP { get; set; } = RequestBase.DefaultTopP;
        public NativeMessageCollection MessageHistory { get; set; } = [];

        public string SystemPrompt
        {
            set
            {
                var systemMessage = MessageHistory.FirstOrDefault(m => m.Role == Role.System) as NativeMessageContent;
                if (value is null)
                {
                    if (systemMessage is not null) MessageHistory.Remove(systemMessage);
                }
                else
                {
                    if (systemMessage is null) MessageHistory.Insert(0, NativeMessageContent.Create(Role.System, value));
                    else systemMessage.Content = value;
                }
            }
            get => (MessageHistory.FirstOrDefault(m => m.Role == Role.System) as NativeMessageContent)?.Content;
        }
        public List<Tool> Tools { get; set; } = [];
        public ManualResetEvent BusyResetEvent { get; private set; } = new ManualResetEvent(true);
        public AsyncLock BusyAsyncLock { get; private set; } = new AsyncLock();

        public abstract void ClearHistory(string? continuation = null);
        public abstract void RollbackHistory(uint rounds = 1);
        public abstract void RebuildHttpClient();
        public virtual StateSet<bool> AddToolSet(IToolSet toolSet) => ToolSetManager.AddToolSet(toolSet);
        public virtual void AddToolSets(IToolSet[] toolSets) => ToolSetManager.AddToolSets(toolSets);

        public abstract IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(string content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
        public abstract IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(INativeMessage content, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
        public abstract IAsyncEnumerable<StateSet<bool, ChatCompletionsChunk>> CompletionsStreamAsync(NativeMessageCollection messageHistory, List<ChatCompletionsChunk> chunks = null, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");

        public abstract Task<ChatCompletionsResponse> CompletionsAsync(string content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
        public abstract Task<ChatCompletionsResponse> CompletionsAsync(INativeMessage content, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");
        public abstract Task<ChatCompletionsResponse> CompletionsAsync(NativeMessageCollection messageHistory, List<Tool> tools = null, string model = null, string completionsUrl = "/chat/completions");

        internal virtual Task onToolCallsStart(ToolCallParameter[] toolCallParameters) => OnToolCallsStart is null ? Task.CompletedTask : OnToolCallsStart.Invoke(toolCallParameters);
        internal virtual Task<ToolCallResult> onToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => OnToolCallInvoke is null ? Task.FromResult(toolCallResult) : OnToolCallInvoke.Invoke(toolCallParameter, toolCallResult);
        internal virtual Task onToolCallExecuting(string functionName, ToolCallParameter toolCallParameter) => OnToolExecuting is null ? Task.CompletedTask : OnToolExecuting.Invoke(functionName, toolCallParameter);
        internal virtual Task onToolCallExecuted(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => OnToolExecuted is null ? Task.CompletedTask : OnToolExecuted.Invoke(functionName, toolCallParameter, toolCallResult);
        internal virtual Task<ToolCallResult[]> onToolCallsFinish(ToolCallParameter[] toolCallParameters, ToolCallResult[] toolCallResults) => OnToolCallsFinish is null ? Task.FromResult(toolCallResults) : OnToolCallsFinish.Invoke(toolCallParameters, toolCallResults);
        internal virtual Task onStreamOutput(StateSet<bool, ChatCompletionsChunk> chunkState) => OnStreamOutput is null ? Task.CompletedTask : OnStreamOutput.Invoke(chunkState);
        internal virtual Task onStreamOutputCompleted(Result result) => OnStreamOutputCompleted is null ? Task.CompletedTask : OnStreamOutputCompleted.Invoke(result);

        public virtual void Dispose()
        {
            OnToolCallsStart = null;
            OnToolCallInvoke = null;
            OnToolCallsFinish = null;
            OnToolExecuting = null;
            OnToolExecuted = null;
            OnStreamOutput = null;
            OnStreamOutputCompleted = null;

            Tools.Clear();
            Tools = null;
            MessageHistory.Clear();
            MessageHistory = null;
        }
    }
}
