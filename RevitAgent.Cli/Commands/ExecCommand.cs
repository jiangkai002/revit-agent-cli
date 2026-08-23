namespace RevitAgent.Cli.Commands;

/// <summary>
/// Runs a Revit-API .cs file directly through the executor subprocess — no LLM.
/// Useful for verifying the CLI↔executor plumbing and for running hand-written code.
/// </summary>
public static class ExecCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var opts = OptionParser.Parse(args);
        var config = ConfigStore.Load();
        var modelPaths = OptionParser.ResolveModelPaths(opts, config);
        var version = OptionParser.ResolveVersion(opts, config);
        var sourcePath = opts.Positional.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            Console.Error.WriteLine("缺少源码文件。用法: revit-agent exec <source.cs> [--version 2022] [--model-path <文件或目录>]");
            return 1;
        }
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"找不到源码文件: {sourcePath}");
            return 1;
        }
        if (modelPaths.Count == 0)
        {
            Console.Error.WriteLine("未找到 Revit 模型文件。请通过 --model-path 指定文件或目录，或在含 .rvt 的目录下运行。");
            return 1;
        }

        if (modelPaths.Count > 1)
        {
            Console.Error.WriteLine($"将顺序运行 {modelPaths.Count} 个模型（同一 Revit 会话）：");
            foreach (var p in modelPaths) Console.Error.WriteLine($"  {p}");
        }

        var source = await File.ReadAllTextAsync(sourcePath);
        var tool = new RunRevitCodeTool();
        string envelope;
        using (var spinner = new Spinner("执行中"))
        {
            envelope = await tool.RunAsync(source, modelPaths, version, CancellationToken.None);
        } // spinner stopped + line cleared here, before printing the envelope
        Console.WriteLine(envelope);
        return 0;
    }
}
