using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI
{
    public class ExecuteToolManager
    {
        public event ToolCallsStartHandler OnToolCallsStart;
        public event ToolCallInvokeHandler OnToolCallInvoke;
        public event ToolCallsFinishHandler OnToolCallsFinish;

        public event ToolExecutingHandler OnToolExecuting;
        public event ToolExecutedHandler OnToolExecuted;
        public List<IExecuteTool> Tools { get; private set; } = [];
        INativeClient NativeClient { get; set; }
        public ExecuteToolManager(INativeClient nativeClient)
        {
            NativeClient = nativeClient;
        }
        public StateSet<bool> AddExecuteTool(IExecuteTool tool)
        {
            foreach (var item in tool.Tools)
            {
                var existsTool = Tools.SelectMany(x => x.Tools).Where(y => string.Equals(y.Function?.Name, item.Function?.Name, StringComparison.Ordinal));
                if (existsTool.Any()) return false.ToStateSet($"此{existsTool.FirstOrDefault().GetType().Name}已存在。");
            }
            var result = tool.InjectToolCall(NativeClient);
            if (result.State) Tools.Add(tool);
            return result;
        }
        internal Task onToolCallExecuting(string functionName, ToolCallParameter toolCallParameter) => OnToolExecuting is null ? Task.CompletedTask : OnToolExecuting(functionName, toolCallParameter);
        internal Task onToolCallExecuted(string functionName, ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => OnToolExecuted is null ? Task.CompletedTask : OnToolExecuted(functionName, toolCallParameter, toolCallResult);
        public void AddExecuteTools(IExecuteTool[] tools) => tools.Each(x => AddExecuteTool(x));
        public async Task<ToolCallResult[]> ToolCalls(ToolCallParameter[] toolCallParameters)
        {
            List<Task<ToolCallResult>> toolCallTasks = [];
            OnToolCallsStart?.Invoke(toolCallParameters);
            foreach (var toolCallParameter in toolCallParameters)
            {
                toolCallTasks.Add(Task.Run(async () =>
                {
                    ToolCallResult result = null;
                    try
                    {
                        List<Task> handlerTasks = [];
                        foreach (ToolCallInvokeHandler handler in OnToolCallInvoke.GetInvocationList().Cast<ToolCallInvokeHandler>())
                        {
                            handlerTasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    var tmpResult = await handler(toolCallParameter, result);
                                    if (tmpResult is not null) result = tmpResult;
                                }
                                catch (Exception ex)
                                {
                                    result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"执行工具调用处理程序发生异常: {ex}"));
                                }
                            }));
                        }
                        await Task.WhenAll([.. handlerTasks]);
                        result ??= ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"function {toolCallParameter.FunctionName} not implemented."));
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(null, false.ToStateSet<object>(null, $"执行工具调用发生异常: {ex.Message}"));
                    }
                    return result;
                }));
            }
            var results = await Task.WhenAll(toolCallTasks);
            results = OnToolCallsFinish is null ? results : await OnToolCallsFinish(toolCallParameters, results);
            return results;
        }
    }
}

