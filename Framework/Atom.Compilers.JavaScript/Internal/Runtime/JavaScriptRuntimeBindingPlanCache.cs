using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeBindingPlanCache(
    ImmutableArray<JavaScriptRuntimeBindingPlan> MemberPlansByMemberIndex)
{
    internal bool IsInitialized => !MemberPlansByMemberIndex.IsDefault;
}