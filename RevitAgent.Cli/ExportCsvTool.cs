using System.IO;
using System.Text;
using System.Text.Json;

namespace RevitAgent.Cli;

/// <summary>
/// Serializes a <see cref="RunRevitCodeTool"/> result — the JSON envelope's per-model <c>Data</c>,
/// expected to be a list of flat row objects — to a CSV file. The envelope is multi-model:
/// <c>Models[]</c> each carrying that model's row list. This tool concatenates every model's rows
/// into one CSV and prepends a <c>Model</c> column (the model's file name) so rows from different
/// models stay distinguishable. CSV writing happens CLI-side (net10), not in the net48 executor.
/// </summary>
internal static class ExportCsvTool
{
    /// <summary>
    /// Parse the RunRevitCode JSON envelope, concatenate every model's Data rows into one CSV with
    /// a leading Model column, write to <paramref name="path"/>. Returns a concise Chinese status
    /// message for the agent.
    /// </summary>
    public static string Export(string envelopeJson, string path)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        var root = doc.RootElement;

        // Top-level Error (compile/inject) or a fully-failed batch → nothing to export.
        if (!root.TryGetProperty("Ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var msg = root.TryGetProperty("Error", out var err) &&
                      err.TryGetProperty("Message", out var m)
                ? m.GetString() ?? "(未知错误)"
                : "(未知错误)";
            return $"代码执行失败，未导出 CSV: {msg}";
        }

        if (!root.TryGetProperty("Models", out var models) || models.ValueKind != JsonValueKind.Array)
            return "执行结果中无 Models 数组，未导出。";

        // Flatten: (modelName, row) per row across all models that returned a list.
        var rows = new List<(string Model, JsonElement Row)>();
        var failedModels = 0;
        var usedModels = 0;
        foreach (var model in models.EnumerateArray())
        {
            if (!model.TryGetProperty("Ok", out var mOk) || mOk.ValueKind != JsonValueKind.True)
            {
                failedModels++;
                continue;
            }
            if (!model.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                // Data isn't a list for this model — counts as unusable, not a row source.
                failedModels++;
                continue;
            }

            var modelName = model.TryGetProperty("Model", out var mProp) &&
                            mProp.ValueKind == JsonValueKind.String
                ? Path.GetFileName(mProp.GetString() ?? "")
                : "";

            foreach (var row in data.EnumerateArray())
                rows.Add((modelName, row));
            usedModels++;
        }

        if (rows.Count == 0)
        {
            var note = failedModels > 0 ? $"（{failedModels} 个模型失败或无列表数据）" : "";
            return $"数据为空（0 行）{note}，未导出文件。";
        }

        // Collect the union of column names across all rows, first-seen order, with Model always
        // first. Falls back to a single "Value" column if the rows are primitives.
        var columns = new List<string> { "Model" };
        var seen = new HashSet<string> { "Model" };
        var allObjects = true;
        foreach (var (_, row) in rows)
        {
            if (row.ValueKind != JsonValueKind.Object) { allObjects = false; break; }
            foreach (var prop in row.EnumerateObject())
                if (seen.Add(prop.Name)) columns.Add(prop.Name);
        }

        var sb = new StringBuilder();
        if (!allObjects)
        {
            sb.AppendLine("Model,Value");
            foreach (var (model, row) in rows)
                sb.AppendLine($"{Escape(model)},{Format(row)}");
        }
        else
        {
            sb.AppendLine(string.Join(",", columns.Select(Escape)));
            foreach (var (model, row) in rows)
            {
                var cells = new List<string>(columns.Count) { Escape(model) };
                foreach (var col in columns.Skip(1)) // skip Model, already written
                {
                    row.TryGetProperty(col, out var val);
                    cells.Add(Format(val));
                }
                sb.AppendLine(string.Join(",", cells));
            }
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // UTF-8 with BOM so Excel renders Chinese (房间名/参数名) correctly.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var colCount = allObjects ? columns.Count : 2;
            var modelNote = usedModels > 1 ? $"，来自 {usedModels} 个模型" : "";
            var skipNote = failedModels > 0 ? $"（跳过 {failedModels} 个失败模型）" : "";
            return $"已导出 {rows.Count} 行（{colCount} 列{modelNote}）到 {path}{skipNote}";
        }
        catch (Exception ex)
        {
            return $"写入 CSV 失败: {ex.Message}";
        }
    }

    private static string Format(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return "";
            case JsonValueKind.String:
                return Escape(el.GetString() ?? "");
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return el.GetRawText();
            default:
                // Object/array — flatten to raw JSON text (escape as needed).
                return Escape(el.GetRawText());
        }
    }

    // RFC 4180 quoting: wrap in double quotes if the value contains comma, quote, or
    // newline; double up any internal quotes.
    private static string Escape(string? s)
    {
        if (s is null) return "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }
}
