namespace Atom.Compilers.JavaScript;

internal readonly record struct JavaScriptRuntimeMemberDescriptor(
    string Name,
    JavaScriptGeneratedMemberKind Kind,
    string? ExportName,
    JavaScriptRuntimeMemberAttributes Attributes);