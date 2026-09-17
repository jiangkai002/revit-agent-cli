using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RevitAgent.Cli;

namespace RevitAgent.Mcp;

/// <summary>
/// Tools exposed over MCP. The CALLING client's model (Claude / WorkBuddy / ...) is the code
/// generator here — this server only provides the headless execution capability that the CLI's
/// own agent loop uses internally, so no LLM key is configured on this side. The executor
/// result envelope (with per-stage Error.Stage) is returned verbatim so the client AI can
/// self-correct: compile errors → fix the code → call again.
/// </summary>
[McpServerToolType]
public static class RevitTools
{
    /// <summary>
    /// One instance for the process lifetime: its static SemaphoreSlim gate serializes Revit
    /// sessions when an MCP client issues concurrent tool calls (only one headless Revit
    /// session may run at a time; concurrent callers queue behind the gate).
    /// </summary>
    private static readonly RunRevitCodeTool Runner = new();

    /// <summary>Camel-case JSON for the version probe report.</summary>
    private static readonly JsonSerializerOptions ProbeJson = new(JsonSerializerDefaults.Web);

    [McpServerTool, Description(
        "在无头 Revit 会话中编译并执行一段调用 Revit API 的 C# 代码，返回 JSON 结果信封(Ok/Models/Summary/Error)。" +
        "代码契约: 源码必须定义 public sealed class DynamicCommand 实现 RevitAgent.DynamicCode.IRevitDynamicCommand，" +
        "其 Execute(Autodesk.Revit.DB.Document document) 返回可 JSON 序列化的纯数据(基本类型/数组/匿名对象)；" +
        "禁止返回 Element/Document/XYZ 等 Revit 对象，必须先取出标量字段(Id、Name、Area 等)。可用现代 C# 语法。 " +
        "所有模型在同一个 Revit 会话中顺序执行，引擎首次初始化约 20 秒；调用全局串行。" +
        "失败时看信封 Error.Stage: compile=代码编译错误(应修正代码后重试)、open=模型打不开、execute=运行期异常、inject=Revit 引擎初始化失败。")]
    public static async Task<string> RunRevitCode(
        [Description("完整 C# 源码(代码全文，不是文件路径)，必须实现 IRevitDynamicCommand 契约")] string source,
        [Description("要打开的 .rvt 模型绝对路径列表；单个模型也传单元素数组")] string[] modelPaths,
        [Description("Revit 版本号(2019/2020/2021/2022)，须与本机安装的 Revit 及模型版本一致")] int revitVersion,
        CancellationToken ct)
        => await Runner.RunAsync(source, modelPaths, revitVersion, ct);

    [McpServerTool, Description("列出目录顶层的 .rvt 模型文件(不递归子目录)，返回绝对路径数组。")]
    public static string[] ListModels(
        [Description("要扫描的目录绝对路径")] string directory)
        => Directory.EnumerateFiles(directory, "*.rvt", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .ToArray();

    [McpServerTool, Description("探测本机已就位的 RevitAgent 执行器(2019-2022)，返回每个版本的可用性及其 exe 路径。")]
    public static string DetectRevitVersions()
    {
        var probes = new List<object>(4);
        foreach (var version in new[] { 2019, 2020, 2021, 2022 })
        {
            try
            {
                probes.Add(new { version, available = true, executor = ExecutorLocator.Find(version) });
            }
            catch (FileNotFoundException)
            {
                // Executor not staged/installed for this version — a probe result, not an error.
                probes.Add(new { version, available = false, executor = (string?)null });
            }
        }

        return JsonSerializer.Serialize(probes, ProbeJson);
    }
}
