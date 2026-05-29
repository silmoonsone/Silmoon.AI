# Silmoon.AI

基于 .NET 的轻量库：对接 **OpenAI-Compatible** 聊天接口，支持 **工具调用（Function Calling）**，附带可复用的本地工具与三个示例程序，用于搭建聊天助手或简单 Agent。

**说明：本 README 由 AI 生成，请以仓库源码为准。**

## 这是什么

| 能力 | 说明 |
|------|------|
| 聊天 | 普通请求与 **SSE 流式**；流式回合的 **`Result`** 可附带 **`Usage`**（token 用量，视厂商返回字段而定）；可按厂商配置思考、联网等（`EnableThinking`、`EnableSearch` 等，见 `Request`）。 |
| 推理链 | 支持将模型的 **思考/推理内容**（如 `reasoning_content`）随助手消息写入历史，流式输出时可与正文分开展示（示例见 Terminal 的 `ClientService`）。 |
| 消息 | 会话历史为 **`IMessage`**，除普通文本（`MessageContent`）外，还可使用 JSON、多段内容、图文片段等形态（见 `Models/OpenAI/Models/Message.cs`）。 |
| 工具 | 向模型注册函数 schema；模型发起 `tool_calls` 后由 **`ExecuteToolManager`** 调度，你在 **`OnToolCallInvoke`** 等事件中实现逻辑，库负责写回结果并继续对话。 |
| 内置工具 | 文件读写与按行读取、终端命令（一次性 / 持久 Shell）、等待、会话记忆压缩与续接、本机环境快照、委托子任务等，位于 **`Silmoon.AI/Tools`**。 |
| 会话 | **`ResetHistory`** 清空或续接记忆；**`RollbackHistory`** 按轮次回滚。 |
| 示例 | **Terminal**（多厂商 JSON + `ContextManagerService` 注入工具）、**HostingTest**、**WinFormTest**（Windows）。 |

核心入口：**`Silmoon.AI.OpenAI.NativeChatClient`**。协议与消息类型在 **`Silmoon.AI/Models/OpenAI`**。

## 仓库里有什么

```text
Silmoon.AI/              ← 核心类库（客户端、模型、ExecuteToolManager、Tools）
Silmoon.AI.Terminal/     ← 终端示例
Silmoon.AI.HostingTest/  ← Hosting 控制台示例
Silmoon.AI.WinFormTest/  ← WinForms 示例
```

- **.NET SDK 10.0**（`net10.0` / `net10.0-windows`）  
- **MIT**（`LICENSE.txt`）

## 跑起来

```bash
git clone https://github.com/silmoonsone/Silmoon.AI.git
cd Silmoon.AI
dotnet restore
```

在 **`config.json` / `config.debug.json`** 中配置 **API 地址、密钥、模型**；本地覆盖可用 **`config.local*.json`**（勿提交密钥）。

```bash
dotnet run --project ./Silmoon.AI.Terminal/Silmoon.AI.Terminal.csproj
dotnet run --project ./Silmoon.AI.HostingTest/Silmoon.AI.HostingTest.csproj
dotnet run --project ./Silmoon.AI.WinFormTest/Silmoon.AI.WinFormTest.csproj
```

- **Terminal**：`defaultModel`、`modelProviders`（可选 `systemPrompt`、`providerDescription` 等）。  
- **HostingTest / WinFormTest**：`apiUrl`、`apiKey`、`modelName`。

Terminal / Hosting 控制台：`@clear` 清历史，`@exit` 退出。

## 在你自己的程序里调用（含工具）

```bash
dotnet new console -n MySilmoonAiDemo -f net10.0
cd MySilmoonAiDemo
dotnet add reference <你的路径>/Silmoon.AI/Silmoon.AI.csproj
```

```csharp
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
    "根据城市名返回一句天气（演示）。",
    [new ToolParameterProperty("string", "city", "城市名", null, true)]));

client.OnToolCallInvoke += (p, _) =>
{
    if (p.FunctionName != "get_weather")
        return Task.FromResult<ToolCallResult>(null);

    var city = p.Parameters["city"]?.Value<string>() ?? "";
    return Task.FromResult(ToolCallResult.Create(p, true.ToStateSet<object>($"{city}：晴，25°C（演示）")));
};

var reply = await client.CompletionsAsync("北京天气怎么样？");
Console.WriteLine(reply.Choices[0].Message.Content);
```

- 流式：**`CompletionsStreamAsync`**，可订阅 **`OnStreamOutput`** / **`OnStreamOutputCompleted`**。  
- 内置工具链：继承 **`ExecuteTool`** 并 **`InjectToolCall(client)`**，或使用 **`ExecuteToolManager.AddExecuteTool`**。  
- 工具生命周期还可订阅 **`OnToolCallsStart`**、**`OnToolExecuting`**、**`OnToolExecuted`**、**`OnToolCallsFinish`** 等（示例见 Terminal / HostingTest）。

更完整的用法与边界行为以 **`NativeChatClient`**、**`ExecuteToolManager`** 及各 **`Tools/*Tool.cs`** 为准。

## 依赖

核心：**`Silmoon`、`Silmoon.Extensions`**（版本见 **`Silmoon.AI.csproj`**）。示例项目另引用 Hosting、JSON 等包，见各 **`.csproj`**。
