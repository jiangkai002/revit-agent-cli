namespace RevitAgent.Cli.Commands;

public static class ChatCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var opts = OptionParser.Parse(args);
        var config = ConfigStore.Load();
        var modelPaths = OptionParser.ResolveModelPaths(opts, config);
        var version = OptionParser.ResolveVersion(opts, config);

        if (modelPaths.Count == 0)
        {
            Console.Error.WriteLine("未找到 Revit 模型文件。请通过 --model-path 指定文件或目录，或在含 .rvt 的目录下运行。");
            return 1;
        }

        var host = new AgentHost(config, opts.Model, modelPaths, version);
        Console.WriteLine($"RevitAgent 交互会话已启动（{modelPaths.Count} 个模型，Revit {version}）。输入 exit 退出。");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null) break;
            if (line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var answer = await host.AskAsync(line, CancellationToken.None);
                Console.WriteLine(answer);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"错误: {ex.Message}");
            }
        }

        return 0;
    }
}
