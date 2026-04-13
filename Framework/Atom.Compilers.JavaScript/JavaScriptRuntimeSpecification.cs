namespace Atom.Compilers.JavaScript;

/// <summary>
/// Определяет целевой набор runtime-возможностей.
/// </summary>
public enum JavaScriptRuntimeSpecification
{
    /// <summary>
    /// Только стандартный ECMAScript-compatible baseline без дополнительных runtime-specific расширений.
    /// </summary>
    ECMAScript,

    /// <summary>
    /// Расширенный runtime surface, включающий internal runtime-specific contracts и будущие platform-specific расширения.
    /// </summary>
    Extended,
}