namespace Atom.Compilers.JavaScript;

internal enum JavaScriptRuntimeExecutionPhaseKind : byte
{
    Bootstrap,
    EngineDispatch,
    Parser,
    Lowering,
    Execution,
}