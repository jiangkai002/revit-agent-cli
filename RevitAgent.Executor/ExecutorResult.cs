using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace RevitAgent.Executor;

/// <summary>
/// Result envelope written by the executor to a file (and stdout) and read back by the CLI.
/// The CLI feeds the JSON form of this object back to the LLM as the tool result.
/// </summary>
/// <remarks>
/// Multi-model batch shape (single model = a 1-element <see cref="Models"/> list): a Revit
/// session is initialized ONCE and reused to run every model sequentially. Per-model success
/// or failure is recorded in <see cref="Models"/>[i]; <see cref="Ok"/> is true only when every
/// model succeeded and there is no top-level <see cref="Error"/>. The top-level <see cref="Error"/>
/// carries a batch-wide catastrophic failure (compilation or Revit injection) where NO model
/// could run — keeping the "LLM fixes code and retries" signal in one place instead of
/// duplicated per model.
/// </remarks>
public sealed class ExecutorResult
{
    public bool Ok { get; set; }

    public List<PerModelResult> Models { get; set; } = new();

    public Summary Summary { get; set; } = new();

    /// <summary>Top-level catastrophic error (compile/inject); null in the per-model path.</summary>
    public ExecutorError? Error { get; set; }
}

public sealed class PerModelResult
{
    /// <summary>Absolute path of the model this entry ran against.</summary>
    public string Model { get; set; } = "";

    public bool Ok { get; set; }

    /// <summary>Whatever the generated command returned for this model; may be null.</summary>
    public object? Data { get; set; }

    public ExecutorError? Error { get; set; }
}

public sealed class Summary
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
}

public sealed class ExecutorError
{
    public string? Type { get; set; }
    public string? Message { get; set; }
    public string? StackTrace { get; set; }

    /// <summary>"compile" | "inject" | "open" | "execute" | "shutdown" | "top"</summary>
    public string? Stage { get; set; }

    /// <summary>
    /// Walks the inner-exception chain (and <see cref="ReflectionTypeLoadException"/> loader
    /// exceptions) so the root cause is visible in the envelope. The injector wraps Revit init
    /// failures in InvalidOperationException, hiding the real reason (e.g. missing native dll,
    /// license, version mismatch); the agent and the user need that root cause to fix the code
    /// or environment. Shared by the per-model path and the top-level failure handler.
    /// </summary>
    public static ExecutorError From(Exception exception, string stage)
    {
        var message = new StringBuilder(exception.Message);
        for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
        {
            message.Append(" --> ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
        }

        if (exception is ReflectionTypeLoadException loaderException)
        {
            foreach (var loader in loaderException.LoaderExceptions)
            {
                if (loader != null)
                {
                    message.Append(" --> Loader: ").Append(loader.Message);
                }
            }
        }

        return new ExecutorError
        {
            Type = exception.GetType().FullName,
            Message = message.ToString(),
            StackTrace = exception.StackTrace,
            Stage = stage
        };
    }
}
