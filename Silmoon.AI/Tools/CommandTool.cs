using Newtonsoft.Json.Linq;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.AI.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Silmoon.AI.Tools
{
    public class CommandTool : ExecuteTool
    {
        public const string CommandFunctionName = "Command_Run";
        public const string StatefulExecuteFunctionName = "Command_StatefulExecute";
        public const string StatefulGetOutputFunctionName = "Command_StatefulGetOutput";
        public const string StatefulGetSessionStatusFunctionName = "Command_StatefulGetSessionStatus";
        public const string StatefulCloseFunctionName = "Command_StatefulClose";

        public const int DefaultStatefulTimeoutMs = 30_000;
        public const int MaxStatefulTimeoutMs = 60_000;
        public const int DefaultStatelessTimeoutMs = 60_000;
        public const int MaxStatelessTimeoutMs = 60_000;
        public const int DefaultIdleCompletionMs = 3_000;
        const int MinIdleCompletionMs = 100;
        const int MaxStatefulBufferChars = 1_000_000;

        /// <summary>工具 schema 与内部逻辑使用的操作系统标识（大小写不敏感输入会归一化为此）。</summary>
        public const string OsWindows = "Windows";
        public const string OsMacOS = "MacOS";
        public const string OsLinux = "Linux";

        /// <summary>工具 schema 与内部逻辑使用的终端类型标识（大小写不敏感输入会归一化为此）。</summary>
        public const string TerminalCmd = "CMD";
        public const string TerminalPowerShell = "PowerShell";
        public const string TerminalBash = "Bash";

        /// <summary>有状态 shell 多实例表：instanceId 作为实例名，互相独立；同一实例内部串行执行。</summary>
        static readonly object StatefulCommandLock = new();
        static readonly ConcurrentDictionary<string, StatefulTerminalSession> StatefulSessions = new();
        /// <summary>instanceId → 曾由 <see cref="CloseCommand"/> 主动关闭、或退出后被同名实例替换的时间（UTC）。</summary>
        static readonly ConcurrentDictionary<string, DateTimeOffset> SessionClosedIntentionallyAt = new();
        const double TombstoneRetentionHours = 168; // 7 天后遗忘，避免字典无限增长

        /// <summary>有状态工具族多实例使用规则。</summary>
        const string StatefulMultiInstanceRule =
            "Stateful shells are multi-instance: `instanceId` is the stable shell name. " +
            "Use them only when you need shared cwd/env, a long-running process, an interactive session, or parallel named tasks; otherwise prefer Command_Run. " +
            "Different instanceIds can run independent long-lived shells; reuse the same id to keep cwd/env. " +
            "Operations on the same instance are serialized. Close instances when done. Use GetSessionStatus with empty/omitted instanceId to list instances.";

        public override async Task<ToolCallResult> OnToolCallInvoke(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult) => await ToolCall(toolCallParameter, toolCallResult);


        public override Tool[] GetTools()
        {
            var statefulTools = $"{StatefulExecuteFunctionName}, {StatefulGetOutputFunctionName}, {StatefulGetSessionStatusFunctionName}, {StatefulCloseFunctionName}";
            return [
                Tool.Create(CommandFunctionName, $"""
                Stateless one-shot shell (new process; no cwd/env carry-over). Hard timeout {MaxStatelessTimeoutMs / 1000}s.
                Use: independent short commands. Not for: same-shell chains, streaming/long jobs, cwd/env-dependent steps → `{StatefulExecuteFunctionName}`.
                Stateful tools ({statefulTools}) support multiple named shell instances via `instanceId`.
                Prefer specialized tools over shell when they fit. Parallel if independent; serial if dependent. Large/streaming output should use stateful tools and polling.
                Returns `State`/`Message`/`Data`; check `State` before using output. No destructive ops without user approval.
                """,
                [
                    new ToolParameterProperty("string", "os", "Windows|MacOS|Linux (case-insensitive).", ["Windows", "MacOS", "Linux"], true),
                    new ToolParameterProperty("string", "command", "Single line only.", null, true),
                    new ToolParameterProperty("string", "terminalType", "Windows: CMD|PowerShell. Mac/Linux: Bash or omit.", ["CMD", "PowerShell", "Bash", null], true),
                ]),
                Tool.Create(StatefulExecuteFunctionName, $"""
                {StatefulMultiInstanceRule}
                Execute one command in a named persistent shell. If `instanceId` does not exist, it starts a new shell; if it exists, it reuses that shell.
                Use separate instanceIds for independent services/tasks/directories/ssh sessions. Use one stable instanceId per task.
                Flow per instance: Execute → GetOutput as needed → more Execute/GetOutput → Close when done.
                Timing: `timeoutMilliseconds` max wait (default {DefaultStatefulTimeoutMs}, cap {MaxStatefulTimeoutMs}); `idleCompletionMilliseconds` silence-after-output for early return (default {DefaultIdleCompletionMs}). Still running at timeout → next-turn GetOutput.
                One shell input line per call; shell syntax such as `&&`, pipes, loops, and redirects is allowed when needed. Returns `State`/`Message`/`Data`.
                """,
                [
                    new ToolParameterProperty("string", "instanceId", "Stable id for this task; reuse across stateful calls.", null, true),
                    new ToolParameterProperty("string", "os", "Windows|MacOS|Linux.", ["Windows", "MacOS", "Linux"], true),
                    new ToolParameterProperty("string", "command", "One shell input line. Shell syntax is allowed when needed.", null, true),
                    new ToolParameterProperty("string", "terminalType", "Windows: CMD|PowerShell. Mac/Linux: Bash or empty.", ["CMD", "PowerShell", "Bash", null], true),
                    new ToolParameterProperty("integer", "timeoutMilliseconds", $"Max wait ms (default {DefaultStatefulTimeoutMs}, cap {MaxStatefulTimeoutMs}).", null, false),
                    new ToolParameterProperty("integer", "idleCompletionMilliseconds", $"Silence-after-output ms for early return (default {DefaultIdleCompletionMs}).", null, false),
                ]),
                Tool.Create(StatefulGetOutputFunctionName, $"""
                {StatefulMultiInstanceRule}
                Poll new stdout/stderr since last Execute/GetOutput for the named shell instance.
                `waitMilliseconds`: pre-read delay, 0=now (max {MaxStatefulTimeoutMs}); use a small wait for streaming commands. Returns `State`/`Message`/`Data`.
                """,
                [
                    new ToolParameterProperty("string", "instanceId", "Named shell instance to read.", null, true),
                    new ToolParameterProperty("integer", "waitMilliseconds", $"Pre-read ms (0=now, max {MaxStatefulTimeoutMs}).", null, false),
                ]),
                Tool.Create(StatefulGetSessionStatusFunctionName, $"""
                {StatefulMultiInstanceRule}
                Check shell sessions without reading output. Omit or pass empty `instanceId` to list all named instances; pass one id for details.
                Returns `State`/`Message`/`Data`.
                """,
                [
                    new ToolParameterProperty("string", "instanceId", "Optional named shell instance. Empty/omitted lists all instances.", null, false),
                ]),
                Tool.Create(StatefulCloseFunctionName, $"""
                {StatefulMultiInstanceRule}
                Close the named stateful shell instance when user asks or that shell work is finished. Other instances remain running.
                Returns `State`/`Message`/`Data`.
                """,
                [
                    new ToolParameterProperty("string", "instanceId", "Named shell instance to close.", null, true),
                ]),
            ];
        }

        public async Task<ToolCallResult> ToolCall(ToolCallParameter toolCallParameter, ToolCallResult toolCallResult)
        {
            ToolCallResult result = null;

            var functionName = toolCallParameter.FunctionName;
            var parameters = toolCallParameter.Parameters;

            switch (functionName)
            {
                case CommandFunctionName:
                    try
                    {
                        await NotifyToolExecuting(functionName, toolCallParameter);
                        var osN = NormalizeOs(parameters["os"]?.Value<string>());
                        var ttN = NormalizeTerminal(parameters["terminalType"]?.Value<string>(), osN);
                        var execResult = Execute(osN, parameters["command"]?.Value<string>() ?? string.Empty, ttN);
                        result = ToolCallResult.Create(toolCallParameter, ToToolObjectResult(execResult));
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{CommandFunctionName}] {ex.Message}"));
                    }
                    finally
                    {
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    break;
                case StatefulExecuteFunctionName:
                    try
                    {
                        await NotifyToolExecuting(functionName, toolCallParameter);
                        int timeoutMs = NormalizeTimeoutMs(parameters["timeoutMilliseconds"]);
                        int idleCompletionMs = NormalizeIdleCompletionMs(parameters["idleCompletionMilliseconds"], timeoutMs);
                        var shellExecResult = ExecuteCommand(
                            parameters["instanceId"]?.Value<string>() ?? string.Empty,
                            parameters["os"]?.Value<string>() ?? string.Empty,
                            parameters["command"]?.Value<string>() ?? string.Empty,
                            parameters["terminalType"]?.Value<string>() ?? string.Empty,
                            timeoutMs,
                            idleCompletionMs);
                        result = ToolCallResult.Create(toolCallParameter, ToToolObjectResult(shellExecResult));
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{StatefulExecuteFunctionName}] {ex.Message}"));
                    }
                    finally
                    {
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    break;
                case StatefulGetOutputFunctionName:
                    try
                    {
                        await NotifyToolExecuting(functionName, toolCallParameter);
                        int waitBeforeReadMs = NormalizeWaitBeforeReadMs(parameters["waitMilliseconds"]);
                        var shellPollResult = GetCommandOutput(parameters["instanceId"]?.Value<string>() ?? string.Empty, waitBeforeReadMs);
                        result = ToolCallResult.Create(toolCallParameter, ToToolObjectResult(shellPollResult));
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{StatefulGetOutputFunctionName}] {ex.Message}"));
                    }
                    finally
                    {
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    break;
                case StatefulGetSessionStatusFunctionName:
                    try
                    {
                        await NotifyToolExecuting(functionName, toolCallParameter);
                        result = ToolCallResult.Create(toolCallParameter, ToToolObjectResult(GetShellSessionStatus(parameters["instanceId"]?.Value<string>() ?? string.Empty)));
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{StatefulGetSessionStatusFunctionName}] {ex.Message}"));
                    }
                    finally
                    {
                        await NotifyToolExecuted(functionName, toolCallParameter, result);
                    }
                    break;
                case StatefulCloseFunctionName:
                    try
                    {
                        await NotifyToolExecuting(functionName, toolCallParameter);
                        var closeResult = CloseCommand(parameters["instanceId"]?.Value<string>() ?? string.Empty);
                        result = ToolCallResult.Create(toolCallParameter, ToToolObjectResult(closeResult));
                    }
                    catch (Exception ex)
                    {
                        result = ToolCallResult.Create(toolCallParameter, false.ToStateSet<object>(null, $"[{StatefulCloseFunctionName}] {ex.Message}"));
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

        static StateSet<bool, string> RunStatelessProcess(ProcessStartInfo psi, string startFailureMessage)
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException(startFailureMessage);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(MaxStatelessTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch { }
                return false.ToStateSet<string>(null, $"[{CommandFunctionName}] 命令执行超时（>{MaxStatelessTimeoutMs}ms），进程已终止。");
            }

            try
            {
                Task.WaitAll([outputTask, errorTask], 5000);
            }
            catch { }

            return true.ToStateSet<string>(CombineProcessOutput(outputTask, errorTask));
        }

        static StateSet<bool, string> Execute(string os, string command, string terminalType)
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
        static StateSet<bool, string> ExecuteCmd(string command)
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
            return RunStatelessProcess(psi, "无法启动 cmd.exe");
        }

        static StateSet<bool, string> ExecutePowerShell(string command)
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);
            return RunStatelessProcess(psi, "无法启动 powershell.exe");
        }

        static StateSet<bool, string> ExecuteBash(string command)
        {
            var psi = new ProcessStartInfo("/bin/bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
            return RunStatelessProcess(psi, "无法启动 /bin/bash");
        }

        static string CombineProcessOutput(Task<string> outputTask, Task<string> errorTask)
        {
            string output = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
            string error = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
            if (string.IsNullOrEmpty(output)) return error;
            if (string.IsNullOrEmpty(error)) return output;
            return output + Environment.NewLine + error;
        }

        static StateSet<bool, object> ToToolObjectResult(StateSet<bool, string> result) =>
            result.State ? true.ToStateSet<object>(result.Data) : false.ToStateSet<object>(null, result.Message);

        static int NormalizeWaitBeforeReadMs(JToken? token)
        {
            if (token is null || token.Type == JTokenType.Null) return 0;
            int ms = token.Value<int>();
            if (ms < 0) ms = 0;
            if (ms > MaxStatefulTimeoutMs) ms = MaxStatefulTimeoutMs;
            return ms;
        }

        static int NormalizeTimeoutMs(JToken? token) => NormalizeTimeoutMs(token is null || token.Type == JTokenType.Null ? DefaultStatefulTimeoutMs : token.Value<int>());

        static int NormalizeTimeoutMs(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0) return DefaultStatefulTimeoutMs;
            if (timeoutMilliseconds > MaxStatefulTimeoutMs) return MaxStatefulTimeoutMs;
            return timeoutMilliseconds;
        }

        static int NormalizeIdleCompletionMs(JToken? token, int timeoutMs)
        {
            int idle = token is null || token.Type == JTokenType.Null ? DefaultIdleCompletionMs : token.Value<int>();
            if (idle < MinIdleCompletionMs) idle = MinIdleCompletionMs;
            if (idle > MaxStatefulTimeoutMs) idle = MaxStatefulTimeoutMs;
            if (idle > timeoutMs) idle = timeoutMs;
            return idle;
        }

        /// <summary>
        /// 在指定名称的持久化 shell 中执行命令。不同 <paramref name="instanceId"/> 对应不同 shell 实例；
        /// 同一实例保留 cwd/env 并串行执行。
        /// 超时不会结束子进程，只返回当前已累计的全部终端输出。
        /// </summary>
        /// <param name="timeoutMilliseconds">等待该命令输出的最长时间（毫秒）。输出静默达到 idleCompletionMilliseconds 时提前返回；否则超时返回。</param>
        /// <param name="idleCompletionMilliseconds">自出现新输出后，连续无新输出的毫秒数，视为命令可能已完成。</param>
        static StateSet<bool, string> ExecuteCommand(string instanceId, string os, string command, string terminalType, int timeoutMilliseconds, int idleCompletionMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return false.ToStateSet<string>(null, $"[{StatefulExecuteFunctionName}] instanceId 不能为空。");
            if (string.IsNullOrWhiteSpace(command)) return false.ToStateSet<string>(null, $"[{StatefulExecuteFunctionName}] command 不能为空。");

            try
            {
                var osNorm = NormalizeOs(os);
                var terminalNorm = NormalizeTerminal(terminalType, osNorm);
                StatefulTerminalSession session;
                instanceId = instanceId.Trim();

                lock (StatefulCommandLock)
                {
                    PruneStaleTombstones();

                    if (StatefulSessions.TryGetValue(instanceId, out var existingSession) && existingSession.IsShellProcessExited)
                    {
                        DisposeStatefulSessionAndRecordTombstone(instanceId, existingSession);
                        StatefulSessions.TryRemove(instanceId, out StatefulTerminalSession _);
                        existingSession = null;
                    }

                    if (existingSession != null && !existingSession.MatchesEnvironment(osNorm, terminalNorm))
                    {
                        return false.ToStateSet<string>(null,
                            $"[{StatefulExecuteFunctionName}] instanceId \"{instanceId}\" 的当前会话环境为 {existingSession.Os}/{existingSession.TerminalType}，与请求的 {osNorm}/{terminalNorm} 不一致。请 Close 后重建，或使用新的 instanceId。");
                    }

                    if (existingSession == null)
                    {
                        SessionClosedIntentionallyAt.TryRemove(instanceId, out DateTimeOffset _);
                        existingSession = StatefulTerminalSession.Start(osNorm, terminalNorm);
                        StatefulSessions[instanceId] = existingSession;
                    }

                    session = existingSession;
                }

                var output = session.ExecuteCommand(osNorm, command, terminalNorm, timeoutMilliseconds, idleCompletionMilliseconds);
                return true.ToStateSet<string>(output);
            }
            catch (Exception ex)
            {
                return false.ToStateSet<string>(null, $"[{StatefulExecuteFunctionName}] {ex.Message}");
            }
        }

        /// <summary>
        /// 获取自上次调用本方法以来新增的终端输出，并报告 shell 是否仍在运行。
        /// </summary>
        /// <param name="waitBeforeReadMilliseconds">在读取缓冲区前额外等待的毫秒数（0 表示立即读取）。用于 ping 等输出陆续到达的场景。</param>
        static StateSet<bool, string> GetCommandOutput(string instanceId, int waitBeforeReadMilliseconds = 0)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return false.ToStateSet<string>(null, $"[{StatefulGetOutputFunctionName}] instanceId 不能为空。");

            instanceId = instanceId.Trim();
            if (!TryResolveStatefulSession(instanceId, out var session, out var resolveMsg, StatefulGetOutputFunctionName))
                return false.ToStateSet<string>(null, resolveMsg!);

            try
            {
                return true.ToStateSet<string>(session!.GetIncrementalOutput(waitBeforeReadMilliseconds));
            }
            catch (Exception ex)
            {
                return false.ToStateSet<string>(null, $"[{StatefulGetOutputFunctionName}] {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭并移除该 instanceId 对应的持久化终端进程。
        /// </summary>
        static StateSet<bool, string> CloseCommand(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return false.ToStateSet<string>(null, $"[{StatefulCloseFunctionName}] instanceId 不能为空。");

            instanceId = instanceId.Trim();
            lock (StatefulCommandLock)
            {
                StatefulTerminalSession session;
                if (!StatefulSessions.TryRemove(instanceId, out session))
                    return false.ToStateSet<string>(null, BuildNoMatchingSessionMessage(StatefulCloseFunctionName, instanceId));

                SessionClosedIntentionallyAt[instanceId] = DateTimeOffset.UtcNow;

                try
                {
                    session.Dispose();
                }
                catch
                {
                    // 忽略关闭时的清理异常
                }
            }

            return true.ToStateSet<string>($"{StatefulCloseFunctionName}: session \"{instanceId}\" closed.");
        }

        /// <summary>
        /// 查询所有有状态 shell，或查询某 <paramref name="instanceId"/> 对应的 shell 是否存在、是否在运行、是否已异常退出，
        /// 或是否曾由 <see cref="CloseCommand"/> 主动关闭（与「从未创建」区分）。
        /// </summary>
        static StateSet<bool, string> GetShellSessionStatus(string instanceId)
        {
            PruneStaleTombstones();
            if (string.IsNullOrWhiteSpace(instanceId))
                return true.ToStateSet<string>(DescribeAllStatefulSessions());

            instanceId = instanceId.Trim();
            lock (StatefulCommandLock)
            {
                if (StatefulSessions.TryGetValue(instanceId, out var session))
                    return true.ToStateSet<string>(session.DescribeSessionStatus(instanceId));
            }

            if (SessionClosedIntentionallyAt.TryGetValue(instanceId, out var closedAt))
                return true.ToStateSet<string>($"""
                    [{StatefulGetSessionStatusFunctionName}: 会话状态]
                    instanceId: {instanceId}
                    状态: 会话已结束（主动 {StatefulCloseFunctionName}，或退出后已被同名实例替换）。非异常崩溃记录。
                    结束时间 (UTC): {closedAt:O}
                    说明: 再次 Execute 可用此 instanceId 新建同名 shell；其他命名实例不受影响。
                    """);

            return true.ToStateSet<string>($"""
                [{StatefulGetSessionStatusFunctionName}: 会话状态]
                instanceId: {instanceId}
                状态: 当前无匹配记录（可能从未创建、id 拼写错误，或 tombstone 已超过保留时间）。
                当前实例: {FormatActiveSessionIds()}
                说明: 请先 {StatefulExecuteFunctionName}，或确认 instanceId。
                """);
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
        static void DisposeStatefulSessionAndRecordTombstone(string instanceId, StatefulTerminalSession session)
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

        static bool TryResolveStatefulSession(string instanceId, out StatefulTerminalSession? session, out string? errorMessage, string callerFunctionName)
        {
            session = null;
            errorMessage = null;
            lock (StatefulCommandLock)
            {
                if (!StatefulSessions.TryGetValue(instanceId, out session))
                {
                    errorMessage = BuildNoMatchingSessionMessage(callerFunctionName, instanceId);
                    return false;
                }
                return true;
            }
        }

        static string BuildNoMatchingSessionMessage(string callerFunctionName, string instanceId)
        {
            if (SessionClosedIntentionallyAt.TryGetValue(instanceId, out var closedAt))
                return $"""
                    [{callerFunctionName}] instanceId "{instanceId}" 对应的有状态 shell 已结束。
                    结束时间 (UTC): {closedAt:O}
                    当前实例: {FormatActiveSessionIds()}
                    """;

            return $"""
                [{callerFunctionName}] 找不到 instanceId "{instanceId}" 对应的有状态 shell。
                当前实例: {FormatActiveSessionIds()}
                可用 {StatefulGetSessionStatusFunctionName}（不传 instanceId）查看全部实例。
                """;
        }

        static string FormatActiveSessionIds()
        {
            var ids = StatefulSessions.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            return ids.Length == 0 ? "(无)" : string.Join(", ", ids.Select(x => $"\"{x}\""));
        }

        static string DescribeAllStatefulSessions()
        {
            lock (StatefulCommandLock)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{StatefulGetSessionStatusFunctionName}: 会话列表]");
                if (StatefulSessions.IsEmpty)
                {
                    sb.AppendLine("当前没有运行中的有状态 shell 实例。");
                }
                else
                {
                    foreach (var item in StatefulSessions.OrderBy(x => x.Key, StringComparer.Ordinal))
                        sb.AppendLine(item.Value.DescribeSessionSummary(item.Key));
                }

                if (!SessionClosedIntentionallyAt.IsEmpty)
                {
                    sb.AppendLine();
                    sb.AppendLine($"最近结束的实例（保留 {TombstoneRetentionHours:g} 小时）:");
                    foreach (var item in SessionClosedIntentionallyAt.OrderBy(x => x.Key, StringComparer.Ordinal))
                        sb.AppendLine($"- instanceId: {item.Key}; endedAtUtc: {item.Value:O}");
                }

                return sb.ToString();
            }
        }

        sealed class StatefulTerminalSession : IDisposable
        {
            const int OutputPollIntervalMs = 50;

            readonly object _executeGate = new();
            readonly StringBuilder _buffer = new();
            readonly object _bufferLock = new();
            int _incrementalMark;
            bool _bufferTruncated;

            internal string Os { get; private set; } = string.Empty;
            internal string TerminalType { get; private set; } = string.Empty;
            internal bool MatchesEnvironment(string os, string terminalType) =>
                string.Equals(Os, os, StringComparison.Ordinal) && string.Equals(TerminalType, terminalType, StringComparison.Ordinal);

            /// <summary>底层 shell 是否已结束（同名 Execute 会回收后重建）。</summary>
            internal bool IsShellProcessExited => _disposed || _process is null || _process.HasExited;

            Process? _process;
            Task? _stdoutReader;
            Task? _stderrReader;
            bool _disposed;

            public static StatefulTerminalSession Start(string os, string terminalType)
            {
                var psi = CreateShellStartInfo(os, terminalType);
                var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 shell 进程。");
                var session = new StatefulTerminalSession
                {
                    _process = process,
                    Os = os,
                    TerminalType = terminalType,
                };
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
                            if (_buffer.Length >= MaxStatefulBufferChars)
                            {
                                if (!_bufferTruncated)
                                {
                                    _buffer.AppendLine();
                                    _buffer.Append($"[{StatefulExecuteFunctionName}: 输出缓冲区已达 {MaxStatefulBufferChars} 字符上限，后续输出已截断]");
                                    _bufferTruncated = true;
                                }
                                continue;
                            }

                            var remaining = MaxStatefulBufferChars - _buffer.Length;
                            _buffer.Append(chunk.Length <= remaining ? chunk : chunk[..remaining]);
                            if (chunk.Length > remaining && !_bufferTruncated)
                            {
                                _buffer.AppendLine();
                                _buffer.Append($"[{StatefulExecuteFunctionName}: 输出缓冲区已达 {MaxStatefulBufferChars} 字符上限，后续输出已截断]");
                                _bufferTruncated = true;
                            }
                        }
                    }
                }
                catch
                {
                    // 进程结束或流关闭
                }
            }

            public string ExecuteCommand(string os, string command, string terminalType, int timeoutMilliseconds, int idleCompletionMilliseconds)
            {
                ThrowIfDisposed();
                var p = _process ?? throw new InvalidOperationException("进程不可用。");

                lock (_executeGate)
                {
                    ThrowIfDisposed();
                    if (p.HasExited) throw new InvalidOperationException("shell 进程已退出，请使用新的 instanceId 调用 Execute。");

                    Console.WriteLineWithColor($"[{os}/{terminalType} (stateful)] [{InstanceTag()}] {command}", ConsoleColor.Green);

                    var stdin = p.StandardInput;
                    var lineEnding = os == OsWindows && terminalType == TerminalCmd ? "\r\n" : "\n";
                    stdin.Write(command);
                    stdin.Write(lineEnding);
                    stdin.Flush();

                    var ms = NormalizeTimeoutMs(timeoutMilliseconds);
                    var bufferLengthAtSend = GetBufferLength();
                    var completedEarly = WaitUntilOutputIdleOrTimeout(ms, bufferLengthAtSend, idleCompletionMilliseconds);

                    lock (_bufferLock)
                    {
                        var text = FormatFullOutput(p, completedEarly);
                        _incrementalMark = _buffer.Length;
                        return text;
                    }
                }
            }

            int GetBufferLength()
            {
                lock (_bufferLock)
                    return _buffer.Length;
            }

            /// <summary>在最大等待时间内轮询缓冲区；有新输出且静默一段时间后提前返回，否则超时返回。</summary>
            bool WaitUntilOutputIdleOrTimeout(int maxWaitMs, int bufferLengthAtSend, int idleCompletionMs)
            {
                var deadline = Environment.TickCount64 + maxWaitMs;
                var lastLength = GetBufferLength();
                var lastChangeTick = Environment.TickCount64;
                var sawOutputSinceSend = lastLength > bufferLengthAtSend;

                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(OutputPollIntervalMs);

                    var currentLength = GetBufferLength();
                    if (currentLength > bufferLengthAtSend)
                        sawOutputSinceSend = true;

                    if (currentLength != lastLength)
                    {
                        lastLength = currentLength;
                        lastChangeTick = Environment.TickCount64;
                    }
                    else if (sawOutputSinceSend && Environment.TickCount64 - lastChangeTick >= idleCompletionMs)
                        return true;
                }

                return false;
            }

            string InstanceTag() => _process?.Id.ToString() ?? "?";

            string FormatFullOutput(Process p, bool completedEarly)
            {
                var sb = new StringBuilder();
                sb.AppendLine(completedEarly
                    ? $"[{StatefulExecuteFunctionName}: 当前终端全部输出（检测到输出静默，已提前返回；shell 仍在运行则可持续 {StatefulGetOutputFunctionName}）]"
                    : $"[{StatefulExecuteFunctionName}: 当前终端全部输出（已达最大等待时间；shell 仍在运行则可持续 {StatefulGetOutputFunctionName}）]");
                sb.Append(_buffer.ToString());
                sb.AppendLine();
                sb.AppendLine(p.HasExited
                    ? $"[状态] shell 已退出，退出码: {p.ExitCode}"
                    : $"[状态] shell 运行中，PID: {p.Id}");
                return sb.ToString();
            }

            /// <summary>供 <see cref="CommandTool.GetShellSessionStatus"/> 使用，不消费增量输出游标。</summary>
            public string DescribeSessionSummary(string instanceId)
            {
                var p = _process;
                if (p is null)
                    return $"- instanceId: {instanceId}; 状态: 内部错误（进程句柄不可用）; 环境: {Os}/{TerminalType}";

                try
                {
                    return p.HasExited
                        ? $"- instanceId: {instanceId}; 状态: 已退出; exitCode: {p.ExitCode}; 环境: {Os}/{TerminalType}"
                        : $"- instanceId: {instanceId}; 状态: 运行中; PID: {p.Id}; 环境: {Os}/{TerminalType}";
                }
                catch (InvalidOperationException)
                {
                    return $"- instanceId: {instanceId}; 状态: 无法读取进程状态; 环境: {Os}/{TerminalType}";
                }
            }

            public string DescribeSessionStatus(string instanceId)
            {
                ThrowIfDisposed();
                var p = _process;
                if (p is null)
                {
                    return $"""
                        [{StatefulGetSessionStatusFunctionName}: 会话状态]
                        instanceId: {instanceId}
                        状态: 内部错误（进程句柄不可用）。
                        """;
                }

                try
                {
                    if (!p.HasExited)
                    {
                        return $"""
                            [{StatefulGetSessionStatusFunctionName}: 会话状态]
                            instanceId: {instanceId}
                            状态: 运行中（活跃 shell，可继续 Execute / GetOutput）。
                            PID: {p.Id}
                            环境: {Os}/{TerminalType}
                            说明: 会话仍由本进程托管；若命令长时间无输出，可用 {StatefulGetOutputFunctionName} 轮询。
                            """;
                    }

                    return $"""
                        [{StatefulGetSessionStatusFunctionName}: 会话状态]
                        instanceId: {instanceId}
                        状态: 子进程已退出（非 Close 路径下 shell 自行结束，或未通过 Close 即崩溃/退出）。
                        退出码: {p.ExitCode}
                        环境: {Os}/{TerminalType}
                        说明: 进程已退出后请再次 Execute（可沿用此 instanceId 或更换 instanceId）以启动 shell。
                        """;
                }
                catch (InvalidOperationException)
                {
                    return $"""
                        [{StatefulGetSessionStatusFunctionName}: 会话状态]
                        instanceId: {instanceId}
                        状态: 无法读取进程状态（进程句柄可能已失效）。
                        """;
                }
            }

            /// <summary>读取自上次游标后的新增输出，并附带 shell 运行状态。</summary>
            /// <param name="waitBeforeReadMilliseconds">在加锁读取缓冲区之前先等待的毫秒数，便于收集陆续到达的输出（如 ping）。0 表示不等待。</param>
            public string GetIncrementalOutput(int waitBeforeReadMilliseconds = 0)
            {
                ThrowIfDisposed();
                var p = _process ?? throw new InvalidOperationException("进程不可用。");

                lock (_executeGate)
                {
                    if (waitBeforeReadMilliseconds > 0)
                        Thread.Sleep(waitBeforeReadMilliseconds);

                    lock (_bufferLock)
                    {
                        var full = _buffer.ToString();
                        var start = Math.Clamp(_incrementalMark, 0, full.Length);
                        var chunk = full.Substring(start);
                        _incrementalMark = full.Length;

                        var sb = new StringBuilder();
                        sb.AppendLine($"[{StatefulGetOutputFunctionName}: 自上次读取以来的新输出]");
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

