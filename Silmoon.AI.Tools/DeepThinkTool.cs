using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Interfaces;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
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

            Tools = GetTools();
        }
        public Tool[] GetTools()
        {
            return [Tool.Create("ask", """
                Delegate a **hard** task to the stronger model (same client stack); returns its reply. **Use:** hard reasoning, review, security, design, long analysis. **Skip:** trivial Q&A the main model handles (cost/latency).

                Empty `system` = don’t override delegation prompt; if set, overrides system for this call only (host behavior).
                """, [
                new ToolParameterProperty("string", "content", "Task: goal, constraints, input, desired output shape."),
                new ToolParameterProperty("string", "system", "Optional. Role, format, language. Omit to keep default.")
                ])];
        }
        public override async Task<StateSet<bool, MessageContent>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState)
        {
            if (functionName == "ask")
            {
                string content = parameters["content"].ToString();
                string system = parameters["system"]?.ToString();

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

                return true.ToStateSet(MessageContent.Create(Role.Tool, result.ToJsonString(), toolCallId));
            }
            else
                return null;
        }
    }
}
