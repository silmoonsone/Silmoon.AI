using Newtonsoft.Json.Linq;
using Silmoon.AI.Client.OpenAI.Enums;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Silmoon.AI.Client.ToolCall
{
    public class CommandTool
    {
        public static Tool[] GetTools()
        {
            return [
                Tool.Create("CommandTool", "It can execute commands on the local computer and supports Windows, macOS, and Linux systems. Please note that you should not use this tool to execute dangerous commands, including power control, modifying or deleting important files and system files. Some high-risk operations require user confirmation before execution. Also, note that CommandTool does not save context with each call; each command is independent. For example, calling `cd ...` a second time renders the previous `cd` command's directory-changing behavior meaningless. Therefore, the `cd` command cannot be used alone. Consider using the \"&&\" in CMD and the \";;\" in PowerShell to pipe the `cd` command in conjunction with other commands. However, note that when using the `cd` command directly in CMD to change directories, switching between drives is ineffective; you must first change the drive letter and then use `cd` to change directories. If multiple commands are to be executed together, it is recommended to use PowerShell throughout!",
                [
                    new ToolParameterProperty("string", "The operating system on which to execute the command.", ["windows", "macos", "linux"], "os", true),
                    new ToolParameterProperty("string", "The command to execute in cmd or powershell.", null, "command", true),
                    new ToolParameterProperty("string", "Which command line should be used in Windows? What parameters are required in Windows? It supports cmd and PowerShell parameters; on other platforms, empty parameters or null parameters can be used, but PowerShell is recommended by default.", ["cmd", "powershell", null], "terminalType", true),
                ]),
                Tool.Create("StatefulShellExecute",
                """
                **Global singleton:** at most ONE persistent shell exists for the whole host. The FIRST StatefulShellExecute starts it; further calls **reuse** the shell only if you pass the **same** `instanceId`. If you call Execute with a **different** `instanceId`, the previous shell is terminated and replaced (the old id is recorded as ended—see GetSessionStatus).

                Persistent shell session: runs one line of shell input bound to `instanceId`. Calls with the same id share cwd, env, and state (unlike stateless CommandTool).

                Workflow: (1) Choose a stable `instanceId` for the task. (2) Call StatefulShellExecute with `os`, `command`, `terminalType`, and `timeoutMilliseconds`. (3) The tool waits up to `timeoutMilliseconds`, then returns FULL output captured so far; it does NOT kill the shell on timeout. (4) Poll StatefulShellGetOutput with the **current active** `instanceId`. (5) StatefulShellGetSessionStatus to check health or id mismatch. (6) StatefulShellClose when done.

                `terminalType`: on Windows use `cmd` or `powershell`. On macOS/Linux pass `bash` or empty string.

                Safety: same constraints as CommandTool—no destructive or privileged operations without explicit user approval.

                Use stateless CommandTool for one‑off commands; use these Stateful shell tools when you need directory/env continuity, long‑running commands with polling, or session health checks.
                """,
                [
                    new ToolParameterProperty("string", "Stable session key. Reuse for the same logical shell; use a new value for a fresh shell.", null, "instanceId", true),
                    new ToolParameterProperty("string", "Host OS for the shell.", ["windows", "macos", "linux"], "os", true),
                    new ToolParameterProperty("string", "Single line of shell input to execute (will be sent as one line; avoid raw newlines).", null, "command", true),
                    new ToolParameterProperty("string", "Windows: `cmd` or `powershell`. macOS/Linux: `bash` or empty.", ["cmd", "powershell", "bash", null], "terminalType", true),
                    new ToolParameterProperty("integer", "Milliseconds to wait after sending the command before returning accumulated output. Does not kill the process. Poll with StatefulShellGetOutput if longer runs are expected. Typical: 3000–60000.", null, "timeoutMilliseconds", true),
                ]),
                Tool.Create("StatefulShellGetOutput",
                """
                Poll NEW terminal output for an existing `instanceId` created by StatefulShellExecute. Returns text appended since the last StatefulShellGetOutput (or since the last StatefulShellExecute return). Also indicates whether the shell process is still running. Use when a command may outlive the Execute timeout or when you need incremental logs.
                """,
                [
                    new ToolParameterProperty("string", "Same instanceId as StatefulShellExecute.", null, "instanceId", true),
                ]),
                Tool.Create("StatefulShellGetSessionStatus",
                """
                Inspect the lifecycle of a persistent shell for `instanceId` without sending commands or reading streamed output. Returns one of: (1) **Running** — shell process is alive and managed by this host; (2) **Exited unexpectedly** — process has terminated (crash, exit, kill) while the session entry still exists—includes exit code; you should start a new shell (new instanceId or call Execute again per messages); (3) **Closed intentionally** — you (or the agent) already called StatefulShellClose; same id was released on purpose, not a surprise crash; (4) **Unknown / never created** — no active session and no recent intentional close record (never opened, typo, or tombstone expired after several days).

                Use when recovering from errors, before reusing an instanceId, or to tell user‑initiated close from unexpected termination.
                """,
                [
                    new ToolParameterProperty("string", "Same instanceId as StatefulShellExecute.", null, "instanceId", true),
                ]),
                Tool.Create("StatefulShellClose",
                """
                Closes the persistent shell for `instanceId`, terminates the underlying process, and frees resources. Call when the session is no longer needed.
                """,
                [
                    new ToolParameterProperty("string", "Same instanceId as StatefulShellExecute.", null, "instanceId", true),
                ]),
            ];
        }
        /// <summary>
        /// 有状态持久 Shell 的工具定义（对应 <c>CommandTool.ExecuteCommand</c> / <c>GetCommandOutput</c> / <c>GetShellSessionStatus</c> / <c>CloseCommand</c>），供本类注入 MCP 或其它宿主合并进 <see cref="NativeChatClient.Tools"/>。
        /// </summary>
        public static Task<StateSet<bool, MessageContent>> CallTool(string functionName, JObject parameters, string toolCallId)
        {
            StateSet<bool, MessageContent> result = null;

            switch (functionName)
            {
                case "CommandTool":
                    var commandResult = Execute(parameters["os"].Value<string>(), parameters["command"].Value<string>(), parameters["terminalType"].Value<string>());
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, commandResult, toolCallId));
                    break;
                case "StatefulShellExecute":
                    var timeoutToken = parameters["timeoutMilliseconds"];
                    int timeoutMs = timeoutToken is null || timeoutToken.Type == JTokenType.Null ? 30_000 : timeoutToken.Value<int>();
                    var shellExecResult = ExecuteCommand(parameters["instanceId"]?.Value<string>() ?? string.Empty, parameters["os"]?.Value<string>() ?? string.Empty, parameters["command"]?.Value<string>() ?? string.Empty, parameters["terminalType"]?.Value<string>() ?? string.Empty, timeoutMs);
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, shellExecResult, toolCallId));
                    break;
                case "StatefulShellGetOutput":
                    var shellPollResult = GetCommandOutput(parameters["instanceId"]?.Value<string>() ?? string.Empty, string.Empty, string.Empty, string.Empty);
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, shellPollResult, toolCallId));
                    break;
                case "StatefulShellGetSessionStatus":
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, GetShellSessionStatus(parameters["instanceId"]?.Value<string>() ?? string.Empty), toolCallId));
                    break;
                case "StatefulShellClose":
                    CloseCommand(parameters["instanceId"]?.Value<string>() ?? string.Empty);
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, "Stateful shell session closed.", toolCallId));
                    break;
                default:
                    break;
            }
            return Task.FromResult(result);
        }

        /// <summary>有状态 shell 全局单例：任意时刻最多一个持久进程；新 <c>instanceId</c> 的 Execute 会替换并结束旧会话。</summary>
        static readonly object StatefulShellGate = new();

        static string? ActiveStatefulInstanceId;
        static StatefulTerminalSession? ActiveStatefulSession;

        /// <summary>instanceId → 曾由 <see cref="CloseCommand"/> 主动关闭、或被新 instanceId 替换的时间（UTC）。</summary>
        static readonly ConcurrentDictionary<string, DateTimeOffset> SessionClosedIntentionallyAt = new();

        const double TombstoneRetentionHours = 168; // 7 天后遗忘，避免字典无限增长

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
        static string ExecuteCmd(string command)
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
        static string ExecutePowerShell(string command)
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
        static string MacOSExecute(string command)
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
        static string LinuxExecute(string command)
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



        /// <summary>
        /// 在持久化 shell 中执行命令。有状态模式为<strong>全局单例</strong>：同时只存在一个持久 shell；
        /// 使用新的 <paramref name="instanceId"/> 时会结束并替换此前的 shell（旧 id 记入 tombstone）。
        /// 超时不会结束子进程，只返回当前已累计的全部终端输出。
        /// </summary>
        /// <param name="timeoutMilliseconds">等待该命令输出的最长时间（毫秒）。到时间后返回当前缓冲区中的全部输出，shell 继续运行。</param>
        public static string ExecuteCommand(string instanceId, string os, string command, string terminalType, int timeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return "[CommandTool] instanceId 不能为空。";
            if (string.IsNullOrWhiteSpace(command)) return "[CommandTool] command 不能为空。";

            try
            {
                string? supersededId = null;
                StatefulTerminalSession session;

                lock (StatefulShellGate)
                {
                    PruneStaleTombstones();

                    if (ActiveStatefulSession != null && !string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal))
                    {
                        supersededId = ActiveStatefulInstanceId;
                        DisposeActiveStatefulSessionAndRecordTombstone(supersededId!, ActiveStatefulSession);
                        ActiveStatefulSession = null;
                        ActiveStatefulInstanceId = null;
                    }

                    // 子进程已退出但槽位未清：允许同一 instanceId 重新创建，避免单例永久卡在死 shell 上
                    if (ActiveStatefulSession != null && string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal) && ActiveStatefulSession.IsShellProcessExited)
                    {
                        DisposeActiveStatefulSessionAndRecordTombstone(instanceId, ActiveStatefulSession);
                        ActiveStatefulSession = null;
                        ActiveStatefulInstanceId = null;
                    }

                    if (ActiveStatefulSession == null)
                    {
                        SessionClosedIntentionallyAt.TryRemove(instanceId, out DateTimeOffset _);
                        ActiveStatefulSession = StatefulTerminalSession.Start(os, terminalType);
                        ActiveStatefulInstanceId = instanceId;
                    }

                    session = ActiveStatefulSession;
                }

                var output = session.ExecuteCommand(os, command, terminalType, timeoutMilliseconds);
                if (supersededId is not null)
                {
                    return $"""
                        [CommandTool] 有状态 shell 为全局单例：已结束并替换此前的实例（旧 instanceId: {supersededId}）。当前活跃 instanceId: {instanceId}。

                        {output}
                        """;
                }

                return output;
            }
            catch (Exception ex)
            {
                return $"[CommandTool] ExecuteCommand 失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取自上次调用本方法以来新增的终端输出，并报告 shell 是否仍在运行。
        /// </summary>
        public static string GetCommandOutput(string instanceId, string os, string command, string terminalType)
        {
            _ = os;
            _ = command;
            _ = terminalType;
            if (string.IsNullOrWhiteSpace(instanceId))
                return "[CommandTool] instanceId 不能为空。";

            if (!TryResolveActiveStatefulSession(instanceId, out var session, out var resolveMsg)) return resolveMsg!;

            try
            {
                return session!.GetIncrementalOutput();
            }
            catch (Exception ex)
            {
                return $"[CommandTool] GetCommandOutput 失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 关闭并移除该 instanceId 对应的持久化终端进程。
        /// </summary>
        public static void CloseCommand(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;

            lock (StatefulShellGate)
            {
                if (ActiveStatefulSession == null || !string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal)) return;

                SessionClosedIntentionallyAt[instanceId] = DateTimeOffset.UtcNow;
                try
                {
                    ActiveStatefulSession.Dispose();
                }
                catch
                {
                    // 忽略关闭时的清理异常
                }

                ActiveStatefulSession = null;
                ActiveStatefulInstanceId = null;
            }
        }

        /// <summary>
        /// 查询某 <paramref name="instanceId"/> 对应的有状态 shell 是否存在、是否在运行、是否已异常退出，
        /// 或是否曾由 <see cref="CloseCommand"/> 主动关闭（与「从未创建」区分）。
        /// </summary>
        public static string GetShellSessionStatus(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return "[CommandTool] instanceId 不能为空。";

            PruneStaleTombstones();

            lock (StatefulShellGate)
            {
                if (ActiveStatefulSession != null)
                {
                    if (string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal))
                        return ActiveStatefulSession.DescribeSessionStatus();

                    return $"""
                        [CommandTool: 会话状态]
                        有状态模式为全局单例。当前活跃 instanceId: "{ActiveStatefulInstanceId}"（与查询的 "{instanceId}" 不一致）。
                        说明: 仅存在一个持久 shell；请对上述活跃 id 调用 GetOutput/Close，或 Execute 使用新 id 以替换当前 shell。
                        """;
                }
            }

            if (SessionClosedIntentionallyAt.TryGetValue(instanceId, out var closedAt))
            {
                return $"""
                    [CommandTool: 会话状态]
                    instanceId: {instanceId}
                    状态: 会话已结束（主动 StatefulShellClose，或已被新的有状态 instanceId 替换）。非异常崩溃记录。
                    结束时间 (UTC): {closedAt:O}
                    说明: 全局仅允许一个有状态 shell；再次 Execute 可新建（可沿用本 id 或新 id，新 Execute 会占用唯一槽位）。
                    """;
            }

            return $"""
                [CommandTool: 会话状态]
                instanceId: {instanceId}
                状态: 当前无匹配记录（可能从未创建、id 拼写错误，或 tombstone 已超过保留时间）。
                说明: 请先 StatefulShellExecute，或确认 instanceId。
                """;
        }

        static void PruneStaleTombstones()
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-TombstoneRetentionHours);
            foreach (var key in SessionClosedIntentionallyAt.Keys.ToArray())
            {
                if (SessionClosedIntentionallyAt.TryGetValue(key, out var t) && t < cutoff)
                    SessionClosedIntentionallyAt.TryRemove(key, out DateTimeOffset _);
            }
        }
        static void DisposeActiveStatefulSessionAndRecordTombstone(string instanceId, StatefulTerminalSession session)
        {
            SessionClosedIntentionallyAt[instanceId] = DateTimeOffset.UtcNow;
            try
            {
                session.Dispose();
            }
            catch
            {
                // 忽略
            }
        }

        /// <summary>在持锁或调用方已确认 id 时使用：解析当前唯一有状态会话。</summary>
        static bool TryResolveActiveStatefulSession(string instanceId, out StatefulTerminalSession? session, out string? errorMessage)
        {
            session = null;
            errorMessage = null;
            lock (StatefulShellGate)
            {
                if (ActiveStatefulSession == null)
                {
                    errorMessage = $"[CommandTool] 当前没有活跃的有状态 shell，请先 ExecuteCommand。查询的 instanceId: \"{instanceId}\"。";
                    return false;
                }

                if (!string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal))
                {
                    errorMessage = $"""
                        [CommandTool] 有状态 shell 为全局单例。当前活跃 instanceId 为 "{ActiveStatefulInstanceId}"，与 "{instanceId}" 不一致。请使用活跃 id 操作，或 Execute 新 id 以替换当前 shell。
                        """;
                    return false;
                }

                session = ActiveStatefulSession;
                return true;
            }
        }

        sealed class StatefulTerminalSession : IDisposable
        {
            readonly object _executeGate = new();
            readonly StringBuilder _buffer = new();
            readonly object _bufferLock = new();
            int _incrementalMark;

            /// <summary>底层 shell 是否已结束（用于全局单例槽位回收）。</summary>
            internal bool IsShellProcessExited => _disposed || _process is null || _process.HasExited;

            Process? _process;
            Task? _stdoutReader;
            Task? _stderrReader;
            bool _disposed;

            public static StatefulTerminalSession Start(string os, string terminalType)
            {
                var psi = CreateShellStartInfo(os, terminalType);
                var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 shell 进程。");
                var session = new StatefulTerminalSession { _process = process };
                session.StartReaders();
                return session;
            }

            static ProcessStartInfo CreateShellStartInfo(string os, string terminalType)
            {
                ProcessStartInfo psi;
                switch (os)
                {
                    case "windows":
                        if (terminalType == "cmd")
                            psi = new ProcessStartInfo("cmd.exe", "/Q");
                        else if (terminalType == "powershell")
                            psi = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -Command -");
                        else
                            throw new NotSupportedException($"Unsupported terminal type: {terminalType}");
                        break;
                    case "linux":
                    case "macos":
                        psi = new ProcessStartInfo("/bin/bash", "-s");
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported operating system: {os}");
                }

                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                return psi;
            }

            void StartReaders()
            {
                var p = _process!;
                _stdoutReader = Task.Run(() => PumpStream(p.StandardOutput));
                _stderrReader = Task.Run(() => PumpStream(p.StandardError));
            }

            void PumpStream(StreamReader reader)
            {
                var buf = new char[4096];
                try
                {
                    int n;
                    while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                    {
                        var chunk = new string(buf, 0, n);
                        lock (_bufferLock)
                        {
                            _buffer.Append(chunk);
                        }
                    }
                }
                catch
                {
                    // 进程结束或流关闭
                }
            }

            public string ExecuteCommand(string os, string command, string terminalType, int timeoutMilliseconds)
            {
                ThrowIfDisposed();
                var p = _process ?? throw new InvalidOperationException("进程不可用。");

                lock (_executeGate)
                {
                    ThrowIfDisposed();
                    if (p.HasExited)
                        return "[CommandTool] shell 进程已退出，请使用新的 instanceId 调用 ExecuteCommand。";

                    Console.WriteLineWithColor($"[Stateful {os}/{terminalType}] [{InstanceTag()}] {command}", ConsoleColor.Green);

                    var stdin = p.StandardInput;
                    var lineEnding = os == "windows" && terminalType == "cmd" ? "\r\n" : "\n";
                    stdin.Write(command);
                    stdin.Write(lineEnding);
                    stdin.Flush();

                    var ms = timeoutMilliseconds <= 0 ? 30_000 : timeoutMilliseconds;
                    Task.Delay(ms).Wait();

                    lock (_bufferLock)
                    {
                        var text = FormatFullOutput(p);
                        _incrementalMark = _buffer.Length;
                        return text;
                    }
                }
            }

            string InstanceTag() => _process?.Id.ToString() ?? "?";

            string FormatFullOutput(Process p)
            {
                var sb = new StringBuilder();
                sb.AppendLine("[CommandTool: 当前终端全部输出（超时未终止进程，shell 仍在运行则可持续 GetCommandOutput）]");
                sb.Append(_buffer.ToString());
                sb.AppendLine();
                sb.AppendLine(p.HasExited
                    ? $"[状态] shell 已退出，退出码: {p.ExitCode}"
                    : $"[状态] shell 运行中，PID: {p.Id}");
                return sb.ToString();
            }

            /// <summary>供 <see cref="CommandTool.GetShellSessionStatus"/> 使用，不消费增量输出游标。</summary>
            public string DescribeSessionStatus()
            {
                ThrowIfDisposed();
                var p = _process;
                if (p is null)
                {
                    return """
                        [CommandTool: 会话状态]
                        状态: 内部错误（进程句柄不可用）。
                        """;
                }

                try
                {
                    if (!p.HasExited)
                    {
                        return $"""
                            [CommandTool: 会话状态]
                            状态: 运行中（活跃 shell，可继续 Execute / GetOutput）。
                            PID: {p.Id}
                            说明: 会话仍由本进程托管；若命令长时间无输出，可用 StatefulShellGetOutput 轮询。
                            """;
                    }

                    return $"""
                        [CommandTool: 会话状态]
                        状态: 子进程已退出（非 CloseCommand 路径下 shell 自行结束，或未通过 Close 即崩溃/退出）。
                        退出码: {p.ExitCode}
                        说明: 全局仅一个有状态 shell；进程已退出后请再次 Execute（可沿用或更换 instanceId，新 Execute 会占用唯一槽位）以启动新 shell。
                        """;
                }
                catch (InvalidOperationException)
                {
                    return """
                        [CommandTool: 会话状态]
                        状态: 无法读取进程状态（进程句柄可能已失效）。
                        """;
                }
            }

            public string GetIncrementalOutput()
            {
                ThrowIfDisposed();
                var p = _process ?? throw new InvalidOperationException("进程不可用。");

                lock (_bufferLock)
                {
                    var full = _buffer.ToString();
                    var start = Math.Clamp(_incrementalMark, 0, full.Length);
                    var chunk = full.Substring(start);
                    _incrementalMark = full.Length;

                    var sb = new StringBuilder();
                    sb.AppendLine("[CommandTool: 自上次 GetCommandOutput 以来的新输出]");
                    if (chunk.Length == 0)
                        sb.AppendLine("(无新输出)");
                    else
                        sb.Append(chunk);
                    sb.AppendLine();
                    sb.AppendLine(p.HasExited
                        ? $"[状态] shell 已退出，退出码: {p.ExitCode}"
                        : $"[状态] shell 运行中，PID: {p.Id}");
                    return sb.ToString();
                }
            }

            void ThrowIfDisposed()
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(StatefulTerminalSession));
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;

                try
                {
                    _process?.StandardInput.Close();
                }
                catch { }

                try
                {
                    if (_process is { HasExited: false })
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(5000);
                    }
                }
                catch { }

                try
                {
                    _process?.Dispose();
                }
                catch { }

                _process = null;

                try
                {
                    _stdoutReader?.Wait(2000);
                    _stderrReader?.Wait(2000);
                }
                catch { }
            }
        }
    }
}
