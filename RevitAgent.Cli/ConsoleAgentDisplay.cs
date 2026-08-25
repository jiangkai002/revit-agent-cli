namespace RevitAgent.Cli;

/// <summary>
/// The console default <see cref="IAgentTurnDisplayFactory"/>: wraps <see cref="Spinner"/> +
/// <see cref="ProcessDisplay"/> plus the 2.5s "分析需求中→写代码中" stage heuristic,
/// reproducing the pre-abstraction AgentHost console behavior exactly. AgentHost falls back
/// to this when no factory is injected, so the CLI's calls sites stay unchanged.
/// </summary>
public sealed class ConsoleAgentDisplayFactory : IAgentTurnDisplayFactory
{
    public IAgentTurnDisplay BeginTurn(CancellationToken ct) => new ConsoleTurnDisplay(ct);

    private sealed class ConsoleTurnDisplay : IAgentTurnDisplay
    {
        private readonly Spinner _spinner;
        private readonly ProcessDisplay _display;
        private readonly CancellationTokenSource _analyzeCts;

        public ConsoleTurnDisplay(CancellationToken ct)
        {
            _spinner = new Spinner("分析需求中");
            _display = new ProcessDisplay(_spinner);

            // Heuristic (moved verbatim from AgentHost): after 2.5s of no tool activity the
            // agent is presumed to be writing code, so the spinner stage moves on. Cancelled
            // (turn ended earlier) or raced (tool already executing → MarkExecuting latched
            // the stage) → the transition is a no-op.
            _analyzeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2500, _analyzeCts.Token);
                    _spinner.TransitionToWritingIfStillAnalyzing();
                }
                catch (OperationCanceledException) { }
            });
        }

        public void OnStage(string stage) => _spinner.SetStage(stage);
        public void OnExecuting() => _spinner.MarkExecuting();
        public void OnReasoningDelta(string chunk) => _display.WriteReasoning(chunk);
        public void OnPreamble(string text) => _display.WritePreamble(text);
        public void OnToolCall(string name, object? arguments) => _display.WriteToolCall(name, arguments);
        public void OnToolResult(object? result) => _display.WriteToolResult(result);
        public void OnTurnCompleted() => _display.CloseLine();

        public void Dispose()
        {
            _analyzeCts.Cancel();
            _spinner.Dispose(); // stops the animation, clears the spinner line
        }
    }
}
