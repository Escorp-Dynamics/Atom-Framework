using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeBindingPlan(
    int RegistrationIndex,
    int TypeIndex,
    int MemberIndex,
    JavaScriptRuntimeTypeAttributes TypeAttributes,
    JavaScriptGeneratedMemberKind MemberKind,
    JavaScriptRuntimeMemberAttributes MemberAttributes);