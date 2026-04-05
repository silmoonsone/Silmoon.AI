using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Client.Models
{
    public class ToolFunction
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<ToolFunctionParameter> Parameters { get; set; }
    }
    public class ToolFunctionParameter
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; } = false;
        public List<object> Enum { get; set; }

        public static ToolFunctionParameter Create(string name, string type, string description, bool required = false, List<object> @enum = null)
        {
            return new ToolFunctionParameter
            {
                Name = name,
                Type = type,
                Description = description,
                Required = required,
                Enum = @enum
            };
        }
    }
}
