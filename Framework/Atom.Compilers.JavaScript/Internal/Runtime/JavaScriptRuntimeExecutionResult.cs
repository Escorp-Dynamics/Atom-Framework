using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeExecutionResult(
    int SessionEpoch,
    JavaScriptRuntimeExecutionOperationKind Operation,
    JavaScriptRuntimeExecutionStatus Status,
    JavaScriptRuntimeValue Value,
    JavaScriptRuntimeExecutionDiagnostic? Diagnostic)
{
    internal static JavaScriptRuntimeExecutionResult Completed(
        int sessionEpoch,
        JavaScriptRuntimeExecutionOperationKind operation,
        JavaScriptRuntimeValue value)
        => new(sessionEpoch, operation, JavaScriptRuntimeExecutionStatus.Completed, value, Diagnostic: null);

    internal static JavaScriptRuntimeExecutionResult ParserFailed(
        int sessionEpoch,
        JavaScriptRuntimeExecutionOperationKind operation,
        JavaScriptRuntimeExecutionDiagnostic diagnostic)
        => new(sessionEpoch, operation, JavaScriptRuntimeExecutionStatus.ParserFailed, JavaScriptRuntimeValue.Null, diagnostic);

    internal static JavaScriptRuntimeExecutionResult LoweringFailed(
        int sessionEpoch,
        JavaScriptRuntimeExecutionOperationKind operation,
        JavaScriptRuntimeExecutionDiagnostic diagnostic)
        => new(sessionEpoch, operation, JavaScriptRuntimeExecutionStatus.LoweringFailed, JavaScriptRuntimeValue.Null, diagnostic);

    internal static JavaScriptRuntimeExecutionResult EngineUnavailable(
        int sessionEpoch,
        JavaScriptRuntimeExecutionOperationKind operation,
        JavaScriptRuntimeExecutionDiagnostic diagnostic)
        => new(sessionEpoch, operation, JavaScriptRuntimeExecutionStatus.EngineUnavailable, JavaScriptRuntimeValue.Null, diagnostic);

    internal static JavaScriptRuntimeExecutionResult SpecificationViolation(
        int sessionEpoch,
        JavaScriptRuntimeExecutionOperationKind operation,
        JavaScriptRuntimeExecutionDiagnostic diagnostic)
        => new(sessionEpoch, operation, JavaScriptRuntimeExecutionStatus.SpecificationViolation, JavaScriptRuntimeValue.Null, diagnostic);
}