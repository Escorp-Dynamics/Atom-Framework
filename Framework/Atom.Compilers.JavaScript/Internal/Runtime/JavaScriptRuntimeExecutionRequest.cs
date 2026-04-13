using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly ref struct JavaScriptRuntimeExecutionRequest
{
    internal JavaScriptRuntimeExecutionRequest(
        JavaScriptRuntimeExecutionState state,
        ReadOnlySpan<char> source,
        JavaScriptRuntimeExecutionOperationKind operation)
    {
        State = state;
        Source = source;
        Operation = operation;
    }

    internal JavaScriptRuntimeExecutionState State { get; }
    internal ReadOnlySpan<char> Source { get; }
    internal JavaScriptRuntimeExecutionOperationKind Operation { get; }
    internal JavaScriptRuntimeSpecification Specification => State.Specification;
    internal int SessionEpoch => State.SessionEpoch;
    internal bool IsEmptySource => Source.IsEmpty;
}