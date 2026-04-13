namespace Atom.Compilers.JavaScript;

/// <summary>
/// Исключает член из JavaScript surface и source generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Event, Inherited = false)]
public sealed class JavaScriptIgnoreAttribute : Attribute
{
}