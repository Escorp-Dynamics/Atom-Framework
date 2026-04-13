namespace Atom.Compilers.JavaScript;

/// <summary>
/// Указывает, что тип должен проецироваться как JavaScript dictionary-like object.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class JavaScriptDictionaryAttribute : Attribute
{
    /// <summary>
    /// Разрешает ли генератору использовать fast-path для string-key maps.
    /// </summary>
    public bool IsStringKeysOnly { get; init; } = true;

    /// <summary>
    /// Требуется ли стабильный порядок перечисления ключей.
    /// </summary>
    public bool IsPreserveEnumerationOrder { get; init; } = true;
}