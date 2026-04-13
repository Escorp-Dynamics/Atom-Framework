namespace Atom.Compilers.JavaScript;

[System.Flags]
internal enum JavaScriptRuntimeLoweringPolicy
{
    None = 0,
    RequiresStrictRuntimeSurface = 1 << 0,
    AllowsExtendedRuntimeSurface = 1 << 1,
    RequiresInvocationLowering = 1 << 2,
    RequiresIndexAccessLowering = 1 << 3,
    RequiresMutationLowering = 1 << 4,
    RequiresLiteralMaterialization = 1 << 5,
    RequiresOperatorLowering = 1 << 6,
    RequiresTemplateMaterialization = 1 << 7,
    RequiresClosureLowering = 1 << 8,
    RequiresShortCircuitLowering = 1 << 9,
    RequiresAggregateLiteralMaterialization = 1 << 10,
    RequiresSpreadLowering = 1 << 11,
    RequiresConditionalLowering = 1 << 12,
    RequiresDestructuringLowering = 1 << 13,
    RequiresRegularExpressionMaterialization = 1 << 14,
}