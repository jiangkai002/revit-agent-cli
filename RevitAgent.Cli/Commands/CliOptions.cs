using RevitAgent.Cli;

namespace RevitAgent.Cli.Commands;

public sealed class CliOptions
{
    public List<string> Positional { get; } = new();
    public int? Version { get; set; }
    public string? ModelPath { get; set; }
    public string? Model { get; set; }
}

public static class OptionParser
{
    public static CliOptions Parse(string[] args)
    {
        var opts = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--version":
                case "-v":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var v))
                        opts.Version = v;
                    break;
                case "--model-path":
                case "-p":
                    if (i + 1 < args.Length) opts.ModelPath = args[++i];
                    break;
                case "--model":
                    if (i + 1 < args.Length) opts.Model = args[++i];
                    break;
                default:
                    opts.Positional.Add(args[i]);
                    break;
            }
        }
        return opts;
    }

    public static string ResolveModelPath(CliOptions opts, AgentConfig config)
    {
        var paths = ResolveModelPaths(opts, config);
        return paths.Count > 0 ? paths[0] : string.Empty;
    }

    /// <summary>
    /// Resolves the batch of Revit models to run. Precedence: --model-path →
    /// config.DefaultModelPath → env REVIT_MODEL_PATH → scan the current working directory
    /// for *.rvt. A non-blank source may be a file (single-model batch) or a directory (scan
    /// its top-level *.rvt). All paths are made absolute because the executor subprocess runs
    /// with WorkingDirectory set to its own exe folder (PolyHook2 native dll resolution), so a
    /// relative path would resolve against the exe folder, not the user's shell cwd. Returns
    /// an empty list when no model is found; callers report the error.
    /// </summary>
    public static List<string> ResolveModelPaths(CliOptions opts, AgentConfig config)
    {
        var src = !string.IsNullOrWhiteSpace(opts.ModelPath) ? opts.ModelPath!
            : !string.IsNullOrWhiteSpace(config.DefaultModelPath) ? config.DefaultModelPath
            : Environment.GetEnvironmentVariable("REVIT_MODEL_PATH") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(src))
        {
            return ScanDirectory(Environment.CurrentDirectory);
        }

        if (File.Exists(src))
        {
            return new List<string> { ToAbsolute(src) };
        }

        if (Directory.Exists(src))
        {
            return ScanDirectory(src);
        }

        Console.Error.WriteLine($"错误：模型路径不存在: {src}");
        return new List<string>();
    }

    private static List<string> ScanDirectory(string directory)
    {
        var result = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(directory, "*.rvt"))
            {
                result.Add(ToAbsolute(file));
            }
        }
        catch
        {
            // Directory access failure → empty list; the caller reports "no model found".
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static string ToAbsolute(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; } // malformed path: let the executor surface its own error
    }

    public static int ResolveVersion(CliOptions opts, AgentConfig config)
    {
        if (opts.Version.HasValue) return opts.Version.Value;
        var env = Environment.GetEnvironmentVariable("REVIT_VERSION");
        if (int.TryParse(env, out var ev)) return ev;
        return config.DefaultRevitVersion;
    }

    public static string ResolveModel(CliOptions opts, AgentConfig config)
    {
        if (!string.IsNullOrWhiteSpace(opts.Model)) return opts.Model!;
        return config.Model;
    }
}
