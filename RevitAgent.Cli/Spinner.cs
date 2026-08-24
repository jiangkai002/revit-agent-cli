namespace RevitAgent.Cli;

/// <summary>
/// Console progress indicator: animates a |\-/ spinner prefixed with a stage label,
/// so the user sees the agent is alive and what it is doing instead of a silent wait.
/// Stages are pushed by AgentHost and the RunRevitCode tool wrapper. Once the tool
/// starts executing, "写代码中" pushes are ignored so they can't clobber "执行中".
/// <para/>
/// <see cref="Pause"/>/<see cref="Resume"/> let the streaming process display freeze
/// the animation and clear its line so a full event line can be printed without being
/// clobbered by the spinner's \r writes; the spinner resumes on the new line afterward.
/// All console writes (animation + pause-clear) are under <see cref="_lock"/> so a pause
/// can never interleave with a half-written spinner frame.
/// </summary>
public sealed class Spinner : IDisposable
{
    private static readonly char[] Frames = { '|', '/', '-', '\\' };
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;
    private string _stage;
    private bool _executing; // latched true once the tool starts; blocks the analyze→write transition
    private bool _paused;     // animation halted and its line cleared so other output can print cleanly
    private bool _lineDirty;  // a spinner frame is on the console (needs clearing before other output / on stop)
    private readonly object _lock = new();

    public Spinner(string initialStage = "分析需求中")
    {
        _stage = initialStage;
        _task = Task.Run(Spin);
    }

    /// <summary>Set the current stage label (e.g. 汇总结果中).</summary>
    public void SetStage(string stage)
    {
        lock (_lock) _stage = stage;
    }

    /// <summary>Transition analyze → write-code, but only if the tool hasn't started yet.</summary>
    public void TransitionToWritingIfStillAnalyzing()
    {
        lock (_lock) { if (!_executing) _stage = "写代码中"; }
    }

    /// <summary>Latch "executing": subsequent write-code transitions are ignored.</summary>
    public void MarkExecuting()
    {
        lock (_lock) { _executing = true; _stage = "执行中"; }
    }

    /// <summary>Freeze the animation and clear its line so a caller can print a full line cleanly. Pairs with <see cref="Resume"/>.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            _paused = true;
            ClearLineLocked();
        }
    }

    /// <summary>Resume the animation on the current line (stage unchanged).</summary>
    public void Resume()
    {
        lock (_lock) _paused = false;
    }

    private void ClearLineLocked()
    {
        // 60 spaces covers any plausible stage+frame width; \r returns to column 0 first.
        if (_lineDirty && !Console.IsOutputRedirected)
        {
            Console.Write("\r" + new string(' ', 60) + "\r");
            _lineDirty = false;
        }
    }

    private async Task Spin()
    {
        // Don't animate when stdout is redirected (pipe/file): \r overwrites only
        // make sense on a terminal, and would otherwise litter piped output.
        if (Console.IsOutputRedirected) return;

        var i = 0;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                lock (_lock)
                {
                    if (!_paused)
                    {
                        // \r rewrites the line; trailing spaces clear stale tail.
                        Console.Write($"\r{_stage}  {Frames[i]}  ");
                        _lineDirty = true;
                        i = (i + 1) % Frames.Length;
                    }
                }
                try { await Task.Delay(150, _cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch
        {
            // progress is best-effort; never throw out of the spinner thread
        }
    }

    /// <summary>Stop the animation and clear the spinner line (only on a terminal).</summary>
    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
        try { _task.Wait(1000); } catch { }
        lock (_lock) ClearLineLocked();
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
