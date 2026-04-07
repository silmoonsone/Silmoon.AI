using Silmoon.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Client.ToolCall
{
    public class CommandTool
    {
        public static string Execute(string os, string command, string terminalType)
        {
            switch (os)
            {
                case "windows":
                    if (terminalType == "cmd")
                        return ExecuteCmd(command);
                    else if (terminalType == "powershell")
                        return ExecutePowerShell(command);
                    else
                        throw new NotSupportedException($"Unsupported terminal type: {terminalType}");
                case "linux":
                    return LinuxExecute(command);
                case "macos":
                    return MacOSExecute(command);
                default:
                    throw new NotSupportedException($"Unsupported operating system: {os}");
            }
        }
        public static string ExecuteCmd(string command)
        {
            Console.WriteLineWithColor($"[CMD] {command}", ConsoleColor.Green);
            var processInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = System.Diagnostics.Process.Start(processInfo);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return string.IsNullOrEmpty(error) ? output : error;
        }
        public static string ExecutePowerShell(string command)
        {
            Console.WriteLineWithColor($"[PowerShell] {command}", ConsoleColor.Green);
            var processInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-Command {command}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = System.Diagnostics.Process.Start(processInfo);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return string.IsNullOrEmpty(error) ? output : error;
        }
        public static string MacOSExecute(string command)
        {
            Console.WriteLineWithColor($"[macOS] {command}", ConsoleColor.Green);
            var processInfo = new System.Diagnostics.ProcessStartInfo("/bin/bash", $"-c \"{command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = System.Diagnostics.Process.Start(processInfo);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return string.IsNullOrEmpty(error) ? output : error;
        }
        public static string LinuxExecute(string command)
        {
            Console.WriteLineWithColor($"[Linux] {command}", ConsoleColor.Green);
            var processInfo = new System.Diagnostics.ProcessStartInfo("/bin/bash", $"-c \"{command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = System.Diagnostics.Process.Start(processInfo);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return string.IsNullOrEmpty(error) ? output : error;
        }
    }
}