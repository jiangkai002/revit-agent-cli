<p align="center">
  <img src="RevitAgent.Gui/Assets/RevitAgent.png" width="112" alt="RevitAgent logo">
</p>

<p align="center">
  <a href="README.md">简体中文</a> · <strong>English</strong>
</p>

# RevitAgent

> Windows desktop workspace, command-line agent, and live website: <https://jiangkai002.github.io/revit-agent-cli/?lang=en>

RevitAgent is an AI desktop application and CLI for Autodesk Revit models. Describe a task in natural language—such as “How many rooms are in this model?”—and the agent generates Revit API C# code, compiles and executes it through a headless Revit process, then delivers the result as a natural-language answer or an Excel-friendly CSV file.

The entire path from request analysis to code generation, execution, and summarization is automated.

## Why it exists

Revit's native automation stack runs on .NET Framework 4.8 and depends on `RevitNET.dll`, so it cannot simply be hosted inside a modern .NET process. The usual solution is to install an add-in, open Revit manually, and click a command—not a good fit for agent-driven or batch automation.

RevitAgent separates the experience, agent host, and Revit runtime:

- `RevitAgent.Gui` (.NET 10 + WPF) provides a Windows Fluent UI desktop workspace with light/dark themes, model selection, skill management, and settings.
- `revit-agent` (.NET 10) is both the CLI and the agent core. It talks to the model provider, generates code, starts the executor, reads results, and produces the final answer.
- `RevitAgent.Executor.<version>.exe` (.NET Framework 4.8, x64) initializes the selected Revit version without its normal UI, opens a model copy, runs the generated code, and exits.

The processes exchange results through files rather than stdout. Revit writes shutdown noise to the console, so a dedicated result file is the reliable source of truth.

## Request lifecycle

1. Enter a request in the desktop app or CLI.
2. `ChatClientAgent`, built on Microsoft Agent Framework, generates a C# class implementing `IRevitDynamicCommand`.
3. The agent host writes the source to a temporary file and starts the correct versioned executor.
4. The executor compiles the source with Roslyn, initializes Revit once, opens a temporary model copy, and runs the command.
5. The executor writes a structured JSON envelope containing per-model data and errors.
6. The agent summarizes the result and streams the reasoning, tool activity, and final answer to the GUI or terminal.

## Windows desktop GUI

The WPF application follows the Windows Fluent UI visual language and is designed for long-running model analysis:

- Follow the system light/dark theme or choose a theme manually.
- Scroll long conversations from anywhere in the transcript while the composer stays fixed at the bottom.
- Collapse reasoning, tool calls, and large tool results.
- Render Markdown and result tables correctly in both themes.
- Select one or more `.rvt` files and switch the active Revit version.
- Manage bundled skills, URL-installed skills, and local ZIP packages.
- Configure the model provider, endpoint, API-key environment variable, and default model path.

## Core capabilities

- **Headless Revit 2019–2022:** each supported release has its own executor. The target computer must have the matching Autodesk Revit version installed.
- **Multi-model batches:** run every top-level `.rvt` file in a directory in one Revit session. A failed model is reported separately and does not stop the rest of the batch.
- **Generated modern C#:** Roslyn compiles the code produced for each request instead of selecting from a fixed script library.
- **Skills:** five read-only examples ship with the package—room audit, building-operations room compliance, and MEP device number/family/connector checks. Additional skills can be installed from an HTTP(S) ZIP or a local ZIP file.
- **CSV export:** table-shaped results are written as UTF-8 with BOM for Excel. Relative output paths resolve beside the first active Revit model; multi-model exports include a leading `Model` column.
- **Lessons learned (knowledge):** when the agent repeatedly fails and only succeeds after your correction, the lesson can be distilled into a knowledge entry stored at `%APPDATA%\revit-agent\knowledge.json` — the agent proactively saves it via the SaveKnowledge tool after a confirmed correction, or you can add one manually in chat with `/kb add <title>::<body>`. Future related tasks consult the catalog first, so the same mistake is not repeated. Manage with `/kb list|show|remove|path` in chat or the `revit-agent knowledge` CLI command; set `REVIT_AGENT_KNOWLEDGE_PATH` to a shared file so a team reuses one knowledge base.
- **Live progress:** reasoning, tool calls, tool results, and the Revit startup stage are shown while the request runs.

## Requirements

- Windows x64
- Autodesk Revit 2019, 2020, 2021, or 2022 installed locally
- An OpenAI-compatible model endpoint and API key for `run` and `chat`
- .NET 10 SDK for source builds
- WiX Toolset v7 only when building the MSI

The MSI contains the GUI, CLI, .NET/WPF runtime, all four executors, and bundled skills. It does not contain Autodesk Revit or an API key.

## Quick start

### Build the Windows installer

See [Windows packaging and delivery](PACKAGING.md) for the complete workflow.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-msi.ps1
```

Install `revit-agent.msi`, then launch **RevitAgent** from the Start menu.

### Run the GUI from source

```powershell
dotnet run --project .\RevitAgent.Gui\RevitAgent.Gui.csproj
```

### Build and configure the CLI

```powershell
.\build.ps1

revit-agent config init
revit-agent config set baseurl https://api.openai.com/v1
revit-agent config set model gpt-4o
revit-agent config set apikey "sk-..."
```

The API key is stored in `config.json` (the same file the GUI settings page writes); a legacy
`REVIT_AGENT_API_KEY` environment variable still works as a fallback when the key is empty.

### Examples

```powershell
# Generate and execute Revit API code from a natural-language request
revit-agent run "Check whether this model contains rooms" `
  --version 2022 `
  --model-path "D:\Models\MyModel.rvt"

# Start a multi-turn conversation for every top-level model in a directory
revit-agent chat --model-path "D:\Models"

# List bundled and user-installed skills
revit-agent skill list

# Install a skill from a local ZIP package
revit-agent skill install "D:\Skills\my-revit-skill.zip"

# Manage lessons learned (auto-saved after confirmed corrections; /kb works inside chat)
revit-agent knowledge add "Convert room area from square feet" "Room.Area returns square feet; multiply by 0.09290304 for square meters"
revit-agent knowledge list

# Run handwritten code without an LLM
revit-agent exec samples\RoomCheck.cs `
  --version 2022 `
  --model-path "D:\Models\MyModel.rvt"
```

In an interactive chat, use `/rvt` for the model picker, `/rvt all` to restore the initial batch, or `/rvt 1` and `/rvt <name>` for quick selection.

## Skill package format

A skill ZIP must contain exactly one skill directory with:

```text
my-skill/
├── skill.json
├── SKILL.md
└── templates/
    └── Example.cs
```

`skill.json` provides the name, description, version, author, and optional series tags. `SKILL.md` contains the instructions loaded by the agent, and `templates/*.cs` contains optional Revit API examples.

Install and manage skills with:

```powershell
revit-agent skill install <url-or-zip-path>
revit-agent skill list
revit-agent skill show <name>
revit-agent skill remove <name>
revit-agent skill path
```

Only install skills from sources you trust: their templates guide code that runs against your Revit model.

## Generated-code contract

Agent-generated code implements this interface and returns JSON-serializable data:

```csharp
public sealed class DynamicCommand : RevitAgent.DynamicCode.IRevitDynamicCommand
{
    public object Execute(Autodesk.Revit.DB.Document document)
    {
        return new { /* scalar values, arrays, and plain objects */ };
    }
}
```

Do not return raw `Element`, `Document`, or `XYZ` objects. Select the scalar fields required by the answer, such as Id, Name, Area, type, and parameter values.

## Limitations

- Revit sessions are serialized; only one executor runs at a time.
- Directory model discovery scans the top level and does not recurse.
- A directory containing models from different Revit releases will report version-mismatched models as individual failures.
- Very large batches are not yet split into automatic Revit process restarts.
- Production MSI releases should be signed with a trusted code-signing certificate to reduce Windows SmartScreen warnings.
