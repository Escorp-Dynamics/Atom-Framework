namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeExecutionEngineScaffold
{
    private const string EcmaScriptEngineUnavailableMessage = "ECMAScript execution engine is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.";
    private const string ExtendedEngineUnavailableMessage = "Extended runtime execution engine is not implemented yet. See JAVASCRIPT_COMPILER_ROADMAP.md.";

    internal delegate bool TryParseDelegate(
        JavaScriptRuntimeExecutionRequest request,
        out JavaScriptRuntimeParsedSource parsedSource);

    internal delegate bool TryLowerDelegate(
        JavaScriptRuntimeParsedSource parsedSource,
        out JavaScriptRuntimeLoweredProgram loweredProgram);

    internal static JavaScriptRuntimeExecutionResult Run(JavaScriptRuntimeExecutionRequest request)
        => Run(request, JavaScriptRuntimeParserStageScaffold.TryParse, JavaScriptRuntimeLoweringStageScaffold.TryLower);

    internal static JavaScriptRuntimeExecutionResult Run(
        JavaScriptRuntimeExecutionRequest request,
        TryParseDelegate tryParse,
        TryLowerDelegate tryLower)
    {
        if (request.IsEmptySource)
        {
            return JavaScriptRuntimeExecutionResult.Completed(
                request.SessionEpoch,
                request.Operation,
                JavaScriptRuntimeValue.Null);
        }

        if (!tryParse(request, out var parsedSource))
        {
            return JavaScriptRuntimeExecutionResult.ParserFailed(
                request.SessionEpoch,
                request.Operation,
                new JavaScriptRuntimeExecutionDiagnostic(
                    JavaScriptRuntimeExecutionPhaseKind.Parser,
                    JavaScriptRuntimeExecutionDiagnosticCodes.ParserStageFailed,
                    "JavaScript parser stage did not return a parsed source contract."));
        }

        if (JavaScriptRuntimeSpecificationPolicy.TryCreateUnsupportedParserFeatureDiagnostic(
            parsedSource.Specification,
            parsedSource.ParserFeatures,
            out var parserFeatureDiagnostic))
        {
            return JavaScriptRuntimeExecutionResult.SpecificationViolation(
                parsedSource.SessionEpoch,
                parsedSource.Operation,
                parserFeatureDiagnostic);
        }

        if (!tryLower(parsedSource, out var loweredProgram))
        {
            return JavaScriptRuntimeExecutionResult.LoweringFailed(
                parsedSource.SessionEpoch,
                parsedSource.Operation,
                new JavaScriptRuntimeExecutionDiagnostic(
                    JavaScriptRuntimeExecutionPhaseKind.Lowering,
                    JavaScriptRuntimeExecutionDiagnosticCodes.LoweringStageFailed,
                    "JavaScript lowering stage did not return a lowered program contract."));
        }

        if (JavaScriptRuntimeSpecificationPolicy.TryCreateUnsupportedLoweringPolicyDiagnostic(
            loweredProgram.Specification,
            loweredProgram.PolicyFlags,
            out var loweringPolicyDiagnostic))
        {
            return JavaScriptRuntimeExecutionResult.SpecificationViolation(
                loweredProgram.SessionEpoch,
                loweredProgram.Operation,
                loweringPolicyDiagnostic);
        }

        var executionPlanSeed = loweredProgram.CreateExecutionPlanSeed();

        return JavaScriptRuntimeExecutionResult.EngineUnavailable(
            executionPlanSeed.SessionEpoch,
            executionPlanSeed.Operation,
            CreateEngineUnavailableDiagnostic(executionPlanSeed));
    }

    private static JavaScriptRuntimeExecutionDiagnostic CreateEngineUnavailableDiagnostic(JavaScriptRuntimeExecutionPlanSeed executionPlanSeed)
    {
        if (JavaScriptRuntimeExecutionPlanPolicy.TryCreateUnavailableDiagnostic(executionPlanSeed, out var diagnostic))
            return diagnostic;

        if (executionPlanSeed.AllowsExtendedRuntimeFeatures)
        {
            return new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.ExtendedEngineUnavailable,
                ExtendedEngineUnavailableMessage);
        }

        if (executionPlanSeed.RequiresStrictRuntimeSurface)
        {
            return new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.EcmaScriptEngineUnavailable,
                EcmaScriptEngineUnavailableMessage);
        }

        return executionPlanSeed.Specification switch
        {
            JavaScriptRuntimeSpecification.ECMAScript => new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.EcmaScriptEngineUnavailable,
                EcmaScriptEngineUnavailableMessage),
            _ => new JavaScriptRuntimeExecutionDiagnostic(
                JavaScriptRuntimeExecutionPhaseKind.EngineDispatch,
                JavaScriptRuntimeExecutionDiagnosticCodes.EngineUnavailable,
                "JavaScriptRuntime execution engine is not implemented for the selected runtime specification. See JAVASCRIPT_COMPILER_ROADMAP.md."),
        };
    }
}