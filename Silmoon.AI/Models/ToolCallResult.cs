using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Models
{
    public class ToolCallResult
    {
        public ToolCallParameter Parameter { get; set; }
        public StateSet<bool, string> Result { get; set; }

        public static ToolCallResult Create(ToolCallParameter parameter, StateSet<bool, string> result)
        {
            return new ToolCallResult
            {
                Parameter = parameter,
                Result = result
            };
        }
    }
}
