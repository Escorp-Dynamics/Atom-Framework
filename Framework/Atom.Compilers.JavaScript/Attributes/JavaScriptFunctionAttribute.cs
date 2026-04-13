namespace Atom.Compilers.JavaScript;

/// <summary>
/// Настраивает экспорт метода в JavaScript function surface.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class JavaScriptFunctionAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// JavaScript-имя функции.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Признак чистой функции без побочных эффектов для optimizer pipeline.
    /// </summary>
    public bool IsPure { get; init; }

    /// <summary>
    /// Допускается ли инлайнинг в generated adapters.
    /// </summary>
    public bool IsInline { get; init; } = true;
}