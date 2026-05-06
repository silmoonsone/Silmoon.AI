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
using System.Reflection.Metadata;
using System.Text;

namespace Silmoon.AI.Tools
{
    public class FileTool : ExecuteTool
    {
        public const string FileFunctionName = "File_File";

        public override async Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => await CallTool(toolCallParameter, toolCallResult);
        public override Tool[] GetTools()
        {
            return [
                Tool.Create(FileFunctionName, """
                UTF-8 text file read/write (whole-file).
                Prefer for configs/logs/code text; do not use for binary or shell-dependent behavior.
                Concurrency: parallel is allowed for independent files/operations.
                Ordered dependency must be serial (e.g., `write same file -> read verify`); do not parallelize dependent steps.
                When write, return Data is null. When read, Data is file content.
                Return JSON object with `State`, `Message`, `Data`. `Data` is a JSON string: `read` includes file content; `write` replaces entire file (parent directories must exist).
                """,
                [
                    new ToolParameterProperty("string", "action", "`read` | `write` (full replace).", ["write", "read"], true),
                    new ToolParameterProperty("string", "path", "File path (parents must exist for write).", null, true),
                    new ToolParameterProperty("string", "content", "Write: full text. Read: ignored.", null, true),
                ]),
            ];
        }


        public static Task<ToolCallResult> CallTool(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
        {
            ToolCallResult result = null;

            var functionName = toolCallParameter.FunctionName;
            var parameters = toolCallParameter.Parameters;

            switch (functionName)
            {
                case FileFunctionName:
                    var fileSystemResult = ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"]?.Value<string>());
                    result = ToolCallResult.Create(toolCallParameter, fileSystemResult);
                    break;
                default:
                    break;
            }
            return Task.FromResult(result);
        }

        static StateSet<bool, object> ExecuteTool(string action, string path, string content)
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
                    return false.ToStateSet<object>(null, $"Unsupported action: {action}");
            }
        }
        static StateSet<bool, object> WriteFile(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
                return true.ToStateSet<object>(null, "File written successfully.");
            }
            catch (Exception e)
            {
                return false.ToStateSet<object>(null, message: $"Error writing file: {e.Message}");
            }
        }
        static StateSet<bool, object> ReadFile(string path)
        {
            try
            {

                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    return true.ToStateSet<object>(content);
                }
                else return false.ToStateSet<object>(null, message: $"File not found: {path}");
            }
            catch (Exception e)
            {
                return false.ToStateSet<object>(null, message: $"Error reading file: {e.Message}");
            }
        }
        //public static StateSet<bool, string> DeleteFile(string path)
        //{
        //    File.Delete(path);
        //    return true.ToStateSet<string>(null);
        //}
    }
}