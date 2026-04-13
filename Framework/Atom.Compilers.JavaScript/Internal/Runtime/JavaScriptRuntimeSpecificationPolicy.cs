namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeSpecificationPolicy
{
    internal static bool SupportsHostRegistration(JavaScriptRuntimeSpecification specification)
        => specification == JavaScriptRuntimeSpecification.Extended;

    internal static bool TryCreateUnsupportedParserFeatureDiagnostic(
        JavaScriptRuntimeSpecification specification,
        JavaScriptRuntimeParserFeature parserFeatures,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (specification != JavaScriptRuntimeSpecification.ECMAScript)
        {
            diagnostic = default;
            return false;
        }

        if ((parserFeatures & JavaScriptRuntimeParserFeature.ContainsHostBindingCandidate) == JavaScriptRuntimeParserFeature.None)
        {
            diagnostic = default;
            return false;
        }

        diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Parser,
            JavaScriptRuntimeExecutionDiagnosticCodes.StrictParserFeatureUnsupported,
            "JavaScript parser feature 'host.' requires JavaScriptRuntimeSpecification.Extended.");

        return true;
    }

    internal static bool TryCreateUnsupportedLoweringPolicyDiagnostic(
        JavaScriptRuntimeSpecification specification,
        JavaScriptRuntimeLoweringPolicy loweringPolicy,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (specification != JavaScriptRuntimeSpecification.ECMAScript)
        {
            diagnostic = default;
            return false;
        }

        if ((loweringPolicy & JavaScriptRuntimeLoweringPolicy.AllowsExtendedRuntimeSurface) == JavaScriptRuntimeLoweringPolicy.None)
        {
            diagnostic = default;
            return false;
        }

        diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Lowering,
            JavaScriptRuntimeExecutionDiagnosticCodes.StrictLoweringPolicyUnsupported,
            "JavaScript lowering policy 'AllowsExtendedRuntimeSurface' requires JavaScriptRuntimeSpecification.Extended.");

        return true;
    }

    internal static void ThrowIfValueKindUnsupported(
        JavaScriptRuntimeSpecification specification,
        JavaScriptRuntimeValueKind valueKind)
    {
        if (!TryCreateUnsupportedValueKindDiagnostic(specification, valueKind, out var diagnostic))
            return;

        throw new NotSupportedException(diagnostic.Message);
    }

    internal static bool TryCreateUnsupportedValueKindDiagnostic(
        JavaScriptRuntimeSpecification specification,
        JavaScriptRuntimeValueKind valueKind,
        out JavaScriptRuntimeExecutionDiagnostic diagnostic)
    {
        if (IsValueKindSupported(specification, valueKind))
        {
            diagnostic = default;
            return false;
        }

        diagnostic = new JavaScriptRuntimeExecutionDiagnostic(
            JavaScriptRuntimeExecutionPhaseKind.Execution,
            JavaScriptRuntimeExecutionDiagnosticCodes.StrictValueKindUnsupported,
            GetUnsupportedValueKindMessage(specification, valueKind));

        return true;
    }

    private static bool IsValueKindSupported(
        JavaScriptRuntimeSpecification specification,
        JavaScriptRuntimeValueKind valueKind)
    {
        if (specification == JavaScriptRuntimeSpecification.Extended)
            return true;

        return !IsExtendedOnlyValueKind(valueKind);
    }

    private static bool IsExtendedOnlyValueKind(JavaScriptRuntimeValueKind valueKind)
        => valueKind is JavaScriptRuntimeValueKind.InternalError
            or JavaScriptRuntimeValueKind.StackOverflowError
            or JavaScriptRuntimeValueKind.TimeoutError
            or JavaScriptRuntimeValueKind.MemoryLimitError
            or JavaScriptRuntimeValueKind.CancellationError
            or JavaScriptRuntimeValueKind.HostInteropError
            or JavaScriptRuntimeValueKind.ResourceExhaustedError
            or JavaScriptRuntimeValueKind.HostObject;

    private static string GetUnsupportedValueKindMessage(
        JavaScriptRuntimeSpecification specification,
        JavaScriptRuntimeValueKind valueKind)
        => $"JavaScript runtime value kind '{valueKind}' is not available when JavaScriptRuntimeSpecification.{specification} is selected.";
}