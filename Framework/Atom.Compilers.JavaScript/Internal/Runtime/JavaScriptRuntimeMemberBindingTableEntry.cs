using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeMemberBindingTableEntry(
    int TypeIndex,
    string Name,
    string? ExportName,
    JavaScriptGeneratedMemberKind Kind,
    JavaScriptRuntimeMemberAttributes Attributes);