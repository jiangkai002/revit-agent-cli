namespace RevitAgent.Cli.Commands;

public static class RunCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var opts = OptionParser.Parse(args);
        var config = ConfigStore.Load();
        var modelPaths = OptionParser.ResolveModelPaths(opts, config);
        var version = OptionParser.ResolveVersion(opts, config);
        var request = string.Join(' ', opts.Positional);

        if (string.IsNullOrWhiteSpace(request))
        {
            Console.Error.WriteLine("缺少需求描述。用法: revit-agent run \"<需求>\" [--version 2022] [--model-path <文件或目录>] [--model <name>]");
            return 1;
        }
        if (modelPaths.Count == 0)
        {
            Console.Error.WriteLine("未找到 Revit 模型文件。请通过 --model-path 指定文件或目录，或在含 .rvt 的目录下运行。");
            return 1;
        }

        var host = new AgentHost(config, opts.Model, modelPaths, version);
        try
        {
            var answer = await host.AskAsync(request, CancellationToken.None);
            Console.WriteLine(answer);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"运行失败: {ex.Message}");
            return 1;
        }
    }
}
