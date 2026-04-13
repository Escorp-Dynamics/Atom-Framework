namespace Atom.Compilers.JavaScript;

internal readonly record struct JavaScriptRuntimeExecutionDiagnostic(
    JavaScriptRuntimeExecutionPhaseKind Phase,
    string Code,
    string Message);