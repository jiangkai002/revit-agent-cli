using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Nice3point.Revit.Injector;
using RevitAgent.DynamicCode;

namespace RevitAgent.Executor;

/// <summary>
/// Opens isolated copies of one or more Revit models in a single UI-less Revit application
/// session and runs a dynamically compiled command against each. Reproduces the proven
/// TestRun/WithDocument lifecycle, lifted to a batch: inject ONCE → per model (temp copy →
/// open → run → close → delete temp) → eject ONCE. A Revit engine init (~20s) is amortized
/// across the whole batch instead of paid per model.
/// </summary>
public sealed class RevitCodeRunner
{
    private readonly IReadOnlyList<string> _modelPaths;

    public RevitCodeRunner(IReadOnlyList<string> modelPaths)
    {
        if (modelPaths is null || modelPaths.Count == 0)
        {
            throw new ArgumentException("模型路径列表不能为空。", nameof(modelPaths));
        }

        _modelPaths = modelPaths;
    }

    /// <summary>
    /// Compiles the source once, then runs a fresh command instance against each model in a
    /// single Revit session. Never throws for compile/inject/per-model failures — those are
    /// captured into the returned envelope (top-level <see cref="ExecutorResult.Error"/> for
    /// compile/inject; <see cref="PerModelResult.Error"/> per model) so a partial batch still
    /// produces a result file.
    /// </summary>
    public ExecutorResult ExecuteDynamicCode(string sourceCode)
    {
        Type commandType;
        try
        {
            commandType = DynamicCodeCompiler.CompileType(sourceCode);
        }
        catch (Exception ex)
        {
            return TopLevelError(ex, "compile");
        }

        List<PerModelResult> models;
        try
        {
            models = WithDocuments(document =>
            {
                // Fresh instance per model so LLM-generated scripts can't leak instance state
                // (counters, cached lists) across models. Compilation is the expensive part and
                // happens once above; instantiation is essentially free.
                var command = (IRevitDynamicCommand)Activator.CreateInstance(commandType)!;
                return command.Execute(document);
            });
        }
        catch (Exception ex)
        {
            // InjectApplication (or eject) failed before/after the loop — no model could run.
            return TopLevelError(ex, "inject");
        }

        var summary = new Summary
        {
            Total = models.Count,
            Succeeded = models.Count(model => model.Ok),
            Failed = models.Count(model => !model.Ok)
        };

        return new ExecutorResult
        {
            Ok = summary.Failed == 0,
            Models = models,
            Summary = summary
        };
    }

    private List<PerModelResult> WithDocuments(Func<Document, object?> perDocument)
    {
        var results = new List<PerModelResult>(_modelPaths.Count);
        Injector? injector = null;
        try
        {
            injector = new Injector();
            var application = injector.InjectApplication(); // Revit engine init ONCE (outside the loop)

            foreach (var modelPath in _modelPaths)
            {
                var absolutePath = Path.GetFullPath(modelPath);
                string? isolatedPath = null;
                Document? document = null;
                try
                {
                    isolatedPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.rvt");
                    File.Copy(modelPath, isolatedPath, overwrite: true);
                    document = application.OpenDocumentFile(isolatedPath);

                    var data = perDocument(document);
                    results.Add(new PerModelResult
                    {
                        Model = absolutePath,
                        Ok = true,
                        Data = data
                    });
                }
                catch (Exception exception)
                {
                    // Distinguish the open stage (copy/open) from the execute stage (script run):
                    // document is still null when OpenDocumentFile threw before assigning it.
                    var stage = document is null ? "open" : "execute";
                    results.Add(new PerModelResult
                    {
                        Model = absolutePath,
                        Ok = false,
                        Error = ExecutorError.From(exception, stage)
                    });
                }
                finally
                {
                    // Per-model cleanup so temp copies never accumulate across a large batch.
                    try { document?.Close(false); } catch { /* close failure doesn't block the next model */ }
                    if (isolatedPath != null && File.Exists(isolatedPath))
                    {
                        try
                        {
                            File.SetAttributes(isolatedPath, FileAttributes.Normal);
                            File.Delete(isolatedPath);
                        }
                        catch { /* temp cleanup failure doesn't affect the result */ }
                    }
                }
            }
        }
        finally
        {
            // Eject ONCE at batch end. Shutdown noise is ignored so it can't mask real results.
            try { injector?.EjectApplication(); } catch { /* shutdown noise ignored */ }
        }

        return results;
    }

    private static ExecutorResult TopLevelError(Exception exception, string stage) => new()
    {
        Ok = false,
        Models = new(),
        Summary = new(),
        Error = ExecutorError.From(exception, stage)
    };
}
