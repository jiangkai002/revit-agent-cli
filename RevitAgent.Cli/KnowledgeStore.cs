using System.Text.Json;

namespace RevitAgent.Cli;

/// <summary>
/// One lessons-learned entry distilled from a conversation: what the model repeatedly got
/// wrong, the user's correction, and the approach that finally worked. The catalog
/// (id + title) is appended to the system prompt each session; the agent loads the full
/// body on demand via the LoadKnowledge tool and saves new lessons via SaveKnowledge.
/// </summary>
public sealed class KnowledgeEntry
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The lesson itself: wrong approach vs correct approach, API names, optionally
    /// a short corrected code snippet. Markdown is fine.</summary>
    public string Body { get; set; } = "";
    /// <summary>Optional topic keywords shown in the catalog to aid matching.</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>"agent" (saved via the SaveKnowledge tool) or "user" (saved via /kb or the
    /// `knowledge add` CLI command).</summary>
    public string Source { get; set; } = "user";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Lessons-learned store persisted at %APPDATA%\revit-agent\knowledge.json (the same
/// config directory ConfigStore uses). The env var REVIT_AGENT_KNOWLEDGE_PATH can point
/// at any full file path — e.g. a team-shared network location. All read APIs are
/// failure-tolerant (missing/corrupt file → empty list, never thrown), matching
/// ConfigStore/SkillStore behavior: a broken knowledge file must not break the agent.
/// </summary>
public static class KnowledgeStore
{
    /// <summary>Env override (full file path, e.g. a team share) if set, else
    /// %APPDATA%\revit-agent\knowledge.json.</summary>
    public static string KnowledgePath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("REVIT_AGENT_KNOWLEDGE_PATH");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(ConfigStore.ConfigDirectory, "knowledge.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>All entries ordered by Id. Corrupt/missing file → empty list.</summary>
    public static List<KnowledgeEntry> List()
    {
        try
        {
            if (!File.Exists(KnowledgePath)) return new List<KnowledgeEntry>();
            var json = File.ReadAllText(KnowledgePath);
            var entries = JsonSerializer.Deserialize<List<KnowledgeEntry>>(json, JsonOpts);
            return entries is null
                ? new List<KnowledgeEntry>()
                : entries.Where(e => !string.IsNullOrWhiteSpace(e.Title)).OrderBy(e => e.Id).ToList();
        }
        catch
        {
            return new List<KnowledgeEntry>(); // malformed file: skip, don't crash the agent
        }
    }

    /// <summary>Add a lesson. A case-insensitively identical title updates that entry in
    /// place (same lesson refined) instead of creating a near-duplicate; <paramref name="tags"/>
    /// merge into the existing entry. Returns the stored entry and whether an existing
    /// one was updated. Titles are trimmed to a sane length for the prompt catalog.</summary>
    public static (KnowledgeEntry Entry, bool UpdatedExisting) Add(
        string title, string body, string source = "user", List<string>? tags = null)
    {
        var t = title.Trim();
        if (t.Length > 80) t = t[..80].TrimEnd();
        var entries = List();

        var existing = entries.FirstOrDefault(e =>
            string.Equals(e.Title.Trim(), t, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Body = body.Trim();
            existing.UpdatedAt = DateTime.Now;
            if (tags is not null)
                foreach (var tag in tags)
                    if (!existing.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        existing.Tags.Add(tag);
            Save(entries);
            return (existing, true);
        }

        var entry = new KnowledgeEntry
        {
            Id = entries.Count == 0 ? 1 : entries.Max(e => e.Id) + 1,
            Title = t,
            Body = body.Trim(),
            Tags = tags ?? new List<string>(),
            Source = source,
            CreatedAt = DateTime.Now
        };
        entries.Add(entry);
        Save(entries);
        return (entry, false);
    }

    /// <summary>Remove by exact id, exact title, or unique title substring (case-insensitive).
    /// Returns (ok, message). Ambiguous substring matches are rejected with the candidates listed.</summary>
    public static (bool Ok, string Message) Remove(string idOrTitle)
    {
        var entries = List();
        var target = Resolve(entries, idOrTitle, out var ambiguity);
        if (target is null) return (false, ambiguity ?? $"未找到经验: {idOrTitle}");
        entries.Remove(target);
        Save(entries);
        return (true, $"已移除经验 [{target.Id}] {target.Title}");
    }

    /// <summary>Formatted entry for display (CLI `knowledge show`, chat /kb show). Null if not found.</summary>
    public static string? Show(string idOrTitle)
    {
        var target = Resolve(List(), idOrTitle, out _);
        if (target is null) return null;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{target.Id}] {target.Title}");
        if (target.Tags.Count > 0)
            sb.AppendLine($"标签: {string.Join(", ", target.Tags)}");
        sb.AppendLine($"来源: {target.Source} · 创建: {target.CreatedAt:yyyy-MM-dd HH:mm}" +
            (target.UpdatedAt is { } u ? $" · 更新: {u:yyyy-MM-dd HH:mm}" : ""));
        sb.AppendLine();
        sb.AppendLine(target.Body);
        return sb.ToString();
    }

    /// <summary>Resolve "5", an exact title, or a unique title substring (case-insensitive) to one
    /// entry; null when not found, and the out param explains ambiguity.</summary>
    private static KnowledgeEntry? Resolve(List<KnowledgeEntry> entries, string idOrTitle, out string? ambiguity)
    {
        ambiguity = null;
        var key = idOrTitle.Trim();
        if (int.TryParse(key, out var id))
            return entries.FirstOrDefault(e => e.Id == id);

        var exact = entries.FirstOrDefault(e =>
            string.Equals(e.Title.Trim(), key, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var partials = entries.Where(e =>
            e.Title.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (partials.Count > 1)
        {
            ambiguity = $"'{key}' 匹配到多条: {string.Join("; ", partials.Select(p => $"[{p.Id}] {p.Title}"))}，请用编号";
            return null;
        }
        return partials.Count == 1 ? partials[0] : null;
    }

    /// <summary>Write atomically (temp file + File.Move overwrite) so a crash mid-write or a
    /// concurrent GUI+CLI session never leaves a truncated knowledge.json.</summary>
    private static void Save(List<KnowledgeEntry> entries)
    {
        var path = KnowledgePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries.OrderBy(e => e.Id).ToList(), JsonOpts));
        File.Move(temp, path, overwrite: true);
    }
}
