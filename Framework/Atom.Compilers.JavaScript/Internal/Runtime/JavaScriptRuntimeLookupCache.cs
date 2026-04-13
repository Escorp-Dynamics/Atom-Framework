using System.Collections.Frozen;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeLookupCache(
    FrozenDictionary<string, int>? RegistrationIndexes,
    FrozenDictionary<(string RegistrationName, string EntityName), int>? TypeIndexes,
    FrozenDictionary<(string RegistrationName, string EntityName, string ExportName), int>? MemberIndexes)
{
    internal bool IsInitialized
        => RegistrationIndexes is not null
            && TypeIndexes is not null
            && MemberIndexes is not null;
}