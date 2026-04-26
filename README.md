# Silmoon.AI

基于 .NET 的轻量库：连接 **OpenAI-Compatible** 聊天接口，支持 **工具调用（Function Calling）**，自带常用本地工具与示例程序。

**说明：本 README 由 AI 生成，请以仓库源码为准。**

## 能做什么

- 兼容 OpenAI 的聊天接口（普通请求 + SSE 流式）
- 工具调用：声明函数 → 模型发起 `tool_calls` → 你执行并回传 → 库内多轮直到模型结束
- 内置工具：文件读写、终端命令（含有状态 Shell）、等待、记忆续接、深度推理等
- 多厂商：JSON 配置 `apiUrl`、密钥、模型列表
- 示例：`Silmoon.AI.Terminal`、`Silmoon.AI.HostingTest`、`Silmoon.AI.WinFormTest`

## 项目结构

核心只有一个类库 **`Silmoon.AI`**（客户端在 `Silmoon.AI.OpenAI`，工具在 `Silmoon.AI.Tools`）。

```text
Silmoon.AI.sln
├─ Silmoon.AI/
├─ Silmoon.AI.Terminal/
├─ Silmoon.AI.HostingTest/
└─ Silmoon.AI.WinFormTest/
```

需要 **.NET SDK 10.0**；WinForms 示例仅 Windows。

## 跑仓库里的示例

```bash
git clone https://github.com/silmoonsone/Silmoon.AI.git
cd Silmoon.AI
dotnet restore
```

在各项目的 `config.json` / `config.debug.json`（或本地 `config.local*.json`）里填写 **apiUrl、apiKey、模型**，勿把密钥提交到 Git。

```bash
dotnet run --project ./Silmoon.AI.Terminal/Silmoon.AI.Terminal.csproj
dotnet run --project ./Silmoon.AI.HostingTest/Silmoon.AI.HostingTest.csproj
dotnet run --project ./Silmoon.AI.WinFormTest/Silmoon.AI.WinFormTest.csproj
```

Terminal / Hosting：`@clear` 清历史，`@exit` 退出。

## 最小用法（自建控制台 + 工具）

```bash
dotnet new console -n MySilmoonAiDemo -f net10.0
cd MySilmoonAiDemo
dotnet add reference <路径>/Silmoon.AI/Silmoon.AI.csproj
```

`Program.cs` 示例（替换 `apiUrl` / `apiKey` / `modelName`）：

```csharp
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.AI.OpenAI;
using Silmoon.Extensions;
using Silmoon.Models;

var client = new NativeChatClient(
    "https://api.example.com/v1",
    "your-api-key",
    "your-model",
    systemPrompt: "用简短中文回答。");

client.Tools.Add(Tool.Create(
    "get_weather",
    "根据城市名返回一句天气描述（演示）。",
    [new ToolParameterProperty("string", "city", "城市名", null, true)]));

client.OnToolCallStart += (toolCallParameters, _) =>
{
    var list = new List<ToolCallResult>();
    foreach (var p in toolCallParameters)
    {
        if (p.FunctionName != "get_weather") continue;
        var city = p.Parameters["city"]?.Value<string>() ?? "";
        list.Add(ToolCallResult.Create(p, true.ToStateSet($"{city}：晴，25°C（演示）")));
    }
    return Task.FromResult(list);
};

var reply = await client.CompletionsAsync("北京天气怎么样？");
Console.WriteLine(reply.Choices[0].Message.Content);
```

流式用 `CompletionsStreamAsync`，可参考示例里的 `ClientService`。复杂工具可继承 `Silmoon.AI.Tools.ExecuteTool` 并 `InjectToolCall(client)`。

## 扩展库内工具

在 `Silmoon.AI/Tools` 继承 `ExecuteTool`，实现 `GetTools()` 与 `OnToolCallInvoke`，宿主里 `InjectToolCall` 即可。多厂商在 JSON 里加 `modelProviders` 并设 `defaultModel`。

其他项目引用：添加对 `Silmoon.AI.csproj` 的项目引用。
