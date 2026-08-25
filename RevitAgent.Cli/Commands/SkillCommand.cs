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
        var source = args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("用法: revit-agent skill install <url|zip路径>");
            return 1;
        }

        var isWebUrl = Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        Console.WriteLine(isWebUrl ? $"正在从 {source} 安装技能..." : $"正在从本地 ZIP 安装技能: {source}");
        var (ok, message) = isWebUrl
            ? await SkillStore.InstallFromUrlAsync(source)
            : await SkillStore.InstallFromZipAsync(source);
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
        var bundled = skills.Count(s => s.Bundled);
        var user = skills.Count - bundled;
        Console.WriteLine($"技能 ({skills.Count}：{user} 用户装{(bundled > 0 ? $"、{bundled} 内置只读" : "")})");
        foreach (var s in skills)
        {
            var desc = string.IsNullOrWhiteSpace(s.Description) ? "(无简介)" : s.Description;
            var tag = s.Bundled ? " [内置]" : "";
            var tags = (s.Tags != null && s.Tags.Count > 0) ? $" [tags: {string.Join(", ", s.Tags)}]" : "";
            Console.WriteLine($"  {s.Name}{tag}{tags} — {desc}");
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
        Console.WriteLine($"用户技能目录: {SkillStore.SkillsDirectory}");
        var bundled = SkillStore.BundledSkillsDirectory;
        Console.WriteLine(bundled is null
            ? "内置技能(只读): (随包未提供)"
            : $"内置技能(只读): {bundled}");
        return 0;
    }

    private static int Usage(string? note = null)
    {
        if (note is not null) Console.WriteLine(note);
        Console.WriteLine("revit-agent skill — 技能管理");
        Console.WriteLine();
        Console.WriteLine("子命令:");
        Console.WriteLine("  install <来源>  从 zip URL（http/https）或本地 ZIP 路径安装技能");
        Console.WriteLine("  list            列出已安装技能（含 [内置] 只读集）");
        Console.WriteLine("  show <name>     查看技能的 manifest 与指引正文");
        Console.WriteLine("  remove <name>   移除用户技能（内置只读不可移除，可装同名覆盖）");
        Console.WriteLine("  path            显示用户与内置技能目录路径");
        Console.WriteLine();
        Console.WriteLine($"用户技能目录: {SkillStore.SkillsDirectory}");
        Console.WriteLine($"内置技能(只读): {SkillStore.BundledSkillsDirectory ?? "(随包未提供)"}");
        Console.WriteLine("可用环境变量 REVIT_AGENT_SKILLS_ROOT 覆盖用户目录（便于团队共享）。内置随包只读、不可移除。");
        return 1;
    }
}
