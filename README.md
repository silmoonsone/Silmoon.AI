# Silmoon.AI

基于 .NET 的轻量库：连接 **OpenAI-Compatible** 聊天接口，支持 **工具调用（Function Calling）**，内置常用本地工具与示例程序。

**说明：本 README 由 AI 生成，请以仓库源码为准。**

## 能做什么

- 兼容 OpenAI 的 **`/chat/completions`**（普通请求 + SSE 流式）
- **工具调用**：模型返回 `tool_calls` → **`ExecuteToolManager`** 调度 → 将工具结果写回消息历史 → 自动多轮直到结束
- **事件**：`OnToolCallsStart`、`OnToolCallInvoke`、`OnToolExecuting`、`OnToolExecuted`、`OnToolCallsFinish`；流式另有 `OnStreamOutput`、`OnStreamOutputCompleted`
- **内置工具**（`Silmoon.AI.Tools`）：`FileTool`、`CommandTool`、`WaitTool`、`MemoryTool`、`DeepThinkTool`、`WorldStateTool`（本机时间/时区等只读快照；对外函数名见各类中常量，如 `Sys_WorldState`、`Wait_Delay`、`DeepThink_Call`）
- **请求开关**：`NativeChatClient.EnableThinking`、`EnableSearch` 会进入 `Request`（具体字段随厂商在 `SetEnableThinking` 中处理）
- **历史**：`ResetHistory`（可选续接正文）、**`RollbackHistory`**（按轮次回滚消息）
- **示例**：`Silmoon.AI.Terminal`（多厂商 JSON + `ContextManagerService` 注入工具）、`Silmoon.AI.HostingTest`、`Silmoon.AI.WinFormTest`（主窗打开测试窗）

`NativeChatClient` 实现 **`IDisposable`**；构造支持 **`disableProxy`**、**`httpRequestTimeoutMilliseconds`**。默认 **`disableProxy` 为 `false`**，对应 `HttpClientHandler.UseProxy = true`（`Proxy = null` 时一般由 **系统代理** 决定）；若要 **禁用代理**，请传入 **`disableProxy: true`**。仅使用无参 **`new SseHttpClient()`** 时则固定 `UseProxy = false`。

## 项目结构

```text
Silmoon.AI.sln
├─ Silmoon.AI/                 # 核心：OpenAI 客户端、模型、ExecuteToolManager、Tools、Prompts
├─ Silmoon.AI.Terminal/
├─ Silmoon.AI.HostingTest/
└─ Silmoon.AI.WinFormTest/
```

- **.NET SDK 10.0**（`net10.0` / `net10.0-windows`）；WinForms 仅 Windows。  
- 许可见 **`LICENSE.txt`**（MIT）。仓库根还有 **`logo.png`** 等解决方案级文件。

## 配置（两种形态）

**Terminal**（`Silmoon.AI.Terminal`）：`defaultModel` + **`modelProviders`**，可选 **`systemPrompt`**、**`tools`** 等，与 `SilmoonConfigureServiceImpl` 一致。

**HostingTest / WinFormTest**：扁平 **`apiUrl`**、**`apiKey`**、**`modelName`**。

勿把真实密钥提交到 Git；可用各项目中的 **`config.local.json` / `config.local.debug.json`** 做本地覆盖（见对应 `.csproj` 的复制规则）。

## 跑示例

```bash
git clone https://github.com/silmoonsone/Silmoon.AI.git
cd Silmoon.AI
dotnet restore
dotnet run --project ./Silmoon.AI.Terminal/Silmoon.AI.Terminal.csproj
dotnet run --project ./Silmoon.AI.HostingTest/Silmoon.AI.HostingTest.csproj
dotnet run --project ./Silmoon.AI.WinFormTest/Silmoon.AI.WinFormTest.csproj
```

Terminal / Hosting 控制台：`@clear` 清历史，`@exit` 退出。

## 工具调度行为（接入前必读）

1. **同一轮里多个 `tool_calls`**：彼此 **`Task` 并行**执行（例如多个 `Wait_Delay` 会叠墙钟时间，见 `WaitTool` 说明）。  
2. **同一 `tool_call` 上多个 `OnToolCallInvoke` 订阅者**：在 `ExecuteToolManager` 内对多个 handler **并行**调用，并写入共享的 `result` 变量，**存在竞态**；若多个 handler 都可能返回非空，应保证**只有一个**会命中该函数名，或自行在应用层合并逻辑。  
3. **`ExecuteToolManager.AddExecuteTool`**：若已注册过同名 **Function**，会拒绝并返回状态说明（避免重复 schema）。  
4. **记忆工具**：仅 **`Memory_ApplyMemory` 且成功**时，客户端可选择不把该条 tool 结果追加进历史（见 `NativeChatClient` 中注释逻辑），避免干扰后续轮次。

## 最小用法（自建控制台 + 工具）

```bash
dotnet new console -n MySilmoonAiDemo -f net10.0
cd MySilmoonAiDemo
dotnet add reference <路径>/Silmoon.AI/Silmoon.AI.csproj
```

```csharp
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.AI.OpenAI;
using Silmoon.Extensions;
using Silmoon.Models;

using var client = new NativeChatClient(
    "https://api.example.com/v1",
    "your-api-key",
    "your-model",
    systemPrompt: "用简短中文回答。");

client.Tools.Add(Tool.Create(
    "get_weather",
    "根据城市名返回一句天气描述（演示）。",
    [new ToolParameterProperty("string", "city", "城市名", null, true)]));

client.OnToolCallInvoke += (p, _) =>
{
    if (p.FunctionName != "get_weather")
        return Task.FromResult<ToolCallResult>(null);

    var city = p.Parameters["city"]?.Value<string>() ?? "";
    return Task.FromResult(ToolCallResult.Create(p, true.ToStateSet($"{city}：晴，25°C（演示）")));
};

var reply = await client.CompletionsAsync("北京天气怎么样？");
Console.WriteLine(reply.Choices[0].Message.Content);
```

流式用 **`CompletionsStreamAsync`**。复杂工具继承 **`ExecuteTool`**，实现 **`OnToolCallInvoke(ToolCallParameter, ToolCallResult)`** 后 **`InjectToolCall(client)`**；也可 **`client.ExecuteToolManager.AddExecuteTool(...)`** 注册 **`IExecuteTool`**。

## 扩展与依赖

- 在 **`Silmoon.AI/Tools`** 增加 `ExecuteTool` 子类，或在外部程序实现 **`IExecuteTool`**。  
- 类库 **NuGet**：`Silmoon`、`Silmoon.Extensions`（版本见 **`Silmoon.AI.csproj`**）。  
- 示例工程另引用 **`Silmoon.Extensions.Hosting`**、`Microsoft.Extensions.Hosting` 等（见各示例 `.csproj`）。  
- JSON 与部分 API 使用 **Newtonsoft.Json**（`JObject` 等）。

## 其它

- 开发辅助：仓库含 **`.vscode`** 下 `tasks.json`、`launch.json`。  
- 更细的协议字段、厂商差异以 **`Models/OpenAI`**、`Request.SetEnableThinking` 与各 **`Tools/*Tool.cs`** 为准。
