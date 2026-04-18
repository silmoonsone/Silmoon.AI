using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
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
    public class FileTool : ExecuteTool
    {
        public FileTool() => Tools = GetTools();
        public override async Task<StateSet<bool, MessageContent>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState) => await CallTool(functionName, parameters, toolCallId);
        public static Tool[] GetTools()
        {
            return [
                Tool.Create("FileTool", "It provides the ability to read and write text files, serving as an alternative when writing and reading large amounts of text using methods similar to command lines or terminals. This tool is not essential; it is simply used to reduce the probability of operations when manipulating large amounts of text files. It returns a JSON object, where Data is the text content.",
                [
                    new ToolParameterProperty("string", "The action to perform on the file system.", ["write", "read"], "action", true),
                    new ToolParameterProperty("string", "The path of the file to operate on.", null, "path", true),
                    new ToolParameterProperty("string", "This parameter is ignored when reading; it can be replaced with null.", null, "content", true),
                ]),
            ];
        }


        public static Task<StateSet<bool, MessageContent>> CallTool(string functionName, JObject parameters, string toolCallId)
        {
            StateSet<bool, MessageContent> result = null;

            switch (functionName)
            {
                case "FileTool":
                    var fileSystemResult = ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"].Value<string>());
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, fileSystemResult.ToJsonString(), toolCallId));
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
                    return false.ToStateSet<object>(data: $"Unsupported action: {action}");
            }
        }
        static StateSet<bool, object> WriteFile(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
                return true.ToStateSet<object>(data: null);
            }
            catch (Exception e)
            {
                return false.ToStateSet<object>(data: null, message: $"Error writing file: {e.Message}");
            }
        }
        static StateSet<bool, object> ReadFile(string path)
        {
            try
            {

                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    return true.ToStateSet<object>(data: content);
                }
                else return false.ToStateSet<object>(data: null, message: $"File not found: {path}");
            }
            catch (Exception e)
            {
                return false.ToStateSet<object>(data: null, message: $"Error reading file: {e.Message}");
            }
        }
        //public static StateSet<bool, object> DeleteFile(string path)
        //{
        //    File.Delete(path);
        //    return true.ToStateSet<object>(data: null);
        //}
    }
}