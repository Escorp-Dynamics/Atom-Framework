namespace Atom.Compilers.JavaScript;

internal enum JavaScriptRuntimeExecutionStatus : byte
{
    Completed,
    ParserFailed,
    LoweringFailed,
    EngineUnavailable,
    SpecificationViolation,
}