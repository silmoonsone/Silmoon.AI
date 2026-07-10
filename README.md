# Silmoon.AI

Silmoon.AI 是一个基于 .NET 的轻量 AI Native API 客户端库，面向聊天、流式输出、工具调用和简单 Agent 场景。

当前版本把不同厂商和不同原生接口抽象到统一的 `INativeClient` 上，同时保留各接口的原生客户端，便于单独调试协议行为。

## 主要能力

| 能力 | 说明 |
|------|------|
| 统一客户端 | 通过 `NativeClientFactory.Create(...)` 根据 `ModelProvider.ApiKind` 创建统一的 `INativeClient`。 |
| OpenAI Chat Completions | `ChatClient` 支持 OpenAI-Compatible `/chat/completions`，包含普通请求、SSE 流式、工具调用和部分厂商的 thinking 字段适配。 |
| Anthropic Messages | `AnthropicClient` 支持 Anthropic Messages 风格接口，并把返回结果适配为库内通用的 Chat Completions 结果模型。 |
| OpenAI Responses | `ResponsesClient` 已预留为 Responses API 实现入口，便于后续扩展，不影响现有统一接口。 |
| SSE 传输 | `SseHttpClient` 已从 Chat Completions 实现中抽出，可作为独立的 SSE HTTP 请求封装复用。 |
| 工具调用 | 使用 `Tool` 声明函数 schema，通过 `ExecuteToolManager` 执行模型返回的 `tool_calls`，并自动把工具结果写回历史继续对话。 |
| 内置工具 | `FileTool`、`CommandTool`、`WaitTool`、`WorldStateTool`、`MemoryTool` 等位于 `Silmoon.AI/Tools`。 |
| 会话历史 | `ClearHistory(...)`、`RollbackHistory(...)` 用于清理或回滚消息历史，System Prompt 会按客户端规则保留。 |
| 兼容旧历史 | `NativeApiJson` 内置反序列化兼容绑定，可读取旧命名空间里的历史 `$type`。以后旧历史淘汰后这部分可以移除。 |

## NativeApiKind

`ModelProvider.ApiKind` 用于选择底层原生接口。

| 值 | 客户端 | 状态 |
|----|--------|------|
| `Chat` | `ChatClient` | 当前主力实现，适配 OpenAI-Compatible 厂商。 |
| `Authropic` | `AnthropicClient` | 已实现，当前主要用于 DeepSeek Anthropic 兼容接口测试。 |
| `Responses` | `ResponsesClient` | 预留实现入口，后续完善 Responses API。 |

统一创建方式：

```csharp
using Silmoon.AI;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;

var provider = new ModelProvider
{
    ProviderName = "deepseek",
    ApiUrl = "https://api.example.com",
    ApiKey = "sk-***",
    ApiKind = NativeApiKind.Authropic,
    AnthropicVersion = "2023-06-01",
    Models = [new Model { Name = "deepseek-chat" }]
};

using INativeClient client = NativeClientFactory.Create(
    provider,
    modelName: "deepseek-chat",
    systemPrompt: "你是一个简洁的中文助手。");
```

## 项目结构

```text
Silmoon.AI/                         核心类库
  Anthropic/                        Anthropic Messages 原生客户端和模型
  OpenAI/                           OpenAI Chat Completions / Responses 原生客户端和模型
  Interfaces/                       INativeClient 等公共接口
  Models/                           跨接口共享模型
  Tools/                            内置工具
  SseHttpClient.cs                  独立 SSE HTTP 客户端封装

Silmoon.AI.HostingTest/             统一 INativeClient 调用示例
Silmoon.AI.ChatCompletionsTest/     OpenAI Chat Completions 原生客户端测试
Silmoon.AI.AuthropicTest/           Anthropic Messages 原生客户端测试
Silmoon.AI.Terminal/                终端示例
Silmoon.AI.WinFormTest/             WinForms 示例
```

## 配置

示例项目使用 `config.json`、`config.debug.json` 作为默认配置，并支持 `config.local.json`、`config.local.debug.json` 作为本机覆盖。带 `local` 的配置用于密钥和私有地址，不应提交到 Git。

统一配置示例：

```json
{
  "apiUrl": "https://api.example.com",
  "apiKey": "sk-***",
  "providerName": "deepseek",
  "modelName": "deepseek-chat",
  "apiKind": "Authropic",
  "anthropicVersion": "2023-06-01"
}
```

OpenAI-Compatible Chat Completions 示例：

```json
{
  "apiUrl": "https://dashscope.aliyuncs.com/compatible-mode/v1",
  "apiKey": "sk-***",
  "providerName": "aliyun",
  "modelName": "qwen3.6-plus",
  "apiKind": "Chat"
}
```

`providerName` 不只是展示名，部分兼容厂商的请求体差异会用它识别。

## 运行示例

```bash
dotnet restore
dotnet run --project ./Silmoon.AI.HostingTest/Silmoon.AI.HostingTest.csproj
dotnet run --project ./Silmoon.AI.ChatCompletionsTest/Silmoon.AI.ChatCompletionsTest.csproj
dotnet run --project ./Silmoon.AI.AuthropicTest/Silmoon.AI.AuthropicTest.csproj
```

控制台示例常用命令：

| 命令 | 作用 |
|------|------|
| `@clear` | 清空当前会话历史。 |
| `@exit` | 退出程序。 |
| `@stream` | Anthropic 测试项目切换到流式模式。 |
| `@nostream` | Anthropic 测试项目切换到非流式模式。 |

## 最小调用示例

```csharp
using Silmoon.AI;
using Silmoon.AI.Interfaces;
using Silmoon.AI.Models;
using Silmoon.AI.OpenAI.Models;
using Silmoon.AI.OpenAI.Models.Enums;
using Silmoon.Models;

var provider = new ModelProvider
{
    ProviderName = "aliyun",
    ApiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
    ApiKey = "sk-***",
    ApiKind = NativeApiKind.Chat,
    Models = [new Model { Name = "qwen3.6-plus" }]
};

using INativeClient client = NativeClientFactory.Create(
    provider,
    modelName: "qwen3.6-plus",
    systemPrompt: "用简短中文回答。");

var response = await client.CompletionsAsync("你好，简单介绍一下 Silmoon.AI。");
Console.WriteLine(response.Choices.FirstOrDefault()?.Message?.Content);
```

流式调用：

```csharp
List<ChatCompletionsChunk> chunks = [];
await foreach (var state in client.CompletionsStreamAsync("写一句短诗。", chunks))
{
    if (!state.State)
    {
        Console.WriteLine(state.Message);
        continue;
    }

    foreach (var choice in state.Data.Choices)
        Console.Write(choice.Delta?.Content);
}
```

## 工具调用示例

```csharp
using Newtonsoft.Json.Linq;
using Silmoon.AI.Models;
using Silmoon.Extensions;
using Silmoon.Models;

client.Tools.Add(Tool.Create(
    "Test_GetSecretCode",
    "Return a fixed test code.",
    []));

client.OnToolCallInvoke += (parameter, currentResult) =>
{
    if (parameter.FunctionName != "Test_GetSecretCode")
        return Task.FromResult(currentResult);

    return Task.FromResult(
        ToolCallResult.Create(parameter, true.ToStateSet<object>("TOOL_OK")));
};
```

内置工具可直接注入：

```csharp
new WaitTool().InjectToolCall(client);
new WorldStateTool().InjectToolCall(client);
new MemoryTool(client).InjectToolCall(client);
```

`FileTool` 和 `CommandTool` 能访问文件系统或执行命令，实际产品中应按宿主环境做好权限边界。

## 模型与命名空间

当前模型按接口实现分组：

```text
Silmoon.AI/OpenAI/Models
Silmoon.AI/Anthropic/Models
Silmoon.AI/Models
```

`Silmoon.AI.Models` 放跨接口共享类型，例如 `ModelProvider`、`NativeApiKind`、`ToolCallParameter`、`ToolCallResult`、`Usage`。OpenAI Chat Completions 专属类型放在 `Silmoon.AI.OpenAI.Models` 下。

旧的 `Silmoon.AI.Models.OpenAI.Models.*` 类型名已迁移，`NativeApiJson` 目前保留历史兼容。

## 构建

```bash
dotnet build ./Silmoon.AI.sln
```

当前目标框架为 .NET 10，Windows 示例项目需要 Windows 桌面相关 SDK。

## 依赖与许可

核心依赖见 `Silmoon.AI/Silmoon.AI.csproj`，示例项目的 Hosting、WinForms 等依赖见各自 `.csproj`。

本项目使用 MIT License，见 `LICENSE.txt`。
