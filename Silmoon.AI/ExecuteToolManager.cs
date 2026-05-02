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
        public async Task<List<ToolCallResult>> ToolCalls(ToolCallParameter[] toolCallParameters, ToolCallStartHandler toolCallStartHandler, ToolCallCompletedHandler toolCallCompletedHandler)
        {
            try
            {
                ConcurrentDictionary<string, ToolCallResult> results = [];
                List<Task> handlerTasks = [];
                foreach (ToolCallStartHandler handler in toolCallStartHandler.GetInvocationList().Cast<ToolCallStartHandler>())
                {
                    handlerTasks.Add(Task.Run(async () =>
                    {
                        var toolCallResults = await handler(toolCallParameters, results);
                        if (toolCallResults is not null)
                        {
                            foreach (var item in toolCallResults)
                            {
                                results[item.Parameter.ToolCallId] = item;
                            }
                        }
                    }));
                }
                await Task.WhenAll([.. handlerTasks]);

                foreach (var item in toolCallParameters)
                {
                    if (!results.ContainsKey(item.ToolCallId))
                    {
                        results[item.ToolCallId] = ToolCallResult.Create(item, false.ToStateSet<string>(null, $"function {item.FunctionName} not implemented."));
                    }
                }
                results = await (toolCallCompletedHandler?.Invoke(results) ?? Task.FromResult(results));
                return [.. results.Values];
            }
            catch (Exception ex)
            {
                return [ToolCallResult.Create(null, false.ToStateSet<string>(null, $"执行工具调用发生异常: {ex.Message}"))];
            }
        }
    }
}
