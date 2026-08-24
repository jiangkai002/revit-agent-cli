using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RevitAgent.Cli;

/// <summary>
/// Bridges the net10 agent loop to a net48 x64 executor subprocess: writes the agent-generated
/// C# source + the model list to temp files, spawns the executor, and reads the JSON envelope
/// from the RESULT FILE (never stdout — Revit shutdown noise leaks to stdout after the console
/// silencer is disposed). Serialized by a gate so at most one Revit session runs at a time.
/// </summary>
public sealed class RunRevitCodeTool
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Spawns one executor process for the whole batch. The executor initializes Revit ONCE and
    /// runs every model in <paramref name="modelPaths"/> in that single session (vs. one process
    /// per model), amortizing the ~20s headless Revit init across the batch.
    /// </summary>
    public async Task<string> RunAsync(string source, IReadOnlyList<string> modelPaths, int revitVersion, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        string? tempDir = null;
        Process? process = null;
        try
        {
            var exePath = ExecutorLocator.Find(revitVersion);
            var exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;

            tempDir = Path.Combine(Path.GetTempPath(), "revit-agent-run", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var sourcePath = Path.Combine(tempDir, "Command.cs");
            await File.WriteAllTextAsync(sourcePath, source, ct);

            // One .rvt absolute path per line; the CLI always writes this list (1 line for a
            // single model, N for a batch). The executor reads it and runs all models in one session.
            var modelsListPath = Path.Combine(tempDir, "models.txt");
            await File.WriteAllLinesAsync(modelsListPath, modelPaths, ct);

            var resultPath = Path.Combine(tempDir, "result.json");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                WorkingDirectory = exeDir, // PolyHook2 native dlls resolve beside the exe
                RedirectStandardError = true,
                RedirectStandardOutput = true, // discard the executor's stdout echo; result comes from the file
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(modelsListPath);
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add(resultPath);

            process = new Process { StartInfo = psi };
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) stderr.AppendLine(e.Data);
            };

            process.Start();
            // Drain stdout fully before awaiting exit so a large echo can't fill the
            // pipe and deadlock the child. Content is discarded — the file is the source of truth.
            var stdoutDrain = process.StandardOutput.ReadToEndAsync();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct);
            await stdoutDrain;

            // The executor writes the envelope to resultPath on BOTH success and
            // its own handled-failure paths. Only a catastrophic crash leaves none.
            if (File.Exists(resultPath))
                return await File.ReadAllTextAsync(resultPath, ct);

            return SynthesizeError(
                "ExecutorMissingResult",
                $"执行器退出码 {process.ExitCode} 但未生成结果文件。\nstderr:\n{stderr}",
                "top");
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C while the executor runs: ct cancelled -> WaitForExitAsync threw, but the
            // subprocess keeps running (can't rely on the OS group signal through CREATE_NO_WINDOW
            // + redirected stdio). Kill the whole Revit tree explicitly so it can't orphan, then
            // let the cancellation propagate up to abort the agent loop.
            if (process is not null && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }
        catch (Exception ex)
        {
            return SynthesizeError(ex.GetType().FullName ?? "Exception", ex.Message, "top", ex.StackTrace);
        }
        finally
        {
            process?.Dispose();
            Gate.Release();
            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch { /* best effort */ }
            }
        }
    }

    // CLI-side synthesis (spawn failure / missing result) uses the unified multi-model
    // envelope: no model ran, so the top-level Error carries the cause and Models is empty.
    private static string SynthesizeError(string type, string message, string stage, string? stackTrace = null) =>
        JsonSerializer.Serialize(new
        {
            Ok = false,
            Models = Array.Empty<object>(),
            Summary = new { Total = 0, Succeeded = 0, Failed = 0 },
            Error = new { Type = type, Message = message, StackTrace = stackTrace, Stage = stage }
        });
}
