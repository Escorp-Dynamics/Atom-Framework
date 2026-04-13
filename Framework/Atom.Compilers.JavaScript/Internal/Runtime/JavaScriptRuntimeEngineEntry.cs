using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeEngineEntry(
    int SessionEpoch,
    int RegistrationIndex,
    int TypeIndex,
    int MemberIndex,
    JavaScriptRuntimeMarshallingChannel Channel,
    JavaScriptGeneratedMemberKind MemberKind,
    JavaScriptRuntimeTypeAttributes TypeAttributes,
    JavaScriptRuntimeMemberAttributes MemberAttributes);