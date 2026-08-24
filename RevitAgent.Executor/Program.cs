using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Nice3point.Revit.Injector;
using RevitAgent.Executor;

// Resolve Revit assemblies for the dynamically compiled command. The injector loads
// RevitAPI into the LoadFrom context; the compiled assembly's metadata reference is a
// default-context binding that the CLR does not auto-unify, so we satisfy it here by
// returning the already-loaded assembly (or loading it from the install dir).
AppDomain.CurrentDomain.AssemblyResolve += static (sender, e) =>
{
    var name = new AssemblyName(e.Name).Name ?? string.Empty;
    if (!name.StartsWith("Revit", StringComparison.Ordinal))
    {
        return null;
    }

    var loaded = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == name);
    if (loaded is not null)
    {
        return loaded;
    }

    var candidate = Path.Combine(RevitEnvironment.EffectiveInstallationPath, name + ".dll");
    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
};

// Resolve arguments up front so the failure handler can write the result file even
// when the STA-threaded Revit run throws. args[0] is now a models-list path: a .rvt file
// (single-model batch) or a text file with one .rvt absolute path per line (the CLI
// always writes this list). The REVIT_MODEL_PATH env fallback is treated as one .rvt path.
var modelsListPath = ResolveArg(args, 0, "REVIT_MODEL_PATH");
var sourcePath = ResolveArg(args, 1, "REVIT_DYNAMIC_CODE_PATH");
var resultPath = ResolveArg(args, 2, "REVIT_DYNAMIC_RESULT_PATH");

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        Run(modelsListPath, sourcePath, resultPath);
    }
    catch (Exception exception)
    {
        failure = exception;
    }
});

thread.SetApartmentState(ApartmentState.STA); // Revit requires an STA thread
thread.Start();
thread.Join();

if (failure != null)
{
    WriteResult(resultPath, new ExecutorResult
    {
        Ok = false,
        Models = new(),
        Summary = new(),
        Error = ExecutorError.From(failure, "top")
    });

    Console.Error.WriteLine("运行失败：");
    PrintException(failure, 0);
    Environment.Exit(1);
}

static void Run(string modelsListPath, string sourcePath, string? resultPath)
{
    if (string.IsNullOrWhiteSpace(modelsListPath))
    {
        throw new ArgumentException("缺少模型路径。请通过参数（模型或列表文件）或 REVIT_MODEL_PATH 提供。");
    }
    if (string.IsNullOrWhiteSpace(sourcePath))
    {
        throw new ArgumentException("缺少动态代码路径。请通过参数或 REVIT_DYNAMIC_CODE_PATH 提供。");
    }
    if (!File.Exists(sourcePath))
    {
        throw new FileNotFoundException("找不到动态代码文件。", sourcePath);
    }

    var modelPaths = ResolveModelPaths(modelsListPath);
    if (modelPaths.Count == 0)
    {
        throw new ArgumentException("未找到可执行的 Revit 模型文件。");
    }

    var sourceCode = File.ReadAllText(sourcePath);
    var runner = new RevitCodeRunner(modelPaths);

    // ExecuteDynamicCode never throws for compile/inject/per-model failures — it captures
    // them into the envelope (top-level Error for compile/inject, per-model Error otherwise).
    // Only truly unexpected throws (bad source path, read failure) escape to the handler above.
    var result = runner.ExecuteDynamicCode(sourceCode);
    WriteResult(resultPath, result);
}

// Resolves the batch model list. Dual mode: a .rvt file path → single-model batch; any
// other existing file → one absolute .rvt path per line (blank lines skipped, missing
// files warned + skipped). The env fallback value is a single .rvt path.
static List<string> ResolveModelPaths(string modelsListPath)
{
    var paths = new List<string>();
    if (string.IsNullOrWhiteSpace(modelsListPath))
    {
        return paths;
    }

    var isRvt = modelsListPath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase);
    if (isRvt && File.Exists(modelsListPath))
    {
        paths.Add(Path.GetFullPath(modelsListPath));
        return paths;
    }

    if (File.Exists(modelsListPath))
    {
        foreach (var line in File.ReadAllLines(modelsListPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (File.Exists(trimmed))
            {
                paths.Add(Path.GetFullPath(trimmed));
            }
            else
            {
                Console.Error.WriteLine($"警告：跳过不存在的模型路径: {trimmed}");
            }
        }
    }
    else
    {
        Console.Error.WriteLine($"警告：模型路径不存在: {modelsListPath}");
    }

    return paths;
}

static void WriteResult(string? resultPath, ExecutorResult result)
{
    var json = new JavaScriptSerializer
    {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 100
    }.Serialize(result);

    if (!string.IsNullOrWhiteSpace(resultPath))
    {
        var resultDirectory = Path.GetDirectoryName(resultPath);
        if (!string.IsNullOrWhiteSpace(resultDirectory))
        {
            Directory.CreateDirectory(resultDirectory);
        }
        File.WriteAllText(resultPath, json, Encoding.UTF8);
    }

    // Best-effort stdout echo for direct runs; the CLI MUST read the result FILE,
    // never stdout, because Revit shutdown noise leaks here after the console silencer
    // (active only during InjectApplication) is disposed. Do NOT set OutputEncoding: the
    // result FILE is already UTF-8 via File.WriteAllText above (independent of the console
    // CP), and mutating the shared console's output codepage here pollutes the caller's
    // terminal (left at 65001 → later typed Chinese mojibakes). Echo in whatever the
    // console already is; the CLI drains+discards this stdout anyway.
    Console.Out.WriteLine(json);
}

static string ResolveArg(string[] args, int index, string envVar)
{
    if (args.Length > index && !string.IsNullOrWhiteSpace(args[index]))
    {
        return args[index]!;
    }

    return Environment.GetEnvironmentVariable(envVar) ?? string.Empty;
}

static void PrintException(Exception exception, int depth)
{
    var prefix = new string(' ', depth * 2);
    Console.Error.WriteLine($"{prefix}Type: {exception.GetType().FullName}");

    try
    {
        Console.Error.WriteLine($"{prefix}Message: {exception.Message}");
    }
    catch
    {
        Console.Error.WriteLine($"{prefix}Message: <读取失败>");
    }

    if (exception is System.Reflection.ReflectionTypeLoadException loaderException)
    {
        foreach (var loader in loaderException.LoaderExceptions)
        {
            if (loader != null)
            {
                PrintException(loader, depth + 1);
            }
        }
    }

    if (exception.InnerException != null)
    {
        PrintException(exception.InnerException, depth + 1);
    }
}
