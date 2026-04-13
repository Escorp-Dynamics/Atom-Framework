namespace Atom.Compilers.JavaScript;

internal readonly record struct JavaScriptGeneratedMemberMetadata(
    string Name,
    JavaScriptGeneratedMemberKind Kind,
    string? ExportName = null,
    bool IsReadOnly = false,
    bool IsRequired = false,
    bool IsPure = false,
    bool IsInline = false);