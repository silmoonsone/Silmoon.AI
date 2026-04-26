using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
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
    public class FileTool : ExecuteTool
    {
        public override async Task<List<ToolCallResult>> OnToolCallInvoke(ToolCallParameter[] toolCallParameters, ConcurrentDictionary<string, ToolCallResult> toolCallResults) => await CallTool(toolCallParameters, toolCallResults);
        public override Tool[] GetTools()
        {
            return [
                Tool.Create("FileTool", """
                UTF-8 **text** read/write whole file. Prefer over huge terminal pipes for configs/logs/code. **Not:** binary; unconfirmed sensitive paths; shell-specific behavior → command tools.

                Response: JSON result; **read** returns file text in payload; **write** replaces entire file (create parents first).
                """,
                [
                    new ToolParameterProperty("string", "action", "`read` | `write` (full replace).", ["write", "read"], true),
                    new ToolParameterProperty("string", "path", "File path (parents must exist for write).", null, true),
                    new ToolParameterProperty("string", "content", "Write: full text. Read: ignored.", null, true),
                ]),
            ];
        }


        public static Task<List<ToolCallResult>> CallTool(ToolCallParameter[] toolCallParameters, ConcurrentDictionary<string, ToolCallResult> toolCallResults)
        {
            List<ToolCallResult> results = [];

            foreach (var parameter in toolCallParameters)
            {
                var functionName = parameter.FunctionName;
                var parameters = parameter.Parameters;

                switch (functionName)
                {
                    case "FileTool":
                        var fileSystemResult = ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"]?.Value<string>());
                        results.Add(ToolCallResult.Create(parameter, true.ToStateSet<string>(fileSystemResult.ToJsonString())));
                        break;
                    default:
                        break;
                }
            }
            return Task.FromResult(results);
        }

        static StateSet<bool, string> ExecuteTool(string action, string path, string content)
        {
            switch (action)
            {
                case "write":
                    return WriteFile(path, content);
                case "read":
                    return ReadFile(path);
                //case "delete":
                //    return DeleteFile(path);
                default:
                    return false.ToStateSet<string>(null, $"Unsupported action: {action}");
            }
        }
        static StateSet<bool, string> WriteFile(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
                return true.ToStateSet<string>(null);
            }
            catch (Exception e)
            {
                return false.ToStateSet<string>(null, message: $"Error writing file: {e.Message}");
            }
        }
        static StateSet<bool, string> ReadFile(string path)
        {
            try
            {

                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    return true.ToStateSet<string>(content);
                }
                else return false.ToStateSet<string>(null, message: $"File not found: {path}");
            }
            catch (Exception e)
            {
                return false.ToStateSet<string>(null, message: $"Error reading file: {e.Message}");
            }
        }
        //public static StateSet<bool, string> DeleteFile(string path)
        //{
        //    File.Delete(path);
        //    return true.ToStateSet<string>(null);
        //}
    }
}