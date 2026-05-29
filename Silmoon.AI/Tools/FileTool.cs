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
using System.IO;
using System.Reflection.Metadata;
using System.Text;

namespace Silmoon.AI.Tools
{
    public class FileTool : ExecuteTool
    {
        public const string FileFunctionName = "File_File";
        public const string ReadLinesFunctionName = "File_ReadLines";

        /// <summary>Underlying stream / <see cref="StreamReader"/> buffer size for line-scoped reads.</summary>
        const int ReadLinesStreamBufferSize = 65536;

        public override async Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => await CallTool(toolCallParameter, toolCallResult);
        public override Tool[] GetTools()
        {
            return [
                Tool.Create(FileFunctionName, """
                Whole-file UTF-8 text only (configs/logs/source—not binary). `read` loads file; `write` full replace (create parent dirs first).
                `read` → `Data` = file string; `write` → `Data` null. Returns `State`/`Message`/`Data`. Parallel unrelated paths only; same-file `write` then `read` must be ordered.
                """,
                [
                    new ToolParameterProperty("string", "action", "`read` | `write` (full replace).", ["write", "read"], true),
                    new ToolParameterProperty("string", "path", "Target file path.", null, true),
                    new ToolParameterProperty("string", "content", "Required. `write`: full new file body. `read`: unused—pass empty string.", null, true),
                ]),
                Tool.Create(ReadLinesFunctionName, """
                Read up to N UTF-8 lines (previews/logs/snippets; entire file → `File_File` `read`). Required `maxLines` ≥ 1.
                `direction` optional: `head` (default) = first N lines, `tail` = last N; shorter files → shorter `Data` array.
                `Data`: string[] (one line per item, no trailing newlines). Returns `State`/`Message`/`Data`. Parallel unrelated files only.
                """,
                [
                    new ToolParameterProperty("string", "path", "Target file path.", null, true),
                    new ToolParameterProperty("integer", "maxLines", "Line cap N (≥1); returns fewer if file is shorter.", null, true),
                    new ToolParameterProperty("string", "direction", "`head` (default) | `tail`. Omit = `head`.", ["head", "tail"], false),
                ])
            ];
        }


        public async Task<ToolCallResult> CallTool(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
        {
            ToolCallResult result = null;

            var functionName = toolCallParameter.FunctionName;
            var parameters = toolCallParameter.Parameters;

            switch (functionName)
            {
                case FileFunctionName:
                    try
                    {
                        await NotifyToolExecuting(functionName, toolCallParameter);
                        var fileSystemResult = ExecuteTool(parameters["action"].Value<string>(), parameters["path"].Value<string>(), parameters["content"]?.Value<string>());
                        result = ToolCallResult.Create(toolCallParameter, fileSystemResult);
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{FileFunctionName}] {ex.Message}"));
                    }
                    finally
                    {
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    break;
                case ReadLinesFunctionName:
                    try
                    {
                        await NotifyToolExecuting(functionName, toolCallParameter);
                        var readLinesResult = ReadLines(parameters["path"].Value<string>(), parameters["maxLines"], parameters["direction"]?.Value<string>() ?? "head");
                        result = ToolCallResult.Create(toolCallParameter, readLinesResult);
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{FileFunctionName}] {ex.Message}"));
                    }
                    finally
                    {
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    break;
                default:
                    break;
            }
            return result;
        }

        StateSet<bool, object> ExecuteTool(string action, string path, string content)
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
        StateSet<bool, object> WriteFile(string path, string content)
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
        StateSet<bool, object> ReadFile(string path)
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
        StateSet<bool, object> ReadLines(string path, JToken maxLinesToken, string direction)
        {
            if (maxLinesToken is null || maxLinesToken.Type == JTokenType.Null)
                return false.ToStateSet<object>(null, "maxLines is required.");
            int maxLines;
            try
            {
                maxLines = maxLinesToken.Type == JTokenType.Integer ? maxLinesToken.Value<int>() : (int)Math.Round(maxLinesToken.Value<double>());
            }
            catch
            {
                return false.ToStateSet<object>(null, "maxLines must be a number.");
            }
            if (maxLines < 1) return false.ToStateSet<object>(null, "maxLines must be >= 1.");

            if (string.IsNullOrWhiteSpace(direction)) direction = "head";
            else direction = direction.Trim();
            if (direction is not ("head" or "tail")) return false.ToStateSet<object>(null, "direction must be `head` or `tail` when provided.");

            try
            {
                if (!File.Exists(path)) return false.ToStateSet<object>(null, message: $"File not found: {path}");

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: ReadLinesStreamBufferSize, FileOptions.SequentialScan);
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: ReadLinesStreamBufferSize);
                string[] lines;
                if (direction == "tail")
                    lines = ReadTailLinesStreaming(reader, maxLines);
                else
                    lines = ReadHeadLinesStreaming(reader, maxLines);

                return true.ToStateSet<object>(lines);
            }
            catch (Exception e)
            {
                return false.ToStateSet<object>(null, message: $"Error reading file: {e.Message}");
            }
        }

        /// <summary>
        /// Reads until a line terminator, using \r\n first, then lone \n, then lone \r (Windows / Unix / classic Mac).
        /// Does not include terminators in the returned string. Returns <c>null</c> only when at EOF with no line content read.
        /// </summary>
        string? ReadLineCrossPlatform(StreamReader reader)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int ch = reader.Read();
                if (ch < 0) return sb.Length > 0 ? sb.ToString() : null;

                if (ch == '\r')
                {
                    if (reader.Peek() == '\n') reader.Read();
                    return sb.ToString();
                }

                if (ch == '\n') return sb.ToString();

                sb.Append((char)ch);
            }
        }

        string[] ReadHeadLinesStreaming(StreamReader reader, int maxLines)
        {
            var list = new List<string>(Math.Min(maxLines, 64));
            for (int i = 0; i < maxLines; i++)
            {
                var line = ReadLineCrossPlatform(reader);
                if (line is null) break;
                list.Add(line);
            }
            return [.. list];
        }

        string[] ReadTailLinesStreaming(StreamReader reader, int maxLines)
        {
            var window = new Queue<string>(maxLines + 1);
            string? line;
            while ((line = ReadLineCrossPlatform(reader)) != null)
            {
                window.Enqueue(line);
                if (window.Count > maxLines) window.Dequeue();
            }
            return [.. window];
        }

        //public static StateSet<bool, string> DeleteFile(string path)
        //{
        //    File.Delete(path);
        //    return true.ToStateSet<string>(null);
        //}
    }
}