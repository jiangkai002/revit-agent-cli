namespace RevitAgent.Cli.Commands;

public static class ConfigCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0) return Usage();
        return args[0] switch
        {
            "init" => Init(),
            "set" => Set(args.Skip(1).ToArray()),
            "get" => Get(),
            "path" => ShowPath(),
            _ => Usage($"未知子命令: {args[0]}")
        };
    }

    private static int Init()
    {
        if (File.Exists(ConfigStore.ConfigPath))
        {
            Console.WriteLine($"配置已存在: {ConfigStore.ConfigPath}");
            return 0;
        }
        ConfigStore.Save(new AgentConfig());
        Console.WriteLine($"已写入默认配置: {ConfigStore.ConfigPath}");
        Console.WriteLine($"请编辑填入 Provider/BaseUrl/Model，并设置 API 密钥环境变量（默认名 {new AgentConfig().ApiKeyEnv}）。");
        return 0;
    }

    private static int Set(string[] args)
    {
        if (args.Length < 2) return Usage("用法: config set <key> <value>");
        var key = args[0];
        var value = args[1];
        var config = ConfigStore.Load();
        switch (key.ToLowerInvariant())
        {
            case "provider": config.Provider = value; break;
            case "baseurl": config.BaseUrl = value; break;
            case "model": config.Model = value; break;
            case "apikeyenv": config.ApiKeyEnv = value; break;
            case "version":
                if (!int.TryParse(value, out var v)) { Console.Error.WriteLine("version 需为整数。"); return 1; }
                config.DefaultRevitVersion = v; break;
            case "modelpath": config.DefaultModelPath = value; break;
            default:
                Console.Error.WriteLine($"未知键: {key}（可选: provider/baseurl/model/apikeyenv/version/modelpath）");
                return 1;
        }
        ConfigStore.Save(config);
        Console.WriteLine($"已设置 {key} = {value}");
        return 0;
    }

    private static int Get()
    {
        var config = ConfigStore.Load();
        Console.WriteLine($"配置路径: {ConfigStore.ConfigPath}");
        Console.WriteLine($"Provider:            {config.Provider}");
        Console.WriteLine($"BaseUrl:             {config.BaseUrl}");
        Console.WriteLine($"Model:               {config.Model}");
        Console.WriteLine($"ApiKeyEnv:           {config.ApiKeyEnv}");
        Console.WriteLine($"DefaultRevitVersion: {config.DefaultRevitVersion}");
        Console.WriteLine($"DefaultModelPath:    {config.DefaultModelPath}");
        return 0;
    }

    private static int ShowPath()
    {
        Console.WriteLine(ConfigStore.ConfigPath);
        return 0;
    }

    private static int Usage(string? note = null)
    {
        if (note is not null) Console.WriteLine(note);
        Console.WriteLine("用法: revit-agent config <init|set|get|path>");
        Console.WriteLine("  init                生成默认配置");
        Console.WriteLine("  set <key> <value>   设置项 (provider/baseurl/model/apikeyenv/version/modelpath)");
        Console.WriteLine("  get                 显示当前配置");
        Console.WriteLine("  path                显示配置文件路径");
        return 1;
    }
}
