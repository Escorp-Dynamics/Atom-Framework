using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeExecutionState(
    ImmutableArray<JavaScriptRuntimeRegistrationDescriptor> Registrations,
    JavaScriptRuntimeSpecification Specification,
    JavaScriptRuntimeSessionTables Tables,
    JavaScriptRuntimeBindingTables BindingTables,
    JavaScriptRuntimeLookupCache LookupCache,
    JavaScriptRuntimeBindingPlanCache BindingPlanCache,
    JavaScriptRuntimeMarshallingPlanCache MarshallingPlanCache,
    int SessionEpoch)
{
    internal bool IsInitialized => SessionEpoch > 0 || !Registrations.IsDefault;

    internal JavaScriptRuntimeExecutionState NextEpoch()
        => new(Registrations, Specification, Tables, BindingTables, LookupCache, BindingPlanCache, MarshallingPlanCache, SessionEpoch + 1);
}