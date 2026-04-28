using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
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
        public INativeChatClient NativeChatClient { get; set; }
        public DeepThinkTool(INativeChatClient nativeChatClient)
        {
            NativeChatClient = nativeChatClient;
        }
        public override Tool[] GetTools()
        {
            return [
                Tool.Create("ask", """
                Delegate hard tasks to a stronger model and return its reply.
                Use for: deep reasoning, code review, security analysis, architecture/design, long-form analysis.
                Skip for simple questions the current model can answer directly.
                Concurrency: singleton, serial only; never run multiple `ask` calls in parallel.
                `system` is optional: empty keeps default delegation prompt; non-empty overrides it for this call.
                """,
                [
                    new ToolParameterProperty("string", "system", "Optional. Role, format, language. Omit to keep default."),
                    new ToolParameterProperty("string", "content", "Task: goal, constraints, input, desired output shape."),
                    //new ToolParameterProperty("bool", "reasonContent", "Enable thinking and reasoning, default is false.", [true, false]),
                ]),
            ];
        }
        public override async Task<List<ToolCallResult>> OnToolCallInvoke(ToolCallParameter[] toolCallParameters, ConcurrentDictionary<string, ToolCallResult> toolCallResults)
        {
            List<ToolCallResult> results = [];

            foreach (var parameter in toolCallParameters)
            {
                var functionName = parameter.FunctionName;
                var parameters = parameter.Parameters;

                if (functionName == "ask")
                {
                    string system = parameters["system"]?.ToString();
                    string content = parameters["content"].ToString();
                    //bool reasonContent = parameters["reasonContent"]?.Value<bool>() ?? false;

                    if (system is not null) NativeChatClient.SystemPrompt = system;

                    List<Chunk> chunks = [];
                    Console.WriteLineWithColor("Agent response start:", ConsoleColor.Green, ConsoleColor.Blue);
                    await foreach (var chunk in NativeChatClient.CompletionsStreamAsync([MessageContent.Create(Role.User, content)], chunks))
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
                    var result = Result.Create([.. chunks]);

                    results.Add(ToolCallResult.Create(parameter, true.ToStateSet<string>(result.ToJsonString())));
                    break;
                }
            }

            return results;
        }
    }
}
