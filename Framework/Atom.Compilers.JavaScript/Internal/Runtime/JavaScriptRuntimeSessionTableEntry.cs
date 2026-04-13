using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeSessionTableEntry(
    string RegistrationName,
    int TypeStart,
    int TypeCount,
    int MemberStart,
    int MemberCount);