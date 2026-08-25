using System.Text.Json;
using System.Windows.Threading;
using RevitAgent.Cli;
using RevitAgent.Gui.Models;

namespace RevitAgent.Gui.Services;

/// <summary>
/// GUI <see cref="IAgentTurnDisplayFactory"/>: every AgentHost display callback becomes a
/// <see cref="TurnEvent"/> posted to the captured UI <see cref="Dispatcher"/>, from wherever
/// it was raised (the streaming loop resumes on the Dispatcher, but the tool delegates call
/// OnExecuting/OnStage from threadpool threads — so marshal unconditionally). Same-priority
/// posts keep the arrival order.
/// </summary>
public sealed class GuiTurnDisplayFactory(Action<TurnEvent> sink) : IAgentTurnDisplayFactory
{
    private readonly Action<TurnEvent> _sink = sink;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher; // constructed on the UI thread

    public IAgentTurnDisplay BeginTurn(CancellationToken ct) => new DispatcherTurnDisplay(_dispatcher, _sink);

    private sealed class DispatcherTurnDisplay(Dispatcher dispatcher, Action<TurnEvent> sink) : IAgentTurnDisplay
    {
        public void OnStage(string stage) => Post(new StageEvent(stage));
        public void OnExecuting() => Post(new ExecutingEvent());
        public void OnReasoningDelta(string chunk) => Post(new ReasoningDeltaEvent(chunk));
        public void OnPreamble(string text) => Post(new PreambleEvent(text));
        public void OnToolCall(string name, object? arguments) => Post(new ToolCallEvent(name, FormatArguments(arguments)));
        public void OnToolResult(object? result) => Post(new ToolResultEvent(FormatResult(result)));
        public void OnTurnCompleted() => Post(new TurnCompletedEvent());
        public void Dispose() { }

        private void Post(TurnEvent e) => dispatcher.InvokeAsync(() => sink(e));
    }

    /// <summary>Flatten tool-call arguments (AIFunctionArguments derives from
    /// Dictionary&lt;string,object?&gt;) to full "key = value" lines — no console-style
    /// truncation; the GUI shows everything.</summary>
    internal static string FormatArguments(object? arguments)
    {
        if (arguments is null) return "(无参数)";
        if (arguments is IDictionary<string, object?> dict)
        {
            if (dict.Count == 0) return "(无参数)";
            return string.Join("\n", dict.Select(kv => $"{kv.Key} = {FormatValue(kv.Value)}"));
        }
        return arguments.ToString() ?? "(无参数)";
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "(null)";
        if (value is string s) return s; // the C# source, shown in full
        return value.ToString() ?? "";
    }

    /// <summary>Tool results are JSON strings (RunRevitCode/ExportCsv envelopes); pretty-print
    /// them for display. Anything non-JSON passes through unchanged.</summary>
    internal static string FormatResult(object? result)
    {
        var text = result?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return "(无返回)";
        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
