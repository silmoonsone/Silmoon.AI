using Newtonsoft.Json;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Extensions;
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Silmoon.AI.Tools
{
    public class WorldStateTool : ToolSet
    {
        public const string WorldStateFunctionName = "Sys_WorldState";

        public override Tool[] GetTools()
        {
            return [
                Tool.Create(WorldStateFunctionName, """
                Return current external world state snapshot for decision making.
                Includes time, timezone, machine and runtime information.
                Stateless read-only tool; no side effects.
                Preferred source for "current time now" requests; combine with `Wait_Delay` for periodic serial sampling.
                Return JSON object with `State`, `Message`, `Data` (`Data` is world-state JSON string).
                """, []),
            ];
        }

        public override async Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
        {
            ToolCallResult result = null;
            var functionName = toolCallParameter.FunctionName;

            if (functionName == WorldStateFunctionName)
            {
                await NotifyToolExecuting(functionName, toolCallParameter);
                try
                {
                    var utcNow = DateTimeOffset.UtcNow;
                    var localNow = DateTimeOffset.Now;
                    var localDate = localNow.Date;
                    var zone = TimeZoneInfo.Local;
                    var isoWeek = ISOWeek.GetWeekOfYear(localDate);
                    var isoWeekYear = ISOWeek.GetYear(localDate);
                    var quarter = ((localDate.Month - 1) / 3) + 1;
                    var isWeekend = localDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    var daysInMonth = DateTime.DaysInMonth(localDate.Year, localDate.Month);

                    var payload = JsonConvert.SerializeObject(new
                    {
                        time = new
                        {
                            utcIso = utcNow.ToString("O"),
                            localIso = localNow.ToString("O"),
                            unixMilliseconds = utcNow.ToUnixTimeMilliseconds(),
                            unixSeconds = utcNow.ToUnixTimeSeconds(),
                        },
                        calendar = new
                        {
                            localDate = localDate.ToString("yyyy-MM-dd"),
                            year = localDate.Year,
                            month = localDate.Month,
                            day = localDate.Day,
                            dayOfWeek = localDate.DayOfWeek.ToString(),
                            dayOfWeekIso = localDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)localDate.DayOfWeek,
                            dayOfYear = localDate.DayOfYear,
                            isoWeekOfYear = isoWeek,
                            isoWeekYear = isoWeekYear,
                            weekOfMonth = ((localDate.Day - 1) / 7) + 1,
                            quarter = quarter,
                            daysInMonth = daysInMonth,
                            isLeapYear = DateTime.IsLeapYear(localDate.Year),
                            isWeekend = isWeekend,
                        },
                        timezone = new
                        {
                            id = zone.Id,
                            displayName = zone.DisplayName,
                            baseUtcOffsetMinutes = (int)zone.BaseUtcOffset.TotalMinutes,
                            currentUtcOffsetMinutes = (int)localNow.Offset.TotalMinutes,
                        },
                        machine = new
                        {
                            machineName = Environment.MachineName,
                            userName = Environment.UserName,
                            domainName = Environment.UserDomainName,
                            osDescription = RuntimeInformation.OSDescription,
                            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                            frameworkDescription = RuntimeInformation.FrameworkDescription,
                            is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                            is64BitProcess = Environment.Is64BitProcess,
                            processorCount = Environment.ProcessorCount,
                        },
                        process = new
                        {
                            currentDirectory = Environment.CurrentDirectory,
                        },
                    });

                    result = ToolCallResult.Create(toolCallParameter, true.ToStateSet<object>(payload));
                }
                catch (Exception ex)
                {
                    result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{WorldStateFunctionName}] {ex.Message}"));
                }

                await NotifyToolExecuted(functionName, toolCallParameter, result);
            }

            return result;
        }
    }
}

