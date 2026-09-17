# RevitAgentMcp

把 RevitAgent 的核心执行能力——**不打开 Revit 界面，编译并执行调用 Revit API 的 C# 代码，返回 JSON 结果**——封装为本地 MCP (Model Context Protocol) server，供 Claude Desktop、Claude Code、WorkBuddy 等支持 MCP 的客户端使用。

接入后，客户端的 AI 按工具描述里的契约自己生成 C# 代码，调用本 server 无头执行。**server 侧不需要配置任何大模型密钥**。

## 暴露的工具

| 工具 | 作用 |
|---|---|
| `run_revit_code` | 核心：编译执行一段实现 `IRevitDynamicCommand` 的 C#，多模型顺序跑在同一会话，返回结果信封（含 `Error.Stage`，compile 错误可让 AI 自纠重试） |
| `list_models` | 列出目录顶层 `.rvt` 文件 |
| `detect_revit_versions` | 探测 2019–2022 执行器是否就位及其 exe 路径 |

## 前提

- Windows x64，本机装有对应版本的 Revit（2019–2022）。
- 构建产物 `RevitAgentMcp.exe` 旁边有 `executor-<版本>\` 文件夹（`dotnet build` 自动暂存四版执行器）。

## 构建

```powershell
dotnet build RevitAgentMcp\RevitAgentMcp.csproj -c Release
```

## 客户端接入

### Claude Desktop

`claude_desktop_config.json`（设置 → 开发者 → 编辑配置）：

```json
{
  "mcpServers": {
    "revit-agent": {
      "command": "C:\\path\\to\\RevitAgentMcp.exe"
    }
  }
}
```

### Claude Code

```powershell
claude mcp add revit-agent -- "C:\path\to\RevitAgentMcp.exe"
```

### 其他支持 stdio MCP 的客户端（WorkBuddy 等）

在客户端的 MCP / 工具设置里填同样的命令（command 指向 `RevitAgentMcp.exe`，无需参数）。

## 环境变量

- `REVIT_AGENT_EXECUTOR_ROOT`：执行器不在默认布局时，指向执行器根目录（内含 `executor-<版本>\...` 或各版本 exe）。

## 实现说明

- 复用 `RevitAgent.Cli` 的 `RunRevitCodeTool` / `ExecutorLocator`（源码链接编译，单一事实来源）：拉起 net48 执行器子进程、结果走文件不走 stdout、SemaphoreSlim 串行闸门。
- MCP stdio 协议独占 stdout，因此全部日志走 stderr；执行结果本就经由文件回传，不会污染协议流。
- 长调用（首次约 20 秒 Revit 引擎初始化）期间客户端会等待；并发调用在闸门后排队。
- 安全边界：本 server 在用户本机执行任意 C#（等同让 AI 跑本地脚本），仅适合用户自行配置的 stdio 本地接入，不要暴露为网络服务。
