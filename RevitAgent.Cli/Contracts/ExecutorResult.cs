using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitAgent.Cli.Contracts;

/// <summary>
/// Mirrors the executor's envelope (RevitAgent.Executor.ExecutorResult) for parsing. Property
/// names are matched case-insensitively (System.Text.Json default). NOTE: this DTO is currently
/// unreferenced at runtime — RunRevitCodeTool returns the raw JSON string and ExportCsvTool
/// parses via JsonDocument by property name. Kept accurate for documentation; update it if the
/// envelope is ever deserialized into this type.
/// </summary>
public sealed class ExecutorResult
{
    [JsonPropertyName("Ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("Models")]
    public List<PerModelResult> Models { get; set; } = new();

    [JsonPropertyName("Summary")]
    public Summary Summary { get; set; } = new();

    [JsonPropertyName("Error")]
    public ExecutorError? Error { get; set; }
}

public sealed class PerModelResult
{
    [JsonPropertyName("Model")] public string Model { get; set; } = "";
    [JsonPropertyName("Ok")] public bool Ok { get; set; }
    [JsonPropertyName("Data")] public JsonElement? Data { get; set; }
    [JsonPropertyName("Error")] public ExecutorError? Error { get; set; }
}

public sealed class Summary
{
    [JsonPropertyName("Total")] public int Total { get; set; }
    [JsonPropertyName("Succeeded")] public int Succeeded { get; set; }
    [JsonPropertyName("Failed")] public int Failed { get; set; }
}

public sealed class ExecutorError
{
    [JsonPropertyName("Type")] public string? Type { get; set; }
    [JsonPropertyName("Message")] public string? Message { get; set; }
    [JsonPropertyName("StackTrace")] public string? StackTrace { get; set; }
    [JsonPropertyName("Stage")] public string? Stage { get; set; }
}
