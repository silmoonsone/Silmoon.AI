using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Models
{
    public class ToolCallParameter
    {
        public string FunctionName { get; set; }
        public JObject Parameters { get; set; }
        public string ToolCallId { get; set; }

        public static ToolCallParameter Create(string functionName, JObject parameters, string toolCallId)
        {
            {
                return new ToolCallParameter
                {
                    FunctionName = functionName,
                    Parameters = parameters,
                    ToolCallId = toolCallId
                };
            }
        }
        public static ToolCallParameter[] Create(List<ToolCall> toolCalls)
        {
            List<ToolCallParameter> results = [];
            foreach (var item in toolCalls)
            {
                results.Add(Create(item.Function?.Name, item.Function?.Arguments?.IsNullOrEmpty() ?? false ? [] : JsonConvert.DeserializeObject<JObject>(item.Function.Arguments), item.Id));
            }
            return [.. results];
        }
    }
}