using System.Text.Json;

namespace RevitAgent.Cli;

/// <summary>
/// User-editable configuration stored at %APPDATA%\revit-agent\config.json.
/// The API key is NEVER stored here — only the NAME of the env var that holds it.
/// </summary>
public sealed class AgentConfig
{
    public string Provider { get; set; } = "openai";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKeyEnv { get; set; } = "REVIT_AGENT_API_KEY";
    public int DefaultRevitVersion { get; set; } = 2022;
    public string DefaultModelPath { get; set; } = "";
}

public static class ConfigStore
{
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "revit-agent");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AgentConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AgentConfig();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AgentConfig>(json, JsonOpts) ?? new AgentConfig();
        }
        catch
        {
            return new AgentConfig();
        }
    }

    public static void Save(AgentConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(ConfigPath, json);
    }
}
