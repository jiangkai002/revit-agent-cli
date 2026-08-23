using System.Text;

namespace RevitAgent.Cli;

/// <summary>
/// Locates the per-version net48 executor exe by probing several candidate locations
/// (dev `dotnet run` output, installed global-tool layouts, and an env override).
/// </summary>
public static class ExecutorLocator
{
    public static string Find(int version)
    {
        var exeName = $"RevitAgent.Executor.{version}.exe";
        var folder = $"executor-{version}";
        var candidates = new List<string>();

        // Explicit override (points either at the executor root or at the version folder).
        var root = Environment.GetEnvironmentVariable("REVIT_AGENT_EXECUTOR_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            candidates.Add(Path.Combine(root, folder, exeName));
            candidates.Add(Path.Combine(root, exeName));
        }

        var baseDir = AppContext.BaseDirectory;

        // Dev: `dotnet run` / `dotnet build` stages executors beside the CLI assembly.
        candidates.Add(Path.Combine(baseDir, folder, exeName));

        // Installed global tool layouts (nupkg tools/ subtree variants).
        candidates.Add(Path.Combine(baseDir, "tools", folder, exeName));
        candidates.Add(Path.Combine(baseDir, "..", folder, exeName));
        candidates.Add(Path.Combine(baseDir, "..", "..", folder, exeName));
        candidates.Add(Path.Combine(baseDir, "..", "..", "..", folder, exeName));

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }

        throw new FileNotFoundException(
            $"找不到 Revit {version} 执行器 ({exeName})。已尝试以下路径：\n" +
            string.Join("\n", candidates.Select(Path.GetFullPath)) +
            "\n可设置环境变量 REVIT_AGENT_EXECUTOR_ROOT 指向执行器根目录。");
    }
}
