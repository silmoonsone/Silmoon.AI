# Silmoon.AI

基于 .NET 的轻量库：对接 **OpenAI-Compatible** 聊天接口，支持 **工具调用（Function Calling）**，并附带一批可直接复用的本地工具与三个示例程序，用来搭聊天机器人或简单 Agent 都很合适。

**说明：本 README 由 AI 生成，请以仓库源码为准。**

## 这是什么

| 能力 | 说明 |
|------|------|
| 聊天 | 支持普通请求与 **SSE 流式**；可按模型厂商调整思考、联网等请求字段（见 `Request` 与相关扩展）。 |
| 工具 | 把函数 schema 交给模型，由模型发起 `tool_calls`，你在 **`OnToolCallInvoke`** 等回调里执行逻辑，库负责把结果写回会话并继续对话。 |
| 内置工具 | 终端命令（含持久 Shell）、文件读写、定时等待、会话记忆压缩/续接、本机时间与运行环境快照、委托子任务等，代码在 **`Silmoon.AI/Tools`**。 |
| 示例 | **Terminal**（多厂商配置 + 工具注入）、**HostingTest**（控制台宿主）、**WinFormTest**（仅 Windows）。 |

客户端主类型：**`Silmoon.AI.OpenAI.NativeChatClient`**；工具调度：**`ExecuteToolManager`**。消息与协议类型在 **`Silmoon.AI/Models/OpenAI`**（含多种消息形态，适配文本与更复杂载荷）。

## 仓库里有什么

```text
Silmoon.AI/              ← 核心类库（客户端、模型、工具）
Silmoon.AI.Terminal/     ← 终端示例
Silmoon.AI.HostingTest/  ← Hosting 控制台示例
Silmoon.AI.WinFormTest/  ← WinForms 示例
```

- 需要 **.NET SDK 10.0**（`net10.0` / `net10.0-windows`）。  
- 许可：**`LICENSE.txt`**（MIT）。

## 跑起来

```bash
git clone https://github.com/silmoonsone/Silmoon.AI.git
cd Silmoon.AI
dotnet restore
```

在 **`config.json` / `config.debug.json`** 里填好 **API 地址、密钥、模型**；可用 **`config.local*.json`** 做本机覆盖（勿把密钥提交到 Git）。

```bash
dotnet run --project ./Silmoon.AI.Terminal/Silmoon.AI.Terminal.csproj
dotnet run --project ./Silmoon.AI.HostingTest/Silmoon.AI.HostingTest.csproj
dotnet run --project ./Silmoon.AI.WinFormTest/Silmoon.AI.WinFormTest.csproj
```

- **Terminal**：多提供商 JSON（`defaultModel`、`modelProviders` 等），详见该项目的配置与 `SilmoonConfigureServiceImpl`。  
- **HostingTest / WinFormTest**：扁平 **`apiUrl`、`apiKey`、`modelName`**。

在 Terminal / Hosting 控制台中：`@clear` 清空历史，`@exit` 退出。

## 在你自己的程序里用最简方式调用（含工具）

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

流式输出请用 **`CompletionsStreamAsync`**；需要内置整条工具链时，可继承 **`ExecuteTool`** 并 **`InjectToolCall(client)`**，或直接操作 **`client.ExecuteToolManager`**。更细的回调与并发约定见 **`NativeChatClient`**、**`ExecuteToolManager`** 与各 Tool 实现。

## 依赖

核心项目引用 **`Silmoon`、`Silmoon.Extensions`**（版本见 **`Silmoon.AI.csproj`**）；示例另含 Hosting、JSON 等依赖，以各 **`.csproj`** 为准。
