namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeLoweringStageScaffold
{
    internal static bool TryLower(
        JavaScriptRuntimeParsedSource parsedSource,
        out JavaScriptRuntimeLoweredProgram loweredProgram)
    {
        loweredProgram = new JavaScriptRuntimeLoweredProgram(
            parsedSource.SessionEpoch,
            parsedSource.Specification,
            GetPolicyFlags(parsedSource),
            parsedSource.Operation,
            parsedSource.SourceLength);

        return true;
    }

    private static JavaScriptRuntimeLoweringPolicy GetPolicyFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = parsedSource.AllowsExtendedRuntimeFeatures || parsedSource.ContainsHostBindingCandidate
            ? JavaScriptRuntimeLoweringPolicy.AllowsExtendedRuntimeSurface
            : JavaScriptRuntimeLoweringPolicy.RequiresStrictRuntimeSurface;

        policyFlags |= GetInvocationAndIndexFlags(parsedSource);
        policyFlags |= GetMutationAndLiteralFlags(parsedSource);
        policyFlags |= GetOperatorAndTemplateFlags(parsedSource);
        policyFlags |= GetClosureAndShortCircuitFlags(parsedSource);
        policyFlags |= GetAggregateAndSpreadFlags(parsedSource);
        policyFlags |= GetConditionalFlags(parsedSource);
        policyFlags |= GetDestructuringFlags(parsedSource);
        policyFlags |= GetRegularExpressionFlags(parsedSource);

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetInvocationAndIndexFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsInvocation)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresInvocationLowering;

        if (parsedSource.ContainsIndexAccess)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresIndexAccessLowering;

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetMutationAndLiteralFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsAssignment)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresMutationLowering;

        if (parsedSource.ContainsStringLiteral || parsedSource.ContainsNumericLiteral)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresLiteralMaterialization;

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetOperatorAndTemplateFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsUnaryOperator || parsedSource.ContainsBinaryOperator || parsedSource.ContainsComparisonOperator || parsedSource.ContainsLogicalOperator)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresOperatorLowering;

        if (parsedSource.ContainsTemplateLiteral)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresTemplateMaterialization;

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetClosureAndShortCircuitFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsArrowFunctionCandidate)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresClosureLowering;

        if (parsedSource.ContainsNullishCoalescingOperator || parsedSource.ContainsOptionalChainingCandidate)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresShortCircuitLowering;

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetAggregateAndSpreadFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsArrayLiteralCandidate || parsedSource.ContainsObjectLiteralCandidate)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresAggregateLiteralMaterialization;

        if (parsedSource.ContainsSpreadOrRestCandidate)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresSpreadLowering;

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetConditionalFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsConditionalOperatorCandidate)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresConditionalLowering;

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetDestructuringFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsDestructuringPatternCandidate)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresDestructuringLowering;

        return policyFlags;
    }

    private static JavaScriptRuntimeLoweringPolicy GetRegularExpressionFlags(JavaScriptRuntimeParsedSource parsedSource)
    {
        var policyFlags = JavaScriptRuntimeLoweringPolicy.None;

        if (parsedSource.ContainsRegularExpressionLiteralCandidate)
            policyFlags |= JavaScriptRuntimeLoweringPolicy.RequiresRegularExpressionMaterialization;

        return policyFlags;
    }
}