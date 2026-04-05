using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Client.ToolCall
{
    public class LocalFileSystemTool
    {
        public static StateSet<bool, object> ExecuteTool(string action, string path, string content)
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
        public static StateSet<bool, object> WriteFile(string path, string content)
        {
            File.WriteAllText(path, content);
            return true.ToStateSet<object>(data: null);
        }
        public static StateSet<bool, object> ReadFile(string path)
        {
            string content = File.ReadAllText(path);
            return true.ToStateSet<object>(data: content);
        }
        //public static StateSet<bool, object> DeleteFile(string path)
        //{
        //    File.Delete(path);
        //    return true.ToStateSet<object>(data: null);
        //}
    }
}