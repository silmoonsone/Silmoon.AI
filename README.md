# Silmoon.AI

一个基于 .NET 的轻量 AI 客户端与工具调用（Tool Calling）实验项目，包含：

- 可复用的 OpenAI-Compatible 客户端封装
- 可注入的本地工具系统（命令、文件、等待、记忆、深度思考）
- 多种宿主示例（Console/Terminal、Hosting、WinForms）

项目目标是提供一个易扩展的“模型 + 工具 + 宿主”组合范式，便于你快速构建自己的 AI Agent 原型。

## 当前状态

- 项目处于持续迭代阶段，接口与配置结构可能调整
- 更适合用于学习、原型验证和二次开发
- 生产使用前请补充权限控制、审计日志与测试覆盖

## 功能特性

- OpenAI Compatible 聊天接口封装（流式 + 非流式）
- Tool Calling 事件链（开始/完成钩子）
- 内置工具：
  - `CommandTool`：无状态/有状态终端命令执行
  - `FileTool`：文本文件读写
  - `WaitTool`：定时等待/节流
  - `MemoryTool`：会话压缩记忆与续接
  - `DeepThinkTool`：委托更强模型处理复杂任务
- 模型提供商配置（`providerName/apiUrl/apiKey/models`）
- 多宿主接入示例：
  - `Silmoon.AI.Terminal`：终端交互版
  - `Silmoon.AI.HostingTest`：Hosted Service 测试版
  - `Silmoon.AI.WinFormTest`：Windows Forms 测试版

## 项目结构

```text
Silmoon.AI.sln
├─ Silmoon.AI/                # 核心模型与协议实体（Request/Response/Tool 等）
├─ Silmoon.AI.Client/         # OpenAI-Compatible 客户端实现（NativeChatClient/SSE）
├─ Silmoon.AI.Tools/          # 工具实现（Command/File/Wait/Memory/DeepThink）
├─ Silmoon.AI.Terminal/       # 终端宿主示例（本地 MCP 风格工具注入）
├─ Silmoon.AI.HostingTest/    # Hosting 场景示例
└─ Silmoon.AI.WinFormTest/    # WinForms 场景示例
```

## 运行环境

- .NET SDK 10.0（项目当前 `TargetFramework` 为 `net10.0` / `net10.0-windows`）
- Windows / Linux / macOS（WinForms 仅 Windows）

## 快速开始

### 1) 克隆并还原

```bash
git clone https://github.com/<your-name>/Silmoon.AI.git
cd Silmoon.AI
dotnet restore
```

> 请将 `<your-name>` 替换为实际仓库拥有者。

### 2) 配置模型参数

当前示例使用 `config.json` / `config.debug.json` 读取配置，典型字段如下：

```json
{
  "defaultModel": {
    "defaultProvider": "aliyun",
    "defaultModelName": "qwen3-max"
  },
  "modelProviders": [
    {
      "providerName": "aliyun",
      "apiUrl": "https://dashscope.aliyuncs.com/compatible-mode/v1",
      "apiKey": "sk-***",
      "models": [
        { "name": "qwen3.6-plus", "enable": true }
      ]
    }
  ]
}
```

> 建议：请使用你自己的密钥，并在公开仓库前确保密钥已轮换。

### 3) 运行示例

运行终端示例：

```bash
dotnet run --project ./Silmoon.AI.Terminal/Silmoon.AI.Terminal.csproj
```

运行 Hosting 示例：

```bash
dotnet run --project ./Silmoon.AI.HostingTest/Silmoon.AI.HostingTest.csproj
```

运行 WinForms 示例（Windows）：

```bash
dotnet run --project ./Silmoon.AI.WinFormTest/Silmoon.AI.WinFormTest.csproj
```

## 交互说明（Terminal/Hosting）

- 普通输入：发送用户消息给模型
- 内置命令：
  - `@clear`：清空消息历史
  - `@exit`：退出程序

## 常见问题

### 1) 为什么需要 .NET 10.0？

当前各项目 `TargetFramework` 使用 `net10.0` / `net10.0-windows`，请使用对应 SDK 构建。若你希望兼容更低版本，可自行下调目标框架并验证依赖兼容性。

### 2) 如何接入其他 OpenAI-Compatible 服务？

在配置中增加或修改 `modelProviders`，并设置 `apiUrl`、`apiKey`、`models`，再把 `defaultModel` 指向目标提供商和模型即可。

### 3) 工具调用失败通常先看哪里？

优先检查：模型是否返回 `tool_calls`、工具 schema 参数是否匹配、宿主是否执行了工具注入（`InjectToolCall`）。

## 扩展方式

### 新增工具

1. 在 `Silmoon.AI.Tools` 中继承 `ExecuteTool`
2. 实现 `GetTools()` 定义工具 schema
3. 实现 `OnToolCallInvoke(...)` 处理调用逻辑
4. 在宿主项目中注入（`InjectToolCall(...)`）

### 自定义模型提供商

- 在配置中新增 `modelProviders` 项
- 设置 `defaultModel.defaultProvider` 与 `defaultModel.defaultModelName`

## 安全与开源提示

- 不要提交真实 `apiKey`、token、证书、私钥
- 发布前请轮换曾在本地/历史中出现过的密钥
- 对任何可能含敏感信息的配置与日志进行二次检查

## 路线方向（计划）

- 更完善的工具权限与沙箱控制
- 更细粒度的会话记忆管理策略
- 更多模型提供商适配与统一抽象
- 更完整的测试覆盖与示例文档

## 贡献

欢迎 Issue / PR：

1. Fork 本仓库
2. 创建特性分支
3. 提交变更并附说明
4. 发起 Pull Request

## License

当前仓库尚未在本 README 明确许可证。  
建议尽快补充 `LICENSE` 文件（如 MIT/Apache-2.0），以便开源协作与二次分发。

## 文档声明

本项目 `README.md` 由 AI 生成初稿，并经人工审阅与调整。

---

如果这个项目对你有帮助，欢迎点个 Star。  
也欢迎基于本项目构建你自己的 Agent 工作流。