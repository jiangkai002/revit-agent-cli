using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nice3point.Revit.Injector;
using RevitAgent.DynamicCode;

namespace RevitAgent.Executor;

/// <summary>
/// Compiles generated C# source into a transient assembly that implements
/// <see cref="IRevitDynamicCommand"/>, using Roslyn (<c>CSharpCompilation</c>) so
/// modern C# syntax (?. , $"", nameof, pattern matching, records, ...) is supported.
/// References every assembly already loaded in this process — notably the injector's
/// RevitAPI/RevitAPIUI (loaded via LoadFrom) and this executor assembly hosting the
/// interface — guaranteeing the generated code binds to the same Revit type identity
/// used at runtime. (See <c>Program.cs</c> AssemblyResolve for the runtime bridge.)
/// </summary>
public static class DynamicCodeCompiler
{
    /// <summary>
    /// Compiles generated C# source into the transient command <see cref="Type"/>. Roslyn
    /// compilation is the expensive part; callers compile once and instantiate fresh per
    /// document via <see cref="Activator.CreateInstance"/> (see <see cref="RevitCodeRunner"/>),
    /// guaranteeing per-model instance isolation without recompiling.
    /// </summary>
    public static Type CompileType(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new ArgumentException("Dynamic source cannot be empty.", nameof(sourceCode));

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions);

        var compilation = CSharpCompilation.Create(
            "RevitDynamicCommand_" + Guid.NewGuid().ToString("N"),
            new[] { syntaxTree },
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic =>
                {
                    var pos = diagnostic.Location.GetLineSpan().StartLinePosition;
                    return $"Line {pos.Line + 1}, Col {pos.Character + 1}: {diagnostic.Id} {diagnostic.GetMessage()}";
                });
            throw new InvalidOperationException(
                "Dynamic code compilation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        var assembly = Assembly.Load(peStream.ToArray());
        var commandType = assembly.GetTypes()
            .FirstOrDefault(type => typeof(IRevitDynamicCommand).IsAssignableFrom(type) && !type.IsAbstract);

        if (commandType == null)
            throw new InvalidOperationException(
                $"Dynamic code must define a type implementing {nameof(IRevitDynamicCommand)}.");

        return commandType;
    }

    /// <summary>Compiles and returns a single command instance (convenience over <see cref="CompileType"/>).</summary>
    public static IRevitDynamicCommand Compile(string sourceCode)
        => (IRevitDynamicCommand)Activator.CreateInstance(CompileType(sourceCode))!;

    private static IEnumerable<MetadataReference> BuildReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (references.ContainsKey(path)) return;
            references[path] = MetadataReference.CreateFromFile(path);
        }

        // Every assembly loaded in this process: covers RevitAPI/RevitAPIUI (loaded by
        // the injector via LoadFrom), mscorlib, System, System.Core, this executor
        // assembly hosting IRevitDynamicCommand, and Roslyn's own dependencies.
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (loaded.IsDynamic) continue;
            Add(loaded.Location);
        }

        // Explicit guarantees (by resolved path): Revit install + framework + interface.
        var installPath = RevitEnvironment.EffectiveInstallationPath;
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        Add(typeof(IRevitDynamicCommand).Assembly.Location);
        Add(typeof(object).Assembly.Location);
        Add(typeof(Enumerable).Assembly.Location);
        Add(Path.Combine(installPath, "RevitAPI.dll"));
        Add(Path.Combine(installPath, "RevitAPIUI.dll"));
        Add(Path.Combine(runtimeDir, "mscorlib.dll"));
        Add(Path.Combine(runtimeDir, "System.dll"));
        Add(Path.Combine(runtimeDir, "System.Core.dll"));
        Add(Path.Combine(runtimeDir, "Microsoft.CSharp.dll"));

        return references.Values;
    }
}
