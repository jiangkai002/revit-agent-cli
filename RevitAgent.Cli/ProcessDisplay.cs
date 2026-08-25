using System.Text;
using System.Text.RegularExpressions;

namespace RevitAgent.Cli;

/// <summary>
/// Renders the agent's working process (reasoning, tool calls, tool results) as small
/// gray dim lines so the user can watch what the agent is doing, distinct from the final
/// answer (printed in normal color by the command, never here). Streams reasoning tokens
/// into a fixed 3-line window that scrolls in place (only the last 3 display-width-wrapped
/// lines stay visible), so a long chain-of-thought can't flood the screen; buffers per-turn
/// <see cref="Microsoft.Extensions.AI.TextContent"/> so text that precedes a tool call shows
/// as a gray "preamble" line, while the trailing text (no subsequent tool call) is the final
/// answer, returned to the caller as a string.
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
    private bool _reasoningOpen; // a reasoning box is open (spinner paused, no trailing newline)
    private readonly StringBuilder _reasoningBuf = new(); // accumulated reasoning text (box path)
    private int _boxLinesDrawn; // visible lines currently on screen for the reasoning box (cursor-up math)
    private const int ReasoningBoxLines = 3; // fixed height of the scrolling reasoning window

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

    /// <summary>Live-stream a reasoning token chunk into a fixed 3-line gray window that
    /// scrolls in place (only the last 3 lines stay visible). Falls back to a single
    /// growing line when VT is unavailable (redirected output / debug log / tiny terminal).</summary>
    public void WriteReasoning(string chunk)
    {
        if (!ShouldRender || string.IsNullOrEmpty(chunk)) return;

        // BufferWidth throws "句柄无效" (invalid handle) when stdout is redirected: a pipe/file
        // is not a console screen buffer, so querying its size fails. The REVIT_AGENT_DEBUG_PROCESS
        // redirect path forces ShouldRender=true AND the model may emit reasoning, landing here on
        // a pipe. The non-VT fallback below is taken whenever ConsoleAnsi is off (always on a pipe),
        // so it never uses `width` for wrapping — feed 80 on redirect and only touch the real
        // console handle when a buffer actually exists. Real-console behavior is unchanged.
        int width = Console.IsOutputRedirected ? 80 : (Console.BufferWidth > 0 ? Console.BufferWidth : 80);
        if (!ConsoleAnsi.Enabled || width < 20)
        {
            // No VT (or too narrow) -> can't rewrite a box in place. Collapse newlines and
            // append to one gray line. The debug-log path (REVIT_AGENT_DEBUG_PROCESS=1 on a
            // pipe) lands here too, capturing the full trace as plain text without ANSI
            // cursor noise -- ideal for `revit-agent run ... > log.txt`.
            chunk = chunk.Replace("\r", " ").Replace("\n", " ");
            if (!_reasoningOpen)
            {
                _spinner?.Pause();      // freeze spinner, clear its line
                WriteGray("▎ ");        // begin the reasoning line on the cleared line
                _reasoningOpen = true;
            }
            WriteGray(chunk);
            return;
        }

        // VT path: render the reasoning into a fixed 3-line window that scrolls in place.
        // Only the last 3 (display-width-wrapped) lines stay visible; older text scrolls
        // off the top, so a long chain-of-thought can't flood the screen.
        _reasoningBuf.Append(chunk);
        if (!_reasoningOpen)
        {
            _spinner?.Pause();          // freeze spinner for the whole segment
            _reasoningOpen = true;
            _boxLinesDrawn = 0;
        }
        RenderReasoningBox();
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
            Console.WriteLine();        // move past the box so the next line prints below it
            _reasoningOpen = false;
            _reasoningBuf.Clear();
            _boxLinesDrawn = 0;
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

    /// <summary>Redraw the reasoning box in place: wrap the accumulated text to the terminal
    /// width, keep only the last <see cref="ReasoningBoxLines"/> lines, move the cursor to
    /// the top of the previously-drawn box, clear the region, and reprint. The box is always
    /// the last thing on screen (spinner paused, tool calls come after EndReasoning), so
    /// clearing to end-of-screen can't clobber anything.</summary>
    private void RenderReasoningBox()
    {
        int width = Console.BufferWidth > 0 ? Console.BufferWidth : 80;
        int wrap = Math.Max(10, width - 2); // reserve 2 cols for the "▎ " / "  " gutter

        var lines = WrapLines(_reasoningBuf.ToString(), wrap);
        int take = Math.Min(ReasoningBoxLines, lines.Count);
        int start = lines.Count - take;

        if (_boxLinesDrawn > 0)
            Console.Write($"\x1b[{_boxLinesDrawn - 1}A"); // cursor up to the box's top line
        Console.Write("\r\x1b[J");                          // col 0, then clear to end-of-screen

        for (int i = 0; i < take; i++)
        {
            string prefix = i == 0 ? "▎ " : "  ";          // marker on the top visible line, indent the rest
            Console.Write($"\x1b[2;90m{prefix}{lines[start + i]}\x1b[0m");
            if (i < take - 1) Console.Write("\r\n");
        }
        _boxLinesDrawn = take;
    }

    /// <summary>Split text into display-width-bounded lines (newlines preserved as breaks,
    /// each paragraph hard-wrapped at <paramref name="maxDisplayWidth"/>). East Asian wide
    /// chars count as 2 columns so CJK reasoning doesn't overflow the 3-line window.</summary>
    private static List<string> WrapLines(string text, int maxDisplayWidth)
    {
        var result = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(para)) continue;
            var line = new StringBuilder();
            int lineW = 0;
            foreach (var ch in para)
            {
                int cw = CharWidth(ch);
                if (lineW + cw > maxDisplayWidth && line.Length > 0)
                {
                    result.Add(line.ToString().TrimEnd());
                    line.Clear();
                    lineW = 0;
                }
                line.Append(ch);
                lineW += cw;
            }
            if (line.Length > 0) result.Add(line.ToString().TrimEnd());
        }
        return result;
    }

    // East Asian wide/fullwidth chars take 2 console columns, everything else 1. Drives the
    // box wrap so CJK reasoning text stays inside the fixed 3-line window.
    private static int CharWidth(char ch)
    {
        if (ch < 0x1100) return 1;
        if (ch <= 0x115F) return 2;
        if (ch >= 0x2E80 && ch <= 0x303E) return 2;
        if (ch >= 0x3040 && ch <= 0x33BF) return 2;
        if (ch >= 0x3400 && ch <= 0x4DBF) return 2;
        if (ch >= 0x4E00 && ch <= 0x9FFF) return 2;
        if (ch >= 0xA000 && ch <= 0xA4CF) return 2;
        if (ch >= 0xAC00 && ch <= 0xD7A3) return 2;
        if (ch >= 0xF900 && ch <= 0xFAFF) return 2;
        if (ch >= 0xFE30 && ch <= 0xFE4F) return 2;
        if (ch >= 0xFF00 && ch <= 0xFF60) return 2;
        if (ch >= 0xFFE0 && ch <= 0xFFE6) return 2;
        return 1;
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
