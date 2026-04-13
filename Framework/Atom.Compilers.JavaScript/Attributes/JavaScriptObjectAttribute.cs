namespace Atom.Compilers.JavaScript;

/// <summary>
/// Помечает CLR-тип как источник генерации JavaScript object surface.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class JavaScriptObjectAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// JavaScript-имя типа.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Признак генерации как глобально регистрируемого объекта.
    /// </summary>
    public bool IsGlobalExportEnabled { get; init; }
}