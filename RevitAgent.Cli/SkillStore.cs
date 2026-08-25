using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RevitAgent.Cli;

/// <summary>
/// A skill is a pluggable domain-knowledge package: guidance + optional C# templates the
/// agent loads on demand (via the LoadSkill tool) to handle a specific business scenario
/// instead of generating code from scratch. Lives under %APPDATA%\revit-agent\skills\
/// (or REVIT_AGENT_SKILLS_ROOT). Each skill is a folder with skill.json + SKILL.md [+ templates/].
/// </summary>
public sealed class SkillManifest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    /// <summary>Optional tags grouping skills into a series. When the user's request matches a tag
    /// shared by multiple skills, the agent loads ALL skills with that tag and runs them one by
    /// one — each is a distinct check dimension. See SystemPrompt "Skill series (tags)".</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>True for read-only skills shipped with the tool (auto-discovered beside the exe,
    /// see BundledSkillsDirectory). Set by ListInstalled; never persisted in skill.json. A
    /// user-installed skill of the same name overrides the bundled one, so the override is
    /// Bundled=false. Used by `skill list` to tag built-in skills as [内置].</summary>
    public bool Bundled { get; set; }
}

public static class SkillStore
{
    /// <summary>Env override (team-shared folder) if set, else %APPDATA%\revit-agent\skills.</summary>
    public static string SkillsDirectory
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("REVIT_AGENT_SKILLS_ROOT");
            return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(ConfigStore.ConfigDirectory, "skills");
        }
    }

    /// <summary>Read-only skills shipped with the tool, auto-discovered so they're available
    /// out-of-box (no `skill install` needed). Probes the same layouts ExecutorLocator uses for
    /// the executors: &lt;baseDir&gt;/skills (dev `dotnet run`, MSI install dir) and
    /// &lt;baseDir&gt;/tools/skills (installed global-tool layout, PackagePath=tools/skills).
    /// Returns null when no bundled set is present (e.g. running from a build that didn't stage
    /// skills) — bundled skills then simply aren't offered, never an error. ListInstalled merges
    /// these under the user dir; LoadSkillBody/Show resolve user-then-bundled; Remove refuses
    /// bundled skills (read-only). A user skill of the same name overrides the bundled one.</summary>
    public static string? BundledSkillsDirectory
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "skills"),
                Path.Combine(baseDir, "tools", "skills"),
                Path.Combine(baseDir, "..", "skills"),
                Path.Combine(baseDir, "..", "..", "skills"),
            };
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (Directory.Exists(full)) return full;
            }
            return null;
        }
    }

    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Enumerate installed skills (name+description). Per-skill parse errors are skipped,
    /// never thrown. Merges the bundled read-only set under BundledSkillsDirectory with the user
    /// dir; a user skill of the same name overrides the bundled one (only the user one is listed,
    /// tagged Bundled=false). The bundled-only ones are tagged Bundled=true.</summary>
    public static List<SkillManifest> ListInstalled()
    {
        // name -> (manifest, isBundled). Bundled first (lower precedence); user dir overwrites
        // any same-named bundled entry, so a user override wins and is tagged non-bundled.
        var byName = new Dictionary<string, (SkillManifest Manifest, bool Bundled)>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in EnumerateSkillDirs(BundledSkillsDirectory))
        {
            var m = TryReadManifest(dir);
            if (m is not null && !string.IsNullOrWhiteSpace(m.Name))
                byName[m.Name] = (m, true);
        }
        foreach (var dir in EnumerateSkillDirs(SkillsDirectory))
        {
            var m = TryReadManifest(dir);
            if (m is not null && !string.IsNullOrWhiteSpace(m.Name))
                byName[m.Name] = (m, false);
        }
        var list = byName.Values.Select(v => { v.Manifest.Bundled = v.Bundled; return v.Manifest; }).ToList();
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static IEnumerable<string> EnumerateSkillDirs(string? root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return Array.Empty<string>();
        return Directory.GetDirectories(root);
    }

    /// <summary>Load a skill's guidance body (SKILL.md + labeled templates/*.cs). Null if not found.</summary>
    public static string? LoadSkillBody(string name)
    {
        var dir = ResolveSkillDir(name);
        return dir is null ? null : LoadSkillBodyFromDir(dir);
    }

    /// <summary>Install a skill from a zip URL. Downloads it, then uses the same validation and
    /// installation pipeline as a local zip.</summary>
    public static async Task<(bool Ok, string Message)> InstallFromUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, $"无效的 URL: {url}（仅支持 http/https）");
        }

        var zipPath = Path.Combine(Path.GetTempPath(), "revit-agent-skill-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var fs = File.Create(zipPath))
            using (var stream = await s_http.GetStreamAsync(uri))
            {
                await stream.CopyToAsync(fs);
            }
            return await Task.Run(() => InstallZipArchive(zipPath));
        }
        catch (HttpRequestException ex)
        {
            return (false, $"下载失败: {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return (false, $"无效的 zip 压缩包: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return (false, $"skill.json 解析失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"安装失败: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    /// <summary>Install a skill from a local zip file.</summary>
    public static Task<(bool Ok, string Message)> InstallFromZipAsync(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            return Task.FromResult((false, "请选择技能 ZIP 文件"));

        string fullPath;
        try { fullPath = Path.GetFullPath(zipPath.Trim()); }
        catch (Exception ex) { return Task.FromResult((false, $"无效的文件路径: {ex.Message}")); }

        if (!File.Exists(fullPath))
            return Task.FromResult((false, $"找不到文件: {fullPath}"));
        if (!string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult((false, "仅支持 .zip 技能压缩包"));

        return Task.Run(() => InstallZipArchive(fullPath));
    }

    /// <summary>Remove an installed user skill (by folder or manifest name). Bundled read-only
    /// skills cannot be removed — to suppress one, install a same-named user skill (it overrides
    /// the bundled entry). False if not found or if the resolved skill is bundled.</summary>
    public static (bool Ok, string Message) Remove(string name)
    {
        var dir = ResolveSkillDir(name);
        if (dir is null) return (false, $"未找到技能: {name}");
        var bundled = BundledSkillsDirectory;
        if (bundled is not null
            && dir.StartsWith(bundled + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"'{name}' 是随包只读内置技能,不可移除。如需禁用,在用户技能目录装同名技能覆盖。");
        }
        try { Directory.Delete(dir, recursive: true); return (true, $"已移除技能: {name}"); }
        catch (Exception ex) { return (false, $"移除失败: {ex.Message}"); }
    }

    /// <summary>Formatted manifest + body for CLI display. Null if not found.</summary>
    public static string? Show(string name)
    {
        var dir = ResolveSkillDir(name);
        if (dir is null) return null;
        var m = TryReadManifest(dir);
        var sb = new StringBuilder();
        sb.AppendLine($"名称: {m?.Name ?? name}");
        sb.AppendLine($"简介: {m?.Description ?? ""}");
        if (m is not null)
        {
            sb.AppendLine($"版本: {m.Version}");
            sb.AppendLine($"作者: {m.Author}");
        }
        sb.AppendLine($"路径: {dir}");
        sb.AppendLine();
        sb.AppendLine(LoadSkillBodyFromDir(dir) ?? "(缺少 SKILL.md)");
        return sb.ToString();
    }

    // ---- helpers ----

    private static (bool Ok, string Message) InstallZipArchive(string zipPath)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "revit-agent-skill-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            ZipFile.ExtractToDirectory(zipPath, tempRoot);

            // Accept a manifest at the archive root or inside one wrapping directory. For more
            // deeply nested archives, locate it recursively, but reject ambiguous multi-skill zips.
            var manifests = Directory.EnumerateFiles(tempRoot, "skill.json", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar)
                    .Any(part => string.Equals(part, "__MACOSX", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (manifests.Count == 0) return (false, "压缩包内未找到 skill.json");
            if (manifests.Count > 1) return (false, "压缩包包含多个 skill.json，请每个 ZIP 只放一个技能");

            var sourceDir = Path.GetDirectoryName(manifests[0])!;
            var manifest = TryReadManifest(sourceDir);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name))
                return (false, "skill.json 无法解析，或缺少 name 字段");

            var requestedName = manifest.Name.Trim();
            var safeName = SanitizeName(requestedName);
            if (safeName is null
                || !string.Equals(safeName, requestedName, StringComparison.Ordinal)
                || requestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return (false, "skill.json 的 name 含有无效路径或文件名字符");

            if (!File.Exists(Path.Combine(sourceDir, "SKILL.md")))
                return (false, "技能缺少 SKILL.md（必需）");

            Directory.CreateDirectory(SkillsDirectory);
            var target = Path.Combine(SkillsDirectory, safeName);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true); // overwrite same-named user skill
            CopyDirectory(sourceDir, target);

            var msg = $"已安装技能: {requestedName}";
            if (!string.IsNullOrWhiteSpace(manifest.Description)) msg += $" — {manifest.Description}";
            return (true, msg);
        }
        catch (InvalidDataException ex)
        {
            return (false, $"无效的 ZIP 压缩包: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return (false, $"skill.json 解析失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"安装失败: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static string? ResolveSkillDir(string name)
    {
        var safe = SanitizeName(name);
        if (safe is null) return null;
        // User dir first: a user-installed same-named skill overrides the bundled (read-only) one.
        var userDir = ResolveInRoot(SkillsDirectory, safe);
        if (userDir is not null) return userDir;
        // Bundled fallback (read-only): LoadSkillBody/Show resolve to it; Remove refuses it.
        return ResolveInRoot(BundledSkillsDirectory, safe);
    }

    private static string? ResolveInRoot(string? root, string safe)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
        // Fast path: folder named exactly 'safe'.
        var dir = Path.Combine(root, safe);
        if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "skill.json"))) return dir;
        // Fallback: a skill whose manifest Name matches (folder may differ from manifest name).
        foreach (var sub in Directory.GetDirectories(root))
        {
            var m = TryReadManifest(sub);
            if (m is not null && string.Equals(m.Name, safe, StringComparison.OrdinalIgnoreCase))
                return sub;
        }
        return null;
    }

    private static string? LoadSkillBodyFromDir(string dir)
    {
        var skillMd = Path.Combine(dir, "SKILL.md");
        if (!File.Exists(skillMd)) return null;
        var sb = new StringBuilder(File.ReadAllText(skillMd));

        var templatesDir = Path.Combine(dir, "templates");
        if (Directory.Exists(templatesDir))
        {
            var files = Directory.GetFiles(templatesDir, "*.cs")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
            foreach (var tpl in files)
            {
                sb.AppendLine();
                sb.AppendLine($"--- 模板: {Path.GetFileName(tpl)} ---");
                sb.AppendLine();
                sb.AppendLine("```csharp");
                sb.AppendLine(File.ReadAllText(tpl));
                sb.AppendLine("```");
            }
        }
        return sb.ToString();
    }

    private static SkillManifest? TryReadManifest(string dir)
    {
        var jsonPath = Path.Combine(dir, "skill.json");
        if (!File.Exists(jsonPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<SkillManifest>(File.ReadAllText(jsonPath), JsonOpts);
        }
        catch
        {
            return null; // malformed manifest: skip, don't crash the agent
        }
    }

    /// <summary>Strip any path component from a skill name to prevent traversal outside SkillsDirectory.</summary>
    private static string? SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            var safe = Path.GetFileName(name.Trim());
            if (string.IsNullOrWhiteSpace(safe) || safe == "." || safe == "..") return null;
            return safe;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var sub in Directory.EnumerateDirectories(source))
            CopyDirectory(sub, Path.Combine(target, Path.GetFileName(sub)));
    }
}
