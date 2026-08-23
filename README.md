# RevitAgent

RevitAgent 是一个命令行工具：你用自然语言描述一个关于 Revit 模型的需求（比如"这个模型里有多少个房间"），它把需求交给大语言模型，让模型生成一段调用 Revit API 的 C# 代码，然后在不开 Revit 界面的情况下编译并执行这段代码，最后把结果用自然语言回答给你。

从理解需求到生成代码、编译、跑、汇总，整条链路没有人工写代码的环节。

## 它解决什么问题

Revit 的二次开发接口跑在 .NET Framework 4.8 上，核心是原生的 `RevitNET.dll`，没法被一个现代 .NET 进程直接承载。常规做法是写个插件装进 Revit、手动打开 Revit、点按钮——对自动化不友好。

RevitAgent 用双进程绕开这个限制：

- `revit-agent`（.NET 10）：负责和大模型对话、生成代码、拉起执行器、读结果、回答你。
- `RevitAgent.Executor.<版本>.exe`（.NET Framework 4.8，x64）：负责无头初始化 Revit、打开模型副本、运行那段代码、关掉、退出。

两个进程之间通过文件交换结果，不走 stdout。原因是 Revit 退出阶段往控制台喷的噪声会污染任何走标准输出的结果——踩过这个坑之后改成只信文件。

## 一条请求的生命周期

1. 你输入需求。
2. CLI 里的 `ChatClientAgent`（基于 Microsoft Agent Framework）把需求转成一段实现 `IRevitDynamicCommand` 的 C# 类。
3. CLI 把源码写到临时文件，拉起对应版本的执行器。
4. 执行器用 Roslyn 编译这段代码，注入一次 Revit，打开模型副本，执行命令拿到返回值，关模型，退出注入。
5. 执行器把结果写成 JSON 信封到结果文件。
6. CLI 读回结果，喂给大模型，由它用你的语言总结作答。

代码是现场生成的，不是预置脚本。Roslyn 用 `LanguageVersion.Latest`，所以生成出来的 C# 可以用现代写法——`?.`、`$""`、`nameof`、模式匹配、record 都行。

## 批量执行

一个目录下有多个 `.rvt` 时可以一次性都跑掉。不指定 `--model-path` 时，工具默认扫描当前目录下的所有 `.rvt`；指定一个目录也行。所有模型在同一个 Revit 会话里顺序执行——引擎只初始化一次（约 20 秒），而不是每跑一个模型就重新启动一次 Revit。10 个模型大约就是一次初始化加 10 次单模型执行，而不是 10 次完整启动。

返回的是一个多模型信封：每个模型一条结果（成功/失败加数据），外加一个汇总（总数、成功数、失败数）。某个模型打不开（比如版本不对或文件损坏）只影响那一条，其余照跑。

## 支持的 Revit 版本

执行器按版本各编一个：2019、2020、2021、2022 都能编译，也都能无头跑。2022 及以上走 `Autodesk.Revit.Product.Initialize_ForAutodeskInternalUseOnly`；2019–2021 走 `Product.Init` 的回退路径。目标机器上得装着对应版本的 Revit——执行器通过注册表定位安装路径，运行时从那里加载真正的 `RevitAPI.dll`。包里不含 Revit 本体。

## 其余能力

- 技能（skills）：把某个领域的约定、清单、代码模板打包成一个 skill，按需加载。目录里只放名字和一句话描述，需要时再拉全文，不占基础提示。
- 导出 CSV：模型参数之类的表格数据可以直接导成带 BOM 的 UTF-8 CSV，Excel 能直接开。多模型时首列是模型文件名，各模型的行拼在一起。
- 进度显示：跑的时候在终端用灰色实时显示模型的推理、工具调用、返回结果。那段约 20 秒的 Revit 启动空窗会有动画过渡。

## 用之前要准备什么

- Windows x64。Revit 的原生组件只有 x64。
- 装好对应版本的 Revit（2019–2022 任选）。
- 跑 `run`/`chat`（让大模型生成代码）要自己配一个大模型的 API 密钥，兼容任何 OpenAI 兼容后端。密钥只读环境变量，不写进配置文件。
- 构建需要 .NET 10 SDK。

## 快速开始

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

# 多轮交互
revit-agent chat --model-path "D:\Models\MyModel.rvt"

# 直接跑一段手写代码（不经过大模型，用来测试执行链路）
revit-agent exec samples\RoomCheck.cs --version 2022 --model-path "D:\Models\MyModel.rvt"

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
