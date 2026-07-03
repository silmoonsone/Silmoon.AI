using System;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Silmoon.AI.OpenAI.Models.Enums;

[JsonConverter(typeof(StringEnumConverter))]
public enum Role
{
    [EnumMember(Value = "system")]
    [Display(Name = "系统")]
    System,
    [EnumMember(Value = "user")]
    [Display(Name = "用户")]
    User,
    [EnumMember(Value = "assistant")]
    [Display(Name = "助手")]
    Assistant,
    [EnumMember(Value = "tool")]
    [Display(Name = "工具")]
    Tool,
}

