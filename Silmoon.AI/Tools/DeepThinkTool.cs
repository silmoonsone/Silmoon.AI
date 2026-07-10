using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Tools
{
    public class DeepThinkTool : ExecuteTool
    {
        public const string CallAgentFunctionName = "DeepThink_Call";

        public INativeClient NativeClient { get; set; }
        public DeepThinkTool(INativeClient nativeClient)
        {
            NativeClient = nativeClient;
        }
        public override Tool[] GetTools()
        {
            return [
                Tool.Create(CallAgentFunctionName, $"""
                Delegate hard tasks to a stronger model and return its reply.
                Use for: deep reasoning, code review, security analysis, architecture/design, long-form analysis.
                Skip for simple questions the current model can answer directly.
                Concurrency: singleton, serial only; never run multiple `{CallAgentFunctionName}` calls in parallel.
                `system` is optional: empty keeps default delegation prompt; non-empty overrides it for this call.
                Return JSON object with `State`, `Message`, `Data` (`Data` is delegated model result JSON string).
                """,
                [
                    new ToolParameterProperty("string", "system", "Optional. Role, format, language. Omit to keep default."),
                    new ToolParameterProperty("string", "content", "Task: goal, constraints, input, desired output shape."),
                    //new ToolParameterProperty("bool", "reasonContent", "Enable thinking and reasoning, default is false.", [true, false]),
                ]),
            ];
        }
        public override async Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
        {
            ToolCallResult result = null;

            var functionName = toolCallParameter.FunctionName;
            var parameters = toolCallParameter.Parameters;

            if (functionName == CallAgentFunctionName)
            {
                await NotifyToolExecuting(functionName, toolCallParameter);
                string system = parameters["system"]?.ToString();
                string content = parameters["content"].ToString();
                //bool reasonContent = parameters["reasonContent"]?.Value<bool>() ?? false;

                if (system is not null) NativeClient.SystemPrompt = system;

                List<ChatCompletionsChunk> chunks = [];
                Console.WriteLineWithColor("Agent response start:", ConsoleColor.Green, ConsoleColor.Blue);
                await foreach (var chunk in NativeClient.CompletionsStreamAsync(new List<IMessage> { MessageContent.Create(Role.User, content) }, chunks))
                {
                    if (chunk.State)
                    {
                        chunk.Data.Choices.Each(x =>
                        {
                            if (x.Delta?.ToolCalls is not null) Console.Write(".");
                            else
                            {
                                Console.WriteWithColor(x?.Delta?.GetThinking(), ConsoleColor.DarkGray);
                                Console.WriteWithColor(x?.Delta?.Content, ConsoleColor.White);
                            }
                        });
                    }
                    else Console.WriteLineWithColor(chunk.Message);
                }
                Console.WriteLine();
                Console.WriteLineWithColor("Agent response end:", ConsoleColor.Green, ConsoleColor.Blue);
                var askResult = Result.Create([.. chunks]);
                result = ToolCallResult.Create(toolCallParameter, true.ToStateSet((object)askResult));
                await NotifyToolExecuted(functionName, toolCallParameter, result);
            }

            return result;
        }
    }
}

