namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeExecutionResultPolicy
{
    internal static JavaScriptRuntimeExecutionResult Apply(
        JavaScriptRuntimeExecutionResult result,
        JavaScriptRuntimeSpecification specification)
    {
        if (result.Status != JavaScriptRuntimeExecutionStatus.Completed)
            return result;

        if (!JavaScriptRuntimeSpecificationPolicy.TryCreateUnsupportedValueKindDiagnostic(specification, result.Value.Kind, out var diagnostic))
            return result;

        return JavaScriptRuntimeExecutionResult.SpecificationViolation(
            result.SessionEpoch,
            result.Operation,
            diagnostic);
    }
}