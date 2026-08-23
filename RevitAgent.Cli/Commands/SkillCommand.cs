namespace RevitAgent.Cli.Commands;

/// <summary>
/// Manages skills (install/list/show/remove/path). Mirrors ConfigCommand's style: a switch
/// on the first arg, Console output, returns 0/1. Install is async (network I/O).
/// </summary>
public static class SkillCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 1;
        }

        var sub = args[0];
        var rest = args[1..];
        return sub switch
        {
            "install" => await InstallAsync(rest),
            "list" => List(),
            "show" => Show(rest),
            "remove" or "uninstall" => Remove(rest),
            "path" => ShowPath(),
            _ => Usage($"未知子命令: {sub}")
        };
    }

    private static async Task<int> InstallAsync(string[] args)
    {
        var url = args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.Error.WriteLine("用法: revit-agent skill install <url>");
            return 1;
        }

        Console.WriteLine($"正在从 {url} 安装技能...");
        var (ok, message) = await SkillStore.InstallFromUrlAsync(url);
        Console.WriteLine(message);
        if (ok)
        {
            Console.WriteLine("注意: 技能模板会以 C# 形式在你的 Revit 模型上执行，仅从可信来源安装。");
            return 0;
        }
        return 1;
    }

    private static int List()
    {
        var skills = SkillStore.ListInstalled();
        if (skills.Count == 0)
        {
            Console.WriteLine($"(当前未安装任何技能。将技能目录放入: {SkillStore.SkillsDirectory})");
            return 0;
        }
        Console.WriteLine($"已安装技能 ({skills.Count})，目录: {SkillStore.SkillsDirectory}");
        foreach (var s in skills)
        {
            var desc = string.IsNullOrWhiteSpace(s.Description) ? "(无简介)" : s.Description;
            Console.WriteLine($"  {s.Name} — {desc}");
        }
        return 0;
    }

    private static int Show(string[] args)
    {
        var name = args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("用法: revit-agent skill show <name>");
            return 1;
        }
        var text = SkillStore.Show(name);
        if (text is null)
        {
            Console.Error.WriteLine($"未找到技能: {name}");
            return 1;
        }
        Console.WriteLine(text);
        return 0;
    }

    private static int Remove(string[] args)
    {
        var name = args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("用法: revit-agent skill remove <name>");
            return 1;
        }
        var (ok, message) = SkillStore.Remove(name);
        Console.WriteLine(message);
        return ok ? 0 : 1;
    }

    private static int ShowPath()
    {
        Console.WriteLine(SkillStore.SkillsDirectory);
        return 0;
    }

    private static int Usage(string? note = null)
    {
        if (note is not null) Console.WriteLine(note);
        Console.WriteLine("revit-agent skill — 技能管理");
        Console.WriteLine();
        Console.WriteLine("子命令:");
        Console.WriteLine("  install <url>  从 zip URL 安装技能（http/https）");
        Console.WriteLine("  list            列出已安装技能");
        Console.WriteLine("  show <name>     查看技能的 manifest 与指引正文");
        Console.WriteLine("  remove <name>   移除技能");
        Console.WriteLine("  path            显示技能目录路径");
        Console.WriteLine();
        Console.WriteLine($"技能目录: {SkillStore.SkillsDirectory}");
        Console.WriteLine("可用环境变量 REVIT_AGENT_SKILLS_ROOT 覆盖目录（便于团队共享）。");
        return 1;
    }
}
