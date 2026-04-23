using Newtonsoft.Json.Linq;
using Silmoon.AI.Models.OpenAI.Enums;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;


namespace Silmoon.AI.Tools
{
    public class CSharpTool : ExecuteTool
    {
        static readonly object ConsoleRedirectLock = new();
        const int ExecutionTimeoutMilliseconds = 10_000;
        const int MaxOutputChars = 64_000;
        const int SuggestedMaxDownloadBytes = 5 * 1024 * 1024;
        static readonly (Regex Pattern, string Reason)[] SafetyRules =
        [
            (new Regex(@"\bEnvironment\s*\.\s*Exit\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止终止宿主进程（Environment.Exit）。"),
            (new Regex(@"\bEnvironment\s*\.\s*FailFast\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止强制终止宿主进程（Environment.FailFast）。"),
            (new Regex(@"\bSystem\.Diagnostics\.Process\b|\bProcess\s*\.", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止使用 Process 创建/控制外部进程。"),
            (new Regex(@"\[\s*DllImport\b|\bLibraryImport\b|\bNativeLibrary\s*\.", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止调用 Win32/Native DLL（P/Invoke/NativeLibrary）。"),
            (new Regex(@"\bMarshal\s*\.\s*(GetDelegateForFunctionPointer|GetFunctionPointerForDelegate|Read|Write|Copy)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止使用 Marshal 进行原生函数指针或内存操作。"),
            (new Regex(@"\bunsafe\b|\bstackalloc\b|\bfixed\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止 unsafe 指针与底层内存操作。"),
            (new Regex(@"\bUnmanagedCallersOnly\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止使用 UnmanagedCallersOnly。"),
            (new Regex(@"\bTcpListener\b|\bSocket\b\s*\.\s*(Bind|Listen|Accept)\b|\bHttpListener\b|\bKestrel\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止监听端口或创建服务化守护能力。"),
            (new Regex(@"\bwhile\s*\(\s*true\s*\)|\bfor\s*\(\s*;\s*;\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止明显的无限循环。"),
            (new Regex(@"\b(HttpClient|WebClient|HttpResponseMessage)\b[^\n\r;]*\b(GetByteArrayAsync|ReadAsByteArrayAsync)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止直接整块下载二进制内容（无大小上限）。"),
            (new Regex(@"\bWebClient\s*\.\s*Download(File|FileTaskAsync|Data|DataTaskAsync)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止使用 WebClient.Download* 进行无上限下载。"),
            (new Regex(@"\bFile\s*\.\s*WriteAllBytes\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled), "禁止直接写入未知大小二进制文件。"),
        ];

        public override Tool[] GetTools()
        {
            return [
                Tool.Create("RunCSharpCode", """
                    在当前进程内执行一段 C# Script（Roslyn）。

                    可执行形式：
                    - 使用 top-level script 语法（语句块、表达式、局部变量、for/if、LINQ 等）。
                    - 可以直接写表达式（如 `1 + 2`），返回值会以 `[return] ...` 出现在结果中。
                    - 可以使用 `Console.Write/WriteLine` 输出，输出会被捕获并返回。
                    - 不要写 `class Program` / `static void Main` 这类项目入口结构。

                    语法关键规则（高频踩坑）：
                    - `using` 指令（如 `using System.Net;`）必须放在脚本最顶部。
                    - `using var ...` / `using (...) { ... }`（资源管理语句）可以放在代码块内部。
                    - 不要在执行语句中途再写 `using 命名空间;`，否则可能出现 `CS1002` 等编译错误。

                    默认可用命名空间：
                    - System
                    - System.IO
                    - System.Linq
                    - System.Collections.Generic

                    返回规则：
                    - 成功：返回 `[return]`（若有）+ 标准输出 + `[stderr]`（若有）。
                    - 无输出：`[CSharpTool] 执行完成，无输出。`
                    - 编译失败：`[CSharpTool] 编译错误: ...`
                    - 运行时异常：`[CSharpTool] 运行异常: ...`
                    - 安全策略命中：`[CSharpTool] 安全拦截: ...`（直接拒绝，不执行）。
                    - 超时：`[CSharpTool] 执行超时: ...`（达到超时上限后终止本次执行流程）。

                    限制与注意：
                    - 不是沙箱执行：代码运行在宿主进程内，具备当前进程权限。
                    - 有执行时间上限（当前 10 秒）与输出长度上限（当前 64K 字符，超出截断）。
                    - 工具会捕获异常并返回错误文本，但不保证资源层面的绝对安全隔离。
                    - 异步代码建议使用脚本可直接执行的写法；如需阻塞等待可用 `.GetAwaiter().GetResult()`，但避免长期阻塞。

                    安全策略（必须遵守）：
                    - 严禁终止/破坏宿主进程或系统稳定性：禁止 `Environment.Exit(...)`、`FailFast(...)`、`Process.Kill(...)`、向当前/其他进程发送终止信号等。
                    - 严禁进程与命令执行能力：禁止使用 `System.Diagnostics.Process`（包括启动外部程序、shell 命令、脚本解释器、powershell/cmd/bash 等）。
                    - 严禁原生代码互操作与系统 API 直连：禁止 Win32/Native DLL 调用（P/Invoke），包括但不限于 `[DllImport]`、`LibraryImport`、`Marshal` 获取函数指针、`NativeLibrary.Load/GetExport`、`UnmanagedCallersOnly` 等。
                    - 严禁反射绕过与动态加载攻击：禁止加载未知程序集、篡改运行时环境、注入或执行来源不明代码。
                    - 严禁 `unsafe` 指针代码、内存读写越界、以及任何试图绕过托管运行时安全边界的行为。
                    - 严禁破坏性文件/系统操作：禁止批量删除、覆盖关键文件、修改系统配置、注册表/服务等高风险行为。
                    - 严禁创建服务化/守护型能力：禁止监听 Socket/端口（如 `TcpListener`、`Socket.Bind/Listen`、WebServer/Kestrel 自建监听）、禁止常驻后台任务与无限循环保活。
                    - 代码必须是一次性任务：应从开始执行到结束并返回结果，不得设计为长期驻留、阻塞等待连接、或“永不退出”。
                    - 代码应关注内存与资源回收：避免超大对象、一次性加载超大数据、无界集合增长；使用后及时释放可释放资源（`using`/`Dispose`）。
                    - 严禁故意制造内存压力或泄漏（如无限追加缓存、长期持有大对象、无上限并发分配）。
                    - 默认允许网络访问用于正常查询与 API 调用，但严禁流氓/攻击性流量行为：禁止洪水请求、批量发包、端口扫描、DoS/DDoS、爆破、恶意探测、持续高频重试。
                    - 禁止无上限下载大文件：严禁 `GetByteArrayAsync` / `ReadAsByteArrayAsync` / `WebClient.Download*` / `File.WriteAllBytes` 等整块下载与落盘写入方式。
                    - 如确需下载内容，必须采用流式读取并设置硬上限（建议不超过 5MB），超过上限立刻中止。
                    - 网络访问推荐模式：`GetAsync(url, HttpCompletionOption.ResponseHeadersRead)` + `ReadAsStreamAsync()` + 手动累计字节并在达到上限时中止。
                    - 网络请求应遵守最小化原则：低频、有限并发、有限总请求数；出现 429/5xx 时退避，不得无穷重试。
                    - 遇到可能有副作用或越权风险的操作，必须先停下并请求用户确认，不得自行执行。
                    """,
                [
                    new ToolParameterProperty("string", "code", "要执行的 C# Script 代码（top-level 语法；using 指令放顶部；不要包 Program/Main）。", null, true),
                ]),
            ];
        }

        public override Task<StateSet<bool, MessageContent>> OnToolCallInvoke(string functionName, JObject parameters, string toolCallId, StateSet<bool, MessageContent> toolMessageState)
        {
            StateSet<bool, MessageContent> result = null;
            switch (functionName)
            {
                case "RunCSharpCode":
                    string code = parameters["code"]?.ToString() ?? string.Empty;
                    string output = ExecuteCSharpCode(code);
                    result = true.ToStateSet(MessageContent.Create(Role.Tool, output, toolCallId));
                    break;
                default:
                    break;
            }
            return Task.FromResult(result);
        }

        static string ExecuteCSharpCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "[CSharpTool] code 不能为空。";
            if (TryRejectUnsafeCode(code, out string denyReason)) return denyReason;

            lock (ConsoleRedirectLock)
            {
                var originalOut = Console.Out;
                var originalError = Console.Error;
                var outputWriter = new BoundedStringWriter(MaxOutputChars);
                var errorWriter = new BoundedStringWriter(MaxOutputChars);
                var timeoutCts = new CancellationTokenSource(ExecutionTimeoutMilliseconds);

                try
                {
                    Console.SetOut(outputWriter);
                    Console.SetError(errorWriter);

                    var options = ScriptOptions.Default
                        .WithReferences(AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location)))
                        .WithImports("System", "System.IO", "System.Linq", "System.Collections.Generic");

                    var evalTask = CSharpScript.EvaluateAsync<object?>(code, options, cancellationToken: timeoutCts.Token);
                    object? returnValue;
                    try
                    {
                        returnValue = evalTask.GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        return $"[CSharpTool] 执行超时: 脚本超过 {ExecutionTimeoutMilliseconds}ms 未完成，已取消本次执行。";
                    }

                    var stdout = outputWriter.GetContent();
                    var stderr = errorWriter.GetContent();
                    var returnText = returnValue is null ? string.Empty : $"[return] {returnValue}{Environment.NewLine}";

                    var merged = $"{returnText}{stdout}";
                    if (!string.IsNullOrWhiteSpace(stderr))
                        merged += $"{Environment.NewLine}[stderr]{Environment.NewLine}{stderr}";

                    if (outputWriter.IsTruncated || errorWriter.IsTruncated)
                        merged += $"{Environment.NewLine}[CSharpTool] 输出过长已截断（单流上限 {MaxOutputChars} 字符）。";

                    return string.IsNullOrWhiteSpace(merged) ? "[CSharpTool] 执行完成，无输出。" : merged.TrimEnd();
                }
                catch (CompilationErrorException ex)
                {
                    return $"[CSharpTool] 编译错误:{Environment.NewLine}{string.Join(Environment.NewLine, ex.Diagnostics.Select(d => d.ToString()))}";
                }
                catch (Exception ex)
                {
                    var details = ex.ToString();
                    return $"[CSharpTool] 运行异常:{Environment.NewLine}{details}";
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                    timeoutCts.Dispose();
                    RunPostExecutionGc();
                }
            }
        }

        static bool TryRejectUnsafeCode(string code, out string message)
        {
            foreach (var (pattern, reason) in SafetyRules)
            {
                if (!pattern.IsMatch(code)) continue;
                message = $"[CSharpTool] 安全拦截: {reason}";
                return true;
            }

            message = string.Empty;
            return false;
        }

        static void RunPostExecutionGc()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch
            {
                // 忽略回收阶段异常，避免影响工具主流程
            }
        }

        sealed class BoundedStringWriter : TextWriter
        {
            readonly StringBuilder _buffer = new();
            readonly int _maxChars;

            public bool IsTruncated { get; private set; }

            public BoundedStringWriter(int maxChars)
            {
                _maxChars = maxChars <= 0 ? 1024 : maxChars;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value) => Append(value.ToString());
            public override void Write(string? value) => Append(value);
            public override void Write(char[] buffer, int index, int count) => Append(new string(buffer, index, count));

            public string GetContent() => _buffer.ToString();

            void Append(string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                if (_buffer.Length >= _maxChars)
                {
                    IsTruncated = true;
                    return;
                }

                var remain = _maxChars - _buffer.Length;
                if (value.Length <= remain)
                {
                    _buffer.Append(value);
                    return;
                }

                _buffer.Append(value, 0, remain);
                IsTruncated = true;
            }
        }
    }
}
