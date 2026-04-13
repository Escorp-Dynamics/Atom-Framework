using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeLoweredProgram(
    int SessionEpoch,
    JavaScriptRuntimeSpecification Specification,
    JavaScriptRuntimeLoweringPolicy PolicyFlags,
    JavaScriptRuntimeExecutionOperationKind Operation,
    int SourceLength)
{
    internal bool AllowsExtendedRuntimeFeatures
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.AllowsExtendedRuntimeSurface) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresStrictRuntimeSurface
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresStrictRuntimeSurface) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresInvocationLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresInvocationLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresIndexAccessLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresIndexAccessLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresMutationLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresMutationLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresLiteralMaterialization
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresLiteralMaterialization) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresOperatorLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresOperatorLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresTemplateMaterialization
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresTemplateMaterialization) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresClosureLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresClosureLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresShortCircuitLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresShortCircuitLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresAggregateLiteralMaterialization
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresAggregateLiteralMaterialization) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresSpreadLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresSpreadLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresConditionalLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresConditionalLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresDestructuringLowering
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresDestructuringLowering) != JavaScriptRuntimeLoweringPolicy.None;

    internal bool RequiresRegularExpressionMaterialization
        => (PolicyFlags & JavaScriptRuntimeLoweringPolicy.RequiresRegularExpressionMaterialization) != JavaScriptRuntimeLoweringPolicy.None;

    internal JavaScriptRuntimeExecutionPlanSeed CreateExecutionPlanSeed()
        => new(SessionEpoch, Specification, PolicyFlags, Operation, SourceLength);
}