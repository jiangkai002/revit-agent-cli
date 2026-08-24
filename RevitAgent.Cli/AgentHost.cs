using System.ClientModel;
using System.IO;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;

namespace RevitAgent.Cli;

/// <summary>
/// Bridges the LLM agent loop (Microsoft Agent Framework) to the net48 Revit executor. Builds a
/// <see cref="ChatClientAgent"/> with three tools — RunRevitCode (run C# → JSON answer), LoadSkill
/// (load a skill's guidance+templates), and ExportCsv (run C# returning a row list → write CSV) —
/// maintains one <see cref="AgentSession"/> across turns (multi-turn memory), and returns the
/// agent's final text for each user message. The model batch (<see cref="_modelPaths"/>) runs in
/// a single Revit session in the executor, so the ~20s headless init is paid once per batch.
/// </summary>
public sealed class AgentHost
{
    private readonly AgentConfig _config;
    private readonly string _modelOverride;
    private readonly IReadOnlyList<string> _initialModelPaths; // the entering batch; "/rvt all" restores to this
    private IReadOnlyList<string> _modelPaths; // current effective batch; mutable via SetModelPaths (/rvt), read by the tool wrapper
    private readonly int _revitVersion;

    private ChatClientAgent? _agent;
    private AgentSession? _session;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile Spinner? _spinner; // per AskAsync call; read by the tool wrapper (may run on a framework thread)
    private CancellationToken _cancelToken; // per AskAsync call; read by the tool wrapper so Ctrl+C cancels the in-flight executor wait. Plain field — CancellationToken (a struct w/ a reference) can't be `volatile`; the async/await happens-before edge when the framework schedules the delegate guarantees the write is visible.

    public AgentHost(AgentConfig config, string? modelOverride, IReadOnlyList<string> modelPaths, int revitVersion)
    {
        _config = config;
        _modelOverride = modelOverride ?? string.Empty;
        _initialModelPaths = modelPaths;
        _modelPaths = modelPaths;
        _revitVersion = revitVersion;
    }

    /// <summary>The batch of models active when the session started; "/rvt all" restores to it.</summary>
    public IReadOnlyList<string> InitialModelPaths => _initialModelPaths;

    /// <summary>The currently effective model batch (mutable via the /rvt REPL command); the tool
    /// wrapper runs Execute once per model in this list. Read by the tool wrapper during a turn,
    /// written by SetModelPaths only between turns (at the REPL prompt), so the read is race-free.</summary>
    public IReadOnlyList<string> CurrentModelPaths => _modelPaths;

    /// <summary>Switch the effective model batch (from the /rvt REPL command).</summary>
    public void SetModelPaths(IReadOnlyList<string> paths) => _modelPaths = paths;

    /// <summary>Restore the effective batch to the entering batch (the "/rvt all" case).</summary>
    public void ResetModelPaths() => _modelPaths = _initialModelPaths;

    public async Task<string> AskAsync(string request, CancellationToken ct)
    {
        var agent = await GetAgentAsync(ct);
        _cancelToken = ct; // the tool delegates read this so Ctrl+C can cancel the in-flight executor wait
        _session ??= await agent.CreateSessionAsync(conversationId: null!, ct);

        // Spinner animates during gaps (notably the ~20s headless Revit execution);
        // the streaming ProcessDisplay pauses it to print gray event lines (reasoning,
        // tool calls, tool results) and resumes afterward. The RunRevitCode tool
        // wrapper still drives 执行中 / 汇总结果中 stages on this same spinner, so the
        // user sees live activity during the long tool run, not a frozen line. The
        // final answer is returned (not printed here) so the command prints it in
        // normal color, cleanly separated from the gray process above it.
        var spinner = new Spinner("分析需求中");
        _spinner = spinner;

        using var analyzeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500, analyzeCts.Token);
                spinner.TransitionToWritingIfStillAnalyzing();
            }
            catch (OperationCanceledException) { }
        });

        try
        {
            var runOptions = new ChatClientAgentRunOptions(new ChatOptions());
            var display = new ProcessDisplay(spinner);
            // TextContent buffered for the current turn: if a tool call follows it, it is
            // shown as a gray "preamble" line (the model thinking out loud before acting);
            // if the stream ends with no tool call after it, it is the final answer
            // returned to the caller. Reasoning (TextReasoningContent) streams live and
            // is excluded from the answer, naturally mirroring AgentResponse.Text.
            var preamble = new StringBuilder();

            await foreach (var update in agent.RunStreamingAsync(request, _session, runOptions, ct))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent reasoning:
                            display.WriteReasoning(reasoning.Text ?? "");
                            break;
                        case TextContent text:
                            preamble.Append(text.Text);
                            break;
                        case FunctionCallContent functionCall:
                            if (preamble.Length > 0)
                            {
                                display.WritePreamble(preamble.ToString());
                                preamble.Clear();
                            }
                            display.WriteToolCall(functionCall.Name ?? "?", functionCall.Arguments);
                            break;
                        case FunctionResultContent functionResult:
                            if (preamble.Length > 0)
                            {
                                display.WritePreamble(preamble.ToString());
                                preamble.Clear();
                            }
                            display.WriteToolResult(functionResult.Result);
                            break;
                    }
                }
            }

            display.CloseLine();

            // Text remaining after the last event with no subsequent tool call = the final answer.
            var finalText = preamble.ToString();
            return string.IsNullOrWhiteSpace(finalText) ? "(模型未返回文本)" : finalText;
        }
        finally
        {
            analyzeCts.Cancel();
            _spinner = null;
            spinner.Dispose(); // stops the animation, clears the spinner line
        }
    }

    private async Task<ChatClientAgent> GetAgentAsync(CancellationToken ct)
    {
        if (_agent is not null) return _agent;
        await _initLock.WaitAsync(ct);
        try
        {
            _agent ??= BuildAgent();
            return _agent;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private ChatClientAgent BuildAgent()
    {
        var apiKey = Environment.GetEnvironmentVariable(_config.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"未设置 API 密钥环境变量 {_config.ApiKeyEnv}。" +
                $"请先设置该环境变量（如 setx {_config.ApiKeyEnv} \"sk-...\"）后再试。");
        }

        var clientOpts = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(_config.BaseUrl))
        {
            clientOpts.Endpoint = new Uri(_config.BaseUrl);
        }

        // OpenAI-compatible: the same OpenAIClient works for any OpenAI-shaped
        // endpoint (OpenAI, DeepSeek,通义, local vLLM, …) by overriding Endpoint.
        var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), clientOpts);
        var chatClient = openAi.GetChatClient(ResolveModel());
        IChatClient ichat = chatClient.AsIChatClient();

        // RunRevitCode: compile + run Revit-API C# headless, return a JSON answer. The framework
        // invokes this delegate with the source string the LLM produces; it cannot receive a
        // CancellationToken, so the subprocess runs to completion. The same RunRevitCodeTool
        // instance is shared with ExportCsv (its Gate serializes Revit access, so the two tools
        // never run concurrently). The whole model batch runs in one executor process / one Revit
        // session, so Execute is called once per model but Revit initializes only once.
        var tool = new RunRevitCodeTool();
        AITool runRevitCode = AIFunctionFactory.Create(
            (Func<string, Task<string>>)(async source =>
            {
                _spinner?.MarkExecuting();
                try { return await tool.RunAsync(source, _modelPaths, _revitVersion, _cancelToken); }
                finally { _spinner?.SetStage("汇总结果中"); }
            }),
            name: "RunRevitCode",
            description: "编译并在无头 Revit 中运行 C# 二次开发代码。传入一个完整 .cs 文件源码字符串，"
                + "其中定义 `public sealed class DynamicCommand : RevitAgent.DynamicCode.IRevitDynamicCommand`，"
                + "实现 `public object? Execute(Autodesk.Revit.DB.Document document)`。"
                + "会对当前批次中的每个 Revit 模型各执行一次 Execute（同一 Revit 会话复用，不逐个初始化）。"
                + "返回 JSON 信封 {\"Ok\":<全部成功>,\"Models\":[{\"Model\",\"Ok\",\"Data\",\"Error\"},...],"
                + "\"Summary\":{\"Total\",\"Succeeded\",\"Failed\"},\"Error\":null}。"
                + "顶层 Error 非空表示编译/注入失败（按 Error.Message 修正代码后重试，最多 3 次）；"
                + "逐模型失败见 Models[i].Error（通常是版本不符/损坏，报告哪些模型失败即可）。"
                + "多个模型时请汇总各模型结果，让 Execute 返回紧凑的逐模型摘要而非完整元素列表。",
            serializerOptions: null);

        // LoadSkill: progressive disclosure — the catalog (name+description) is always in the
        // system prompt; the agent calls this to fetch a skill's full guidance + templates only
        // when a request matches. Sync delegate (string→string); the framework feeds the return
        // text to the LLM as the tool result. Not-found re-lists installed names for self-correction.
        AITool loadSkill = AIFunctionFactory.Create(
            (Func<string, string>)(name => SkillStore.LoadSkillBody(name)
                ?? $"未找到技能: {name}。可用技能: {string.Join(", ", SkillStore.ListInstalled().Select(s => s.Name))}"),
            name: "LoadSkill",
            description: "按名称加载已安装技能的详细指南与 C# 模板。传入技能名称（须与可用技能目录一致），"
                + "返回该技能 SKILL.md 正文及 templates/*.cs 模板源码。匹配时先调用此工具加载技能，"
                + "再据此改写代码并调用 RunRevitCode。",
            serializerOptions: null);

        // ExportCsv: when the user wants to export element parameter info to a CSV file,
        // the agent passes a C# source (same IRevitDynamicCommand contract, but Execute
        // returns a LIST of flat row objects) + an output path. Runs against every model in
        // the batch (Execute called once per model), then concatenates all models' rows into
        // one CSV with a leading Model column, written CLI-side (net10) with Excel-friendly encoding.
        AITool exportCsv = AIFunctionFactory.Create(
            (Func<string, string, Task<string>>)(async (source, path) =>
            {
                _spinner?.MarkExecuting();
                try
                {
                    var envelope = await tool.RunAsync(source, _modelPaths, _revitVersion, _cancelToken);
                    return ExportCsvTool.Export(envelope, path);
                }
                finally { _spinner?.SetStage("汇总结果中"); }
            }),
            name: "ExportCsv",
            description: "将 Revit 构件参数信息导出为 CSV 文件。参数：(1) source — 完整 .cs 源码字符串，"
                + "定义 `public sealed class DynamicCommand : RevitAgent.DynamicCode.IRevitDynamicCommand`，"
                + "其 `Execute(Document)` 返回一个**列表**（数组/List）的扁平行对象（每行一个匿名对象，"
                + "属性=列名，值须为标量 int/double/bool/string，不要嵌套对象）。"
                + "会对批次中每个模型各执行一次 Execute，工具把所有模型的行拼成一个 CSV，"
                + "并自动在最前加 `Model` 列（模型文件名）区分来源。"
                + "(2) path — 输出 CSV 文件路径（默认放当前目录，如 ./walls.csv；若用户指定了路径则用其路径）。"
                + "工具编译并运行代码，序列化为 CSV（UTF-8+BOM，Excel 友好）写入该路径，"
                + "返回 '已导出 N 行（M 列，来自 K 个模型）到 <path>'。注意：每模型返回完整列表，不要截断/抽样。",
            serializerOptions: null);

        var instructions = LoadSystemPrompt() + BuildSkillsCatalog();
        var services = new ServiceCollection().BuildServiceProvider();

        return new ChatClientAgent(
            ichat,
            instructions,
            name: "revit-agent",
            description: "Generates and runs Revit API C# code against a batch of Revit models.",
            tools: new List<AITool> { runRevitCode, loadSkill, exportCsv },
            loggerFactory: new NullLoggerFactory(),
            services: services);
    }

    private string ResolveModel() =>
        !string.IsNullOrWhiteSpace(_modelOverride) ? _modelOverride : _config.Model;

    private static string LoadSystemPrompt()
    {
        var names = typeof(AgentHost).Assembly.GetManifestResourceNames();
        var resourceName = names.FirstOrDefault(n =>
            n.EndsWith("Prompts.SystemPrompt.md", StringComparison.Ordinal));
        if (resourceName is null) return FallbackPrompt;

        using var stream = typeof(AgentHost).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return FallbackPrompt;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Build the dynamic available-skills catalog appended to the system prompt.</summary>
    private static string BuildSkillsCatalog()
    {
        var skills = SkillStore.ListInstalled();
        if (skills.Count == 0)
            return "\n\n# 可用技能目录\n（当前未安装任何技能。直接按工作流生成代码即可。）\n";

        var lines = new List<string> { "", "", "# 可用技能目录" };
        foreach (var s in skills)
        {
            var desc = string.IsNullOrWhiteSpace(s.Description) ? "(无简介)" : s.Description;
            if (desc.Length > 120) desc = desc[..117] + "...";
            lines.Add($"- {s.Name} — {desc}");
        }
        lines.Add("（匹配需求时先调用 LoadSkill(\"名称\") 加载详细指南与模板）");
        return string.Join("\n", lines) + "\n";
    }

    private const string FallbackPrompt =
        "You are RevitAgent. Translate the user's request about one or more Revit models into C# implementing " +
        "RevitAgent.DynamicCode.IRevitDynamicCommand.Execute(Document), call the RunRevitCode tool with the full " +
        "source, read the returned JSON envelope {\"Ok\",\"Models\":[{\"Model\",\"Ok\",\"Data\",\"Error\"}],\"Summary\",\"Error\"}, " +
        "and summarize it for the user in their language. Execute runs once per model in a single reused Revit " +
        "session; aggregate per-model results rather than dumping raw JSON. Return only JSON-serializable data " +
        "(primitives/arrays/anonymous objects), never raw Revit Element/Document/XYZ.";
}
