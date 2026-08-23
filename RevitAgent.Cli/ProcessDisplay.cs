using System.Text;
using System.Text.RegularExpressions;

namespace RevitAgent.Cli;

/// <summary>
/// Renders the agent's working process (reasoning, tool calls, tool results) as small
/// gray dim lines so the user can watch what the agent is doing, distinct from the final
/// answer (printed in normal color by the command, never here). Streams reasoning tokens
/// live; buffers per-turn <see cref="Microsoft.Extensions.AI.TextContent"/> so text that
/// precedes a tool call shows as a gray "preamble" line, while the trailing text (no
/// subsequent tool call) is the final answer, returned to the caller as a string.
/// <para/>
/// Coordinates with a <see cref="Spinner"/>: the spinner animates during gaps (notably the
/// ~20s Revit execution), and is paused around each printed line so its \r writes can't
/// clobber a gray line. Reasoning keeps the spinner paused for the whole segment.
/// <para/>
/// No-ops entirely when stdout is redirected (ANSI escapes and live framing only make
/// sense on a terminal); the caller still gets the buffered answer string.
/// </summary>
internal sealed class ProcessDisplay
{
    private readonly Spinner? _spinner;
    private bool _reasoningOpen; // a reasoning line is open (spinner paused, no trailing newline)

    // REVIT_AGENT_DEBUG_PROCESS=1 forces the process lines to render even when stdout
    // is redirected, so `revit-agent run ... > log.txt` captures the full reasoning /
    // tool-call / tool-result trace (as plain text — VT can't be enabled on a pipe, so
    // no ANSI color, which is ideal for a log file). Default off: redirected output is
    // just the answer, keeping pipes clean.
    private static readonly bool s_debugForce = Environment.GetEnvironmentVariable("REVIT_AGENT_DEBUG_PROCESS") == "1";
    private static bool ShouldRender => !Console.IsOutputRedirected || s_debugForce;

    public ProcessDisplay(Spinner? spinner)
    {
        _spinner = spinner;
        ConsoleAnsi.EnsureEnabled();
    }

    /// <summary>Live-stream a reasoning token chunk in gray (begins a segment with a marker).</summary>
    public void WriteReasoning(string chunk)
    {
        if (!ShouldRender || string.IsNullOrEmpty(chunk)) return;
        // Collapse internal newlines so a reasoning segment stays one visual line.
        chunk = chunk.Replace("\r", " ").Replace("\n", " ");
        if (!_reasoningOpen)
        {
            _spinner?.Pause();       // freeze spinner, clear its line
            WriteGray("▎ ");          // begin the reasoning line on the cleared line
            _reasoningOpen = true;
        }
        WriteGray(chunk);
    }

    /// <summary>Assistant text emitted before a tool call — show as a gray "thinking" line.</summary>
    public void WritePreamble(string text)
    {
        if (!ShouldRender || string.IsNullOrWhiteSpace(text)) return;
        _spinner?.Pause();
        EndReasoning();
        WriteGrayLine($"▎ {Compact(text)}");
        _spinner?.Resume();
    }

    public void WriteToolCall(string name, object? arguments)
    {
        if (!ShouldRender) return;
        _spinner?.Pause();
        EndReasoning();
        var args = SummarizeArguments(arguments);
        WriteGrayLine(string.IsNullOrEmpty(args) ? $"→ {name}()" : $"→ {name}({args})");
        _spinner?.Resume();
    }

    public void WriteToolResult(object? result)
    {
        if (!ShouldRender) return;
        _spinner?.Pause();
        EndReasoning();
        WriteGrayLine($"← {SummarizeResult(result)}");
        _spinner?.Resume();
    }

    /// <summary>Close any open reasoning line so subsequent output starts on a fresh line.</summary>
    public void CloseLine() => EndReasoning();

    private void EndReasoning()
    {
        if (_reasoningOpen)
        {
            Console.WriteLine();
            _reasoningOpen = false;
        }
    }

    // \x1b[2;90m = SGR dim (2) + bright-black/gray (90). Legacy conhost ignores SGR 2 but
    // honors 90; modern terminals apply both. Falls back to plain text if VT not enabled.
    private static void WriteGray(string s) =>
        Console.Write(ConsoleAnsi.Enabled ? $"\x1b[2;90m{s}\x1b[0m" : s);

    private static void WriteGrayLine(string s)
    {
        WriteGray(s);
        Console.WriteLine();
    }

    /// <summary>Flatten to one line and truncate long values (e.g. a C# source arg or JSON result).</summary>
    private static string Compact(string? s, int max = 80)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var flat = Regex.Replace(s.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
        return flat.Length > max ? flat[..(max - 1)] + "…" : flat;
    }

    /// <summary>Summarize a tool-call argument dictionary (RunRevitCode's <c>source</c> is huge C#).</summary>
    private static string SummarizeArguments(object? arguments)
    {
        if (arguments is null) return "";
        var parts = new List<string>();
        switch (arguments)
        {
            // Covers Dictionary<string,object?>, AIFunctionArguments, etc. — object and
            // object? are the same IL type, so this one pattern matches both nullable and
            // non-nullable argument dictionaries.
            case IEnumerable<KeyValuePair<string, object?>> kvps:
                foreach (var kv in kvps) parts.Add($"{kv.Key}: {Compact(kv.Value?.ToString())}");
                break;
            case System.Collections.IDictionary dict:
                foreach (System.Collections.DictionaryEntry e in dict)
                    parts.Add($"{e.Key}: {Compact(e.Value?.ToString())}");
                break;
        }
        return string.Join(", ", parts);
    }

    private static string SummarizeResult(object? result)
    {
        if (result is null) return "(无返回)";
        var s = Compact(result?.ToString(), 120);
        return string.IsNullOrEmpty(s) ? "(空)" : s;
    }
}
