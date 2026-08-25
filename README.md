<p align="center">
  <img src="RevitAgent.Gui/Assets/RevitAgent.png" width="112" alt="RevitAgent logo">
</p>

# RevitAgent

> Windows 桌面智能工作台、命令行工具与在线演示：<https://jiangkai002.github.io/revit-agent-cli/>

RevitAgent 是一个面向 Revit 模型的 AI 桌面应用与 CLI。你用自然语言描述需求（比如“这个模型里有多少个房间”），它把需求交给大语言模型，现场生成调用 Revit API 的 C# 代码，在不打开 Revit 界面的情况下编译执行，最后用自然语言或 CSV 表格交付结果。

从理解需求到生成代码、编译、跑、汇总，整条链路没有人工写代码的环节。

## 它解决什么问题

Revit 的二次开发接口跑在 .NET Framework 4.8 上，核心是原生的 `RevitNET.dll`，没法被一个现代 .NET 进程直接承载。常规做法是写个插件装进 Revit、手动打开 Revit、点按钮——对自动化不友好。

RevitAgent 用桌面端、智能体宿主与执行器协作绕开这个限制：

- `RevitAgent.Gui`（.NET 10 + WPF）：提供符合 Windows Fluent UI 的桌面界面、亮暗主题、模型选择、技能管理和设置。
- `revit-agent`（.NET 10）：CLI 与智能体核心，负责和大模型对话、生成代码、拉起执行器、读结果、回答你。
- `RevitAgent.Executor.<版本>.exe`（.NET Framework 4.8，x64）：负责无头初始化 Revit、打开模型副本、运行那段代码、关掉、退出。

两个进程之间通过文件交换结果，不走 stdout。原因是 Revit 退出阶段往控制台喷的噪声会污染任何走标准输出的结果——踩过这个坑之后改成只信文件。

## 一条请求的生命周期

1. 你在桌面 GUI 或 CLI 中输入需求。
2. `ChatClientAgent`（基于 Microsoft Agent Framework）把需求转成一段实现 `IRevitDynamicCommand` 的 C# 类。
3. CLI 把源码写到临时文件，拉起对应版本的执行器。
4. 执行器用 Roslyn 编译这段代码，注入一次 Revit，打开模型副本，执行命令拿到返回值，关模型，退出注入。
5. 执行器把结果写成 JSON 信封到结果文件。
6. 智能体读回结果，喂给大模型，由它用你的语言总结，并实时呈现在 GUI 或终端中。

代码是现场生成的，不是预置脚本。Roslyn 用 `LanguageVersion.Latest`，所以生成出来的 C# 可以用现代写法——`?.`、`$""`、`nameof`、模式匹配、record 都行。

## Windows 桌面 GUI

WPF 桌面端使用 Windows Fluent UI 视觉语言，并针对长时间模型分析做了专门设计：

- 自动跟随系统亮色/暗色，也可在设置中手动切换。
- 对话记录独立滚动，输入区固定在窗口底部；鼠标位于对话内容任意位置都可滚动。
- 推理过程、工具调用和大型工具结果可折叠，Markdown 表格使用适配主题的样式。
- 支持选择单个或多个 `.rvt` 模型，并在会话中切换 Revit 版本。
- 技能页面统一管理内置技能、URL 安装和本地 ZIP 安装。
- 使用 Windows 常用中文字体，并提供完整的窗口拖动、最小化、最大化与关闭操作。

## 批量执行

一个目录下有多个 `.rvt` 时可以一次性都跑掉。不指定 `--model-path` 时，工具默认扫描当前目录下的所有 `.rvt`；指定一个目录也行。所有模型在同一个 Revit 会话里顺序执行——引擎只初始化一次（约 20 秒），而不是每跑一个模型就重新启动一次 Revit。10 个模型大约就是一次初始化加 10 次单模型执行，而不是 10 次完整启动。

返回的是一个多模型信封：每个模型一条结果（成功/失败加数据），外加一个汇总（总数、成功数、失败数）。某个模型打不开（比如版本不对或文件损坏）只影响那一条，其余照跑。

## 支持的 Revit 版本

执行器按版本各编一个：2019、2020、2021、2022 都能编译，也都能无头跑。2022 及以上走 `Autodesk.Revit.Product.Initialize_ForAutodeskInternalUseOnly`；2019–2021 走 `Product.Init` 的回退路径。目标机器上得装着对应版本的 Revit——执行器通过注册表定位安装路径，运行时从那里加载真正的 `RevitAPI.dll`。包里不含 Revit 本体。

## 其余能力

- 技能（skills）：把某个领域的约定、清单、代码模板打包成一个 skill，按需加载——目录里只放名字和一句话描述，需要时再拉全文，不占基础提示。**自带 5 个示例技能（房间审计、建筑运维房间合规、MEP 设备编号 / 族类别 / 连接器检查），随包只读内置，开箱即用**；GUI 支持 URL 和本地 ZIP 安装，CLI 可用 `revit-agent skill install <url|zip路径>` 安装，`skill list / show / remove / path` 管理。
- 交互选模型：`chat` 多轮会话里输入 `/rvt`，弹出 Claude 式多选列表（↑↓ 移动、Space 切换、A 全选、Enter 确认、Esc 取消），后续提问只作用于勾选的模型；`/rvt all` 恢复全部。也支持 `/rvt 1`、`/rvt 文件名` 等文本快捷方式。
- 导出 CSV：模型参数之类的表格数据可以直接导成带 BOM 的 UTF-8 CSV，Excel 能直接开。相对路径默认写入第一个模型所在目录；多模型时首列是模型文件名，各模型的行拼在一起。
- 进度显示：跑的时候在终端用灰色实时显示模型的推理、工具调用、返回结果。那段约 20 秒的 Revit 启动空窗会有动画过渡。

## 用之前要准备什么

- Windows x64。Revit 的原生组件只有 x64。
- 装好对应版本的 Revit（2019–2022 任选）。
- 跑 `run`/`chat`（让大模型生成代码）要自己配一个大模型的 API 密钥，兼容任何 OpenAI 兼容后端。密钥只读环境变量，不写进配置文件。
- 构建需要 .NET 10 SDK。

## 快速开始

### Windows 安装包

项目可以生成包含 GUI、CLI、.NET 10/WPF 运行时、四套 Revit 执行器和内置技能的单文件 MSI，目标用户不需要额外安装 .NET。构建与发布步骤请参阅 [Windows 打包与交付指南](PACKAGING.md)。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-msi.ps1
```

安装完成后，从开始菜单启动 **RevitAgent**。

### 从源码运行 GUI

```powershell
dotnet run --project .\RevitAgent.Gui\RevitAgent.Gui.csproj
```

### 构建 CLI

构建（会同时编出 2019–2022 四个版本的执行器并暂存到 CLI 输出旁）：

```
.\build.ps1
```

配置（密钥只走环境变量，不进 config.json）：

```
revit-agent config init
revit-agent config set baseurl https://api.openai.com/v1
revit-agent config set model gpt-4o
setx REVIT_AGENT_API_KEY "sk-..."
```

跑：

```
# 让大模型按需求生成并执行代码
revit-agent run "检查模型中有没有房间" --version 2022 --model-path "D:\Models\MyModel.rvt"

# 多轮交互（会话里输 /rvt 弹出模型多选，只对勾选的模型提问）
revit-agent chat --model-path "D:\Models"

# 列出技能（自带 5 个随包只读技能，标 [内置]）
revit-agent skill list

# 从 URL 或本地 ZIP 安装技能
revit-agent skill install "D:\Skills\my-revit-skill.zip"

# 直接跑一段手写代码（不经过大模型、免 API 密钥，用来测试执行链路）
revit-agent exec samples\RoomCheck.cs --version 2022 --model-path "D:\Models\MyModel.rvt"

# 直接跑某个技能模板（同样不经过大模型、免密钥）
revit-agent exec samples\skills\room-audit\templates\RoomCheck.cs --version 2022 --model-path "D:\Models\MyModel.rvt"

# 不指定模型路径，自动扫当前目录下所有 .rvt，一个会话顺序跑完
cd D:\Models
revit-agent exec samples\RoomCheck.cs --version 2022
```

## 生成代码的契约

工具让大模型生成的代码必须长这样——一个实现了 `IRevitDynamicCommand` 的类，`Execute` 接收一个已打开的 `Document`，返回能序列化成 JSON 的纯数据：

```csharp
public sealed class DynamicCommand : RevitAgent.DynamicCode.IRevitDynamicCommand
{
    public object Execute(Autodesk.Revit.DB.Document document)
    {
        return new { /* 只放基本类型、数组、匿名对象 */ };
    }
}
```

不要直接返回 Revit 的 `Element`、`Document`、`XYZ` 这些——它们没法序列化，得把需要的标量字段（Id、Name、Area 之类）抠出来放进普通对象。

## 一些限制

- 一次只跑一个 Revit 会话（串行）。
- 目录扫描默认只扫顶层，不递归子目录。
- 一个目录里混着不同 Revit 版本的模型时，非目标版本的模型会逐个失败（记在那条模型的错误里），不影响其余模型。
- 大批量跑如果遇到 Revit 内存退化，目前没有自动分批重启的机制。
