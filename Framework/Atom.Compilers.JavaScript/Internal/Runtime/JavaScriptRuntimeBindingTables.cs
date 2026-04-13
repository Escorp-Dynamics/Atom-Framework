using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeBindingTables(
    ImmutableArray<JavaScriptRuntimeTypeBindingTableEntry> Types,
    ImmutableArray<JavaScriptRuntimeMemberBindingTableEntry> Members)
{
    internal bool IsInitialized => !Types.IsDefault || !Members.IsDefault;
}