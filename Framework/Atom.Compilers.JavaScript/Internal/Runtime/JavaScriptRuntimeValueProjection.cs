namespace Atom.Compilers.JavaScript;

internal static class JavaScriptRuntimeValueProjection
{
    internal static object? Project(JavaScriptRuntimeValue value)
        => Project(value, JavaScriptRuntimeSpecification.Extended);

    internal static object? Project(
        JavaScriptRuntimeValue value,
        JavaScriptRuntimeSpecification specification)
    {
        if (value.Kind is JavaScriptRuntimeValueKind.Null or JavaScriptRuntimeValueKind.Undefined)
            return null;

        JavaScriptRuntimeSpecificationPolicy.ThrowIfValueKindUnsupported(specification, value.Kind);

        return value.BoxedValue;
    }
}