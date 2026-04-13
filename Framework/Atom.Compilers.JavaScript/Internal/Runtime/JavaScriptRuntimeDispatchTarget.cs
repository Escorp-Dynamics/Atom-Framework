using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeDispatchTarget(
    int SessionEpoch,
    int RegistrationIndex,
    int TypeIndex,
    int MemberIndex,
    JavaScriptRuntimeTypeAttributes TypeAttributes,
    JavaScriptGeneratedMemberKind MemberKind,
    JavaScriptRuntimeMemberAttributes MemberAttributes);