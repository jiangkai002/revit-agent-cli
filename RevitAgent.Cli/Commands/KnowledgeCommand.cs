namespace RevitAgent.Cli.Commands;

/// <summary>
/// Manages the lessons-learned store (add/list/show/remove/path). Mirrors SkillCommand's
/// style: a switch on the first arg, Console output, returns 0/1.
/// </summary>
public static class KnowledgeCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return Task.FromResult(1);
        }

        var sub = args[0];
        var rest = args[1..];
        return sub switch
        {
            "add" => Add(rest),
            "list" => List(),
            "show" => Show(rest),
            "remove" => Remove(rest),
            "path" => ShowPath(),
            _ => Usage($"未知子命令: {sub}")
        };
    }

    /// <summary>add takes either one arg (whole text, auto title) or two args
    /// (title, body) — the two-arg form suits quoting in shells.</summary>
    private static Task<int> Add(string[] args)
    {
        string title, body;
        if (args.Length >= 2)
        {
            title = args[0];
            body = string.Join(' ', args[1..]);
        }
        else if (args.Length == 1 && !string.IsNullOrWhiteSpace(args[0]))
        {
            var text = args[0].Trim();
            title = text.Length > 40 ? text[..40].TrimEnd() : text;
            body = text;
        }
        else
        {
            Console.Error.WriteLine("用法: revit-agent knowledge add <标题> <内容>   （或只给一句话教训，标题自动截取）");
            return Task.FromResult(1);
        }

        var (entry, updated) = KnowledgeStore.Add(title, body, source: "user");
        Console.WriteLine(updated
            ? $"已更新经验 [{entry.Id}] {entry.Title}（{KnowledgeStore.KnowledgePath}）"
            : $"已保存经验 [{entry.Id}] {entry.Title}（{KnowledgeStore.KnowledgePath}）");
        Console.WriteLine("下次会话起自动进入智能体的经验目录（chat 会话中也可用 /kb 管理）。");
        return Task.FromResult(0);
    }

    private static Task<int> List()
    {
        var entries = KnowledgeStore.List();
        if (entries.Count == 0)
        {
            Console.WriteLine($"（暂无经验教训。用 revit-agent knowledge add <标题> <内容> 沉淀。）");
            return Task.FromResult(0);
        }
        Console.WriteLine($"经验教训 ({entries.Count})：");
        foreach (var e in entries)
        {
            var tags = e.Tags.Count > 0 ? $" [tags: {string.Join(", ", e.Tags)}]" : "";
            Console.WriteLine($"  [{e.Id}] {e.Title}{tags} — {e.Source}");
        }
        return Task.FromResult(0);
    }

    private static Task<int> Show(string[] args)
    {
        var key = string.Join(' ', args).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("用法: revit-agent knowledge show <编号|标题>");
            return Task.FromResult(1);
        }
        var text = KnowledgeStore.Show(key);
        if (text is null)
        {
            Console.Error.WriteLine($"未找到经验: {key}");
            return Task.FromResult(1);
        }
        Console.WriteLine(text);
        return Task.FromResult(0);
    }

    private static Task<int> Remove(string[] args)
    {
        var key = string.Join(' ', args).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("用法: revit-agent knowledge remove <编号|标题>");
            return Task.FromResult(1);
        }
        var (ok, message) = KnowledgeStore.Remove(key);
        Console.WriteLine(ok ? message : message);
        return Task.FromResult(ok ? 0 : 1);
    }

    private static Task<int> ShowPath()
    {
        Console.WriteLine($"经验知识库: {KnowledgeStore.KnowledgePath}");
        Console.WriteLine("可用环境变量 REVIT_AGENT_KNOWLEDGE_PATH 指向其他完整文件路径（如团队共享位置）。");
        return Task.FromResult(0);
    }

    private static Task<int> Usage(string? note = null)
    {
        if (note is not null) Console.WriteLine(note);
        Console.WriteLine("revit-agent knowledge — 经验教训管理");
        Console.WriteLine();
        Console.WriteLine("智能体被纠正后可沉淀教训，之后的相关任务会借鉴。子命令:");
        Console.WriteLine("  add <标题> <内容>  保存一条经验（也可只给一句话，标题自动截取）");
        Console.WriteLine("  list              列出已保存的经验");
        Console.WriteLine("  show <编号|标题>   查看一条经验的完整内容");
        Console.WriteLine("  remove <编号|标题> 移除一条经验");
        Console.WriteLine("  path              显示知识库文件路径");
        Console.WriteLine();
        Console.WriteLine($"知识库文件: {KnowledgeStore.KnowledgePath}");
        Console.WriteLine("chat 会话中也可用 /kb 快捷管理。可用环境变量 REVIT_AGENT_KNOWLEDGE_PATH 覆盖路径（便于团队共享）。");
        return Task.FromResult(1);
    }
}
