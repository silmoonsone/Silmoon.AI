using Silmoon.AI.Handlers;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Interfaces;
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
        public void AddExecuteTools(IExecuteTool[] tools) => tools.Each(x => AddExecuteTool(x));
        public async Task<ToolCallResult[]> ToolCalls(ToolCallParameter[] toolCallParameters, ToolCallStartHandler toolCallStartHandler, ToolCallCompletedHandler toolCallCompletedHandler)
        {
            List<Task<ToolCallResult>> toolCallTasks = [];
            foreach (var toolCallParameter in toolCallParameters)
            {
                toolCallTasks.Add(Task.Run(async () =>
                {
                    ToolCallResult result = null;
                    try
                    {
                        List<Task> handlerTasks = [];
                        foreach (ToolCallStartHandler handler in toolCallStartHandler.GetInvocationList().Cast<ToolCallStartHandler>())
                        {
                            handlerTasks.Add(Task.Run(async () => { result = await handler(toolCallParameter, result); }));
                        }
                        await Task.WhenAll([.. handlerTasks]);
                        result ??= ToolCallResult.Create(toolCallParameter, false.ToStateSet<string>(null, $"function {toolCallParameter.FunctionName} not implemented."));
                        result = await (toolCallCompletedHandler?.Invoke(result) ?? Task.FromResult(result));
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(null, false.ToStateSet<string>(null, $"执行工具调用发生异常: {ex.Message}"));
                    }
                    return result;
                }));
            }
            var results = await Task.WhenAll(toolCallTasks);
            return results;
        }
    }
}
