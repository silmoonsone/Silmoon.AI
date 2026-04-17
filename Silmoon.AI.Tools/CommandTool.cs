using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Silmoon.AI.Tools
{
    public class CommandTool
    {
        /// <summary>工具 schema 与内部逻辑使用的操作系统标识（大小写不敏感输入会归一化为此）。</summary>
        public const string OsWindows = "Windows";
        public const string OsMacOS = "MacOS";
        public const string OsLinux = "Linux";

        /// <summary>工具 schema 与内部逻辑使用的终端类型标识（大小写不敏感输入会归一化为此）。</summary>
        public const string TerminalCmd = "CMD";
        public const string TerminalPowerShell = "PowerShell";
        public const string TerminalBash = "Bash";

        /// <summary>有状态 shell 全局单例：任意时刻最多一个持久进程；新 <c>instanceId</c> 的 Execute 会替换并结束旧会话。</summary>
        static readonly object StatefulCommandLock = new();
        static string? ActiveStatefulInstanceId;
        static StatefulTerminalSession? ActiveStatefulSession;
        /// <summary>instanceId → 曾由 <see cref="CloseCommand"/> 主动关闭、或被新 instanceId 替换的时间（UTC）。</summary>
        static readonly ConcurrentDictionary<string, DateTimeOffset> SessionClosedIntentionallyAt = new();
        const double TombstoneRetentionHours = 168; // 7 天后遗忘，避免字典无限增长

        public static Tool[] GetTools()
        {
            return [
                Tool.Create("CommandTool", """
                **When to use:** One-off commands where you do **not** need a persistent working directory or environment (each call is independent). Prefer this for quick checks, single pipelines, or anything that fits in one invocation.

                **When NOT to use:** Multi-step work that depends on `cd`, exports, or long-running processes—use the four `StatefulCommand*` tools instead.

                **Parameters:** `os` = Windows | MacOS | Linux. `terminalType` on Windows = CMD or PowerShell (required); on MacOS/Linux = Bash or omit for default. `command` = full command line. **Matching is case-insensitive** (e.g. `pwsh` → PowerShell).

                **Rules:** No destructive/privileged actions without user approval. `cd` alone does not persist; chain with `&&` (CMD) or `;` (PowerShell) as needed.
                """,
                [
                    new ToolParameterProperty("string", "Windows | MacOS | Linux (any casing accepted).", ["Windows", "MacOS", "Linux"], "os", true),
                    new ToolParameterProperty("string", "Full command to run as a single line.", null, "command", true),
                    new ToolParameterProperty("string", "Windows: CMD or PowerShell. MacOS/Linux: Bash or null/empty for default. Any casing accepted.", ["CMD", "PowerShell", "Bash", null], "terminalType", true),
                ]),
                Tool.Create("StatefulCommandExecuteTool", """
                **Family:** Stateful interactive shell (4 tools: Execute, GetOutput, GetSessionStatus, Close). **Use together** when you need cwd/env to persist, long commands, or polling.

                **Singleton:** Only **one** persistent shell exists for the entire process. A **new** `instanceId` on Execute **replaces** the previous shell (old id is recorded as ended). Reuse the **same** `instanceId` to continue one session.

                **Flow:** (1) Pick a stable `instanceId` (e.g. task name). (2) **StatefulCommandExecuteTool** — send one line of input; wait up to `timeoutMilliseconds`; returns **full** captured output so far; does **not** kill the shell on timeout. (3) **StatefulCommandGetOutputTool** — fetch **new** output since last poll (same `instanceId` as the **active** session). (4) **StatefulCommandGetSessionStatusTool** — check running / crashed / closed / wrong id. (5) **StatefulCommandCloseTool** — release the session when done.

                **Parameters:** Same `os` / `terminalType` rules as CommandTool (case-insensitive). `timeoutMilliseconds`: typical 3000–60000.

                **Safety:** Same as CommandTool; confirm destructive operations with the user.
                """,
                [
                    new ToolParameterProperty("string", "Identifier for this shell session. Reuse for the same task; changing it on Execute replaces the previous shell.", null, "instanceId", true),
                    new ToolParameterProperty("string", "Windows | MacOS | Linux (any casing accepted).", ["Windows", "MacOS", "Linux"], "os", true),
                    new ToolParameterProperty("string", "One line of input for the shell (no embedded newlines).", null, "command", true),
                    new ToolParameterProperty("string", "Windows: CMD or PowerShell. MacOS/Linux: Bash or empty. Any casing accepted.", ["CMD", "PowerShell", "Bash", null], "terminalType", true),
                    new ToolParameterProperty("integer", "Wait this many ms after sending input, then return accumulated output (process keeps running). Use 3000–60000 for typical work.", null, "timeoutMilliseconds", true),
                ]),
                Tool.Create("StatefulCommandGetOutputTool", """
                **When:** After **StatefulCommandExecuteTool**, to read **incremental** output (new text since your last GetOutput or since Execute returned). Use if the command may run longer than Execute’s timeout.

                **Requirement:** `instanceId` must match the **currently active** shell (the one you last started or continued with Execute). If you use the wrong id, the response explains which id is active.

                Does not send a new command; only reads buffered output and reports whether the shell process is still running.
                """,
                [
                    new ToolParameterProperty("string", "Must match the active session’s instanceId (same value you passed to StatefulCommandExecuteTool).", null, "instanceId", true),
                ]),
                Tool.Create("StatefulCommandGetSessionStatusTool", """
                **When:** Before assuming a session is healthy, after errors, or when unsure if the shell exited or was closed. **Does not** run a command or read streamed logs—only returns status text.

                Explains: running OK; process exited unexpectedly (exit code); shell was **closed** by Close or **replaced** by a new instanceId; unknown id / never created.

                If you query the wrong `instanceId`, the tool tells you the **active** instanceId so you can fix the next call.
                """,
                [
                    new ToolParameterProperty("string", "instanceId to query (same format as Execute). Use the active id if you are unsure.", null, "instanceId", true),
                ]),
                Tool.Create("StatefulCommandCloseTool", """
                **When:** The persistent shell for this task is finished; releases resources and kills the underlying process.

                **Requirement:** `instanceId` must match the session you intend to close (usually the active one). No-op if the id does not match the active session.
                """,
                [
                    new ToolParameterProperty("string", "Session to close—same instanceId as StatefulCommandExecuteTool for that shell.", null, "instanceId", true),
                ]),
            ];
        }
        /// <summary>
        /// 分发 <see cref="GetTools"/> 中注册的 <c>CommandTool</c>（无状态）与 <c>StatefulCommandExecuteTool*</c>（有状态）工具；有状态实现对应 <c>ExecuteCommand</c> / <c>GetCommandOutput</c> / <c>GetShellSessionStatus</c> / <c>CloseCommand</c>。
        /// </summary>
        public static Task<StateSet<bool, MessageContent>> CallTool(string functionName, JObject parameters, string toolCallId)
        {
            StateSet<bool, MessageContent> result = null;

            switch (functionName)
            {
                case "CommandTool":
                    try
                    {
                        var osN = NormalizeOs(parameters["os"]?.Value<string>());
                        var ttN = NormalizeTerminal(parameters["terminalType"]?.Value<string>(), osN);
                        var outText = Execute(osN, parameters["command"]?.Value<string>() ?? string.Empty, ttN);
                        result = true.ToStateSet(MessageContent.Create(Role.Tool, outText, toolCallId));
                    }
                    catch (Exception ex)
                    {
                        result = false.ToStateSet<MessageContent>(null, $"[CommandTool] {ex.Message}");
                    }
                    break;
                case "StatefulCommandExecuteTool":
                    var timeoutToken = parameters["timeoutMilliseconds"];
                    int timeoutMs = timeoutToken is null || timeoutToken.Type == JTokenType.Null ? 30_000 : timeoutToken.Value<int>();
                    var shellExecResult = ExecuteCommand(
                        parameters["instanceId"]?.Value<string>() ?? string.Empty,
                        parameters["os"]?.Value<string>() ?? string.Empty,
                        parameters["command"]?.Value<string>() ?? string.Empty,
                        parameters["terminalType"]?.Value<string>() ?? string.Empty,
                        timeoutMs);
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, shellExecResult, toolCallId));
                    break;
                case "StatefulCommandGetOutputTool":
                    var shellPollResult = GetCommandOutput(parameters["instanceId"]?.Value<string>() ?? string.Empty, string.Empty, string.Empty, string.Empty);
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, shellPollResult, toolCallId));
                    break;
                case "StatefulCommandGetSessionStatusTool":
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, GetShellSessionStatus(parameters["instanceId"]?.Value<string>() ?? string.Empty), toolCallId));
                    break;
                case "StatefulCommandCloseTool":
                    CloseCommand(parameters["instanceId"]?.Value<string>() ?? string.Empty);
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, "StatefulCommandCloseTool: session closed.", toolCallId));
                    break;
                default:
                    break;
            }
            return Task.FromResult(result);
        }

        /// <summary>大小写不敏感：按小写分支，返回规范常量。</summary>
        static string NormalizeOs(string? s) => string.IsNullOrWhiteSpace(s) ? throw new ArgumentException("os 不能为空。") : s.Trim().ToLowerInvariant() switch
        {
            "windows" => OsWindows,
            "macos" => OsMacOS,
            "linux" => OsLinux,
            _ => throw new NotSupportedException($"不支持的操作系统: {s}"),
        };
        static string NormalizeTerminal(string? s, string os)
        {
            if (os == OsWindows)
            {
                if (string.IsNullOrWhiteSpace(s)) throw new ArgumentException("Windows 上需要 terminalType（CMD 或 PowerShell）。");
                return s.Trim().ToLowerInvariant() switch
                {
                    "cmd" => TerminalCmd,
                    "powershell" or "pwsh" => TerminalPowerShell,
                    _ => throw new NotSupportedException($"不支持的终端: {s}"),
                };
            }
            if (string.IsNullOrWhiteSpace(s)) return TerminalBash;
            return s.Trim().ToLowerInvariant() switch
            {
                "bash" or "sh" => TerminalBash,
                _ => throw new NotSupportedException($"不支持的终端: {s}"),
            };
        }

        static string Execute(string os, string command, string terminalType)
        {
            Console.WriteLineWithColor($"[{os}/{terminalType}] {command}", ConsoleColor.Green);
            switch (os)
            {
                case OsWindows:
                    if (terminalType == TerminalCmd) return ExecuteCmd(command);
                    if (terminalType == TerminalPowerShell) return ExecutePowerShell(command);
                    throw new NotSupportedException($"Unsupported terminal type for Windows: {terminalType}");
                case OsLinux:
                case OsMacOS:
                    return ExecuteBash(command);
                default:
                    throw new NotSupportedException($"Unsupported operating system: {os}");
            }
        }
        static string ExecuteCmd(string command)
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("无法启动 cmd.exe");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return string.IsNullOrEmpty(error) ? output : error;
        }
        static string ExecutePowerShell(string command)
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-Command {command}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("无法启动 powershell.exe");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return string.IsNullOrEmpty(error) ? output : error;
        }
        static string ExecuteBash(string command)
        {
            using var process = Process.Start(new ProcessStartInfo("/bin/bash", $"-c \"{command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("无法启动 /bin/bash");
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
        static string ExecuteCommand(string instanceId, string os, string command, string terminalType, int timeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return "[CommandTool] instanceId 不能为空。";
            if (string.IsNullOrWhiteSpace(command)) return "[CommandTool] command 不能为空。";

            try
            {
                var osNorm = NormalizeOs(os);
                var terminalNorm = NormalizeTerminal(terminalType, osNorm);
                string? supersededId = null;
                StatefulTerminalSession session;

                lock (StatefulCommandLock)
                {
                    PruneStaleTombstones();

                    if (ActiveStatefulSession != null && !string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal))
                    {
                        supersededId = ActiveStatefulInstanceId;
                        DisposeActiveStatefulSessionAndRecordTombstone(supersededId!, ActiveStatefulSession);
                        ActiveStatefulSession = null;
                        ActiveStatefulInstanceId = null;
                    }

                    if (ActiveStatefulSession != null && string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal) && ActiveStatefulSession.IsShellProcessExited)
                    {
                        DisposeActiveStatefulSessionAndRecordTombstone(instanceId, ActiveStatefulSession);
                        ActiveStatefulSession = null;
                        ActiveStatefulInstanceId = null;
                    }

                    if (ActiveStatefulSession == null)
                    {
                        SessionClosedIntentionallyAt.TryRemove(instanceId, out DateTimeOffset _);
                        ActiveStatefulSession = StatefulTerminalSession.Start(osNorm, terminalNorm);
                        ActiveStatefulInstanceId = instanceId;
                    }

                    session = ActiveStatefulSession;
                }

                var output = session.ExecuteCommand(osNorm, command, terminalNorm, timeoutMilliseconds);
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
                return $"[CommandTool] {ex.Message}";
            }
        }

        /// <summary>
        /// 获取自上次调用本方法以来新增的终端输出，并报告 shell 是否仍在运行。
        /// </summary>
        static string GetCommandOutput(string instanceId, string os, string command, string terminalType)
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
        static void CloseCommand(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;

            lock (StatefulCommandLock)
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
        static string GetShellSessionStatus(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return "[CommandTool] instanceId 不能为空。";
            PruneStaleTombstones();
            lock (StatefulCommandLock)
            {
                if (ActiveStatefulSession != null)
                {
                    if (string.Equals(ActiveStatefulInstanceId, instanceId, StringComparison.Ordinal)) return ActiveStatefulSession.DescribeSessionStatus();
                    return $"""
                        [CommandTool: 会话状态]
                        有状态模式为全局单例。当前活跃 instanceId: "{ActiveStatefulInstanceId}"（与查询的 "{instanceId}" 不一致）。
                        说明: 仅存在一个持久 shell；请对上述活跃 id 调用 GetOutput/Close，或 Execute 使用新 id 以替换当前 shell。
                        """;
                }
            }

            if (SessionClosedIntentionallyAt.TryGetValue(instanceId, out var closedAt))
                return $"""
                    [CommandTool: 会话状态]
                    instanceId: {instanceId}
                    状态: 会话已结束（主动 StatefulCommandCloseTool，或已被新的有状态 instanceId 替换）。非异常崩溃记录。
                    结束时间 (UTC): {closedAt:O}
                    说明: 全局仅允许一个有状态 shell；再次 Execute 可新建（可沿用本 id 或新 id，新 Execute 会占用唯一槽位）。
                    """;

            return $"""
                [CommandTool: 会话状态]
                instanceId: {instanceId}
                状态: 当前无匹配记录（可能从未创建、id 拼写错误，或 tombstone 已超过保留时间）。
                说明: 请先 StatefulCommandExecuteTool，或确认 instanceId。
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
            lock (StatefulCommandLock)
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
                    case OsWindows:
                        if (terminalType == TerminalCmd)
                            psi = new ProcessStartInfo("cmd.exe", "/Q");
                        else if (terminalType == TerminalPowerShell)
                            psi = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -Command -");
                        else
                            throw new NotSupportedException($"Unsupported terminal type: {terminalType}");
                        break;
                    case OsLinux:
                    case OsMacOS:
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
                    if (p.HasExited) return "[CommandTool] shell 进程已退出，请使用新的 instanceId 调用 ExecuteCommand。";

                    Console.WriteLineWithColor($"[{os}/{terminalType} (stateful)] [{InstanceTag()}] {command}", ConsoleColor.Green);

                    var stdin = p.StandardInput;
                    var lineEnding = os == OsWindows && terminalType == TerminalCmd ? "\r\n" : "\n";
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
                            说明: 会话仍由本进程托管；若命令长时间无输出，可用 StatefulCommandGetOutputTool 轮询。
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
