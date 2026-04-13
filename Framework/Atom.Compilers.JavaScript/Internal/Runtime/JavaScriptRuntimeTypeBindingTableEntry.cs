using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeTypeBindingTableEntry(
    int RegistrationIndex,
    string EntityName,
    string Generator,
    int MemberStart,
    int MemberCount,
    JavaScriptRuntimeTypeAttributes Attributes);