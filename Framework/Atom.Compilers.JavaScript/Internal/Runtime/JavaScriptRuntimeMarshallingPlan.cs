using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeMarshallingPlan(
    int RegistrationIndex,
    int TypeIndex,
    int MemberIndex,
    JavaScriptRuntimeMarshallingChannel Channel,
    bool IsPure,
    bool IsInlineCandidate,
    bool IsReadOnly,
    bool IsRequired);