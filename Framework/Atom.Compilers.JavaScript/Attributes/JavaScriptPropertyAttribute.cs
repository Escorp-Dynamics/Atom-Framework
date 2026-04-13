namespace Atom.Compilers.JavaScript;

/// <summary>
/// Настраивает экспорт свойства или поля в JavaScript surface.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class JavaScriptPropertyAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// JavaScript-имя свойства.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Признак readonly-экспорта.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Признак обязательного materialization в shape таблицах генератора.
    /// </summary>
    public bool IsRequired { get; init; }
}