using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeSessionTables(
    ImmutableArray<JavaScriptRuntimeSessionTableEntry> Registrations,
    int TotalTypeCount,
    int TotalMemberCount)
{
    internal bool IsInitialized => TotalTypeCount > 0 || TotalMemberCount > 0 || !Registrations.IsDefault;
}