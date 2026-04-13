namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeExecutionPlanPolicy
{
    internal static bool TryCreateUnavailableDiagnostic(
        JavaScriptRuntimeExecutionPlanSeed executionPlanSeed,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (TryCreateDestructuringOrMutationDiagnostic(executionPlanSeed, out diagnostic)
            || TryCreateInvocationOrClosureDiagnostic(executionPlanSeed, out diagnostic)
            || TryCreateTemplateOrOperatorDiagnostic(executionPlanSeed, out diagnostic)
            || TryCreateLiteralOrAggregateDiagnostic(executionPlanSeed, out diagnostic))
        {
            return true;
        }

        diagnostic = default;
        return false;
    }

    private static bool TryCreateDestructuringOrMutationDiagnostic(
        JavaScriptRuntimeExecutionPlanSeed executionPlanSeed,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (executionPlanSeed.RequiresDestructuringLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.DestructuringLoweringUnavailable,
                "JavaScript destructuring lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresMutationLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.MutationLoweringUnavailable,
                "JavaScript mutation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresIndexAccessLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.IndexAccessLoweringUnavailable,
                "JavaScript index access lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        diagnostic = default;
        return false;
    }

    private static bool TryCreateInvocationOrClosureDiagnostic(
        JavaScriptRuntimeExecutionPlanSeed executionPlanSeed,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (executionPlanSeed.RequiresInvocationLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.InvocationLoweringUnavailable,
                "JavaScript invocation lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresClosureLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.ClosureLoweringUnavailable,
                "JavaScript closure lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        diagnostic = default;
        return false;
    }

    private static bool TryCreateTemplateOrOperatorDiagnostic(
        JavaScriptRuntimeExecutionPlanSeed executionPlanSeed,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (executionPlanSeed.RequiresShortCircuitLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.ShortCircuitLoweringUnavailable,
                "JavaScript short-circuit lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresConditionalLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.ConditionalLoweringUnavailable,
                "JavaScript conditional lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresRegularExpressionMaterialization)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.RegularExpressionMaterializationUnavailable,
                "JavaScript regular expression materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresTemplateMaterialization)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.TemplateMaterializationUnavailable,
                "JavaScript template materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresSpreadLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.SpreadLoweringUnavailable,
                "JavaScript spread lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresOperatorLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.OperatorLoweringUnavailable,
                "JavaScript operator lowering pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        diagnostic = default;
        return false;
    }

    private static bool TryCreateLiteralOrAggregateDiagnostic(
        JavaScriptRuntimeExecutionPlanSeed executionPlanSeed,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (executionPlanSeed.RequiresAggregateLiteralMaterialization)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.AggregateLiteralMaterializationUnavailable,
                "JavaScript aggregate literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        if (executionPlanSeed.RequiresLiteralMaterialization && !executionPlanSeed.RequiresOperatorLowering)
        {
            diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.LiteralMaterializationUnavailable,
                "JavaScript literal materialization pipeline is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.");
            return true;
        }

        diagnostic = default;
        return false;
    }
}