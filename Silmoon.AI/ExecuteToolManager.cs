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
        INativeChatClient NativeChatClient { get; set; }
        public ExecuteToolManager(INativeChatClient nativeChatClient)
        {
            NativeChatClient = nativeChatClient;
        }
        public StateSet<bool, IExecuteTool> AddExecuteTool(IExecuteTool tool)
        {
            foreach (var item in tool.Tools)
            {
                var existsFunctionTool = Tools.Where(x => x.Tools.Any(y => y.Function == item.Function));
                if (existsFunctionTool.Any()) return false.ToStateSet(existsFunctionTool.FirstOrDefault(), "此Tool Function已存在。");
            }
            tool.InjectToolCall(NativeChatClient);
            Tools.Add(tool);
            return true.ToStateSet(tool);
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

