namespace RevitAgent.Cli;

/// <summary>
/// Per-turn progress sink for one <see cref="AgentHost.AskAsync"/> call, created by an
/// <see cref="IAgentTurnDisplayFactory"/>. Replaces AgentHost's direct use of
/// <see cref="Spinner"/>/<see cref="ProcessDisplay"/> so a GUI host can receive the same
/// events (reasoning deltas, tool calls/results, stage changes) instead of console writes.
/// </summary>
/// <remarks>
/// Implementations MUST be thread-safe: <see cref="OnStage"/> and <see cref="OnExecuting"/>
/// are called from framework threadpool threads inside tool delegates, while the streaming
/// events (<see cref="OnReasoningDelta"/> etc.) arrive on the caller's synchronization
/// context. One instance per turn; dispose when the turn ends.
/// </remarks>
public interface IAgentTurnDisplay : IDisposable
{
    /// <summary>Progress stage changed (e.g. "汇总结果中"); maps to Spinner.SetStage.</summary>
    void OnStage(string stage);

    /// <summary>Code execution started (latches Spinner's "执行中" stage); maps to Spinner.MarkExecuting.</summary>
    void OnExecuting();

    /// <summary>Live reasoning delta (TextReasoningContent chunk), excluded from the final answer.</summary>
    void OnReasoningDelta(string chunk);

    /// <summary>Assistant text produced BEFORE a tool call ("thinking aloud" preamble).</summary>
    void OnPreamble(string text);

    /// <summary>Tool invocation (function name + raw arguments object).</summary>
    void OnToolCall(string name, object? arguments);

    /// <summary>Tool result (raw result object; RunRevitCode returns a JSON envelope string).</summary>
    void OnToolResult(object? result);

    /// <summary>The stream ended successfully (close any open line).</summary>
    void OnTurnCompleted();
}

/// <summary>Creates one <see cref="IAgentTurnDisplay"/> per agent turn.</summary>
public interface IAgentTurnDisplayFactory
{
    IAgentTurnDisplay BeginTurn(CancellationToken ct);
}
