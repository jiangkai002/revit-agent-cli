using Autodesk.Revit.DB;

namespace RevitAgent.DynamicCode;

/// <summary>
/// Contract implemented by dynamically compiled Revit commands. The AI agent generates
/// a public type implementing this interface; the executor compiles it and invokes
/// <see cref="Execute"/> against an open document.
/// </summary>
public interface IRevitDynamicCommand
{
    /// <summary>
    /// Runs against the supplied document and returns JSON-serializable data
    /// (primitives, arrays, anonymous objects). Never return Element/Document instances.
    /// </summary>
    object? Execute(Document document);
}
