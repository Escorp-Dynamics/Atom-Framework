namespace Atom;

/// <summary>
/// Модификатор доступа.
/// </summary>
public enum AccessModifier
{
    /// <summary>
    /// Приватный.
    /// </summary>
    Private,
    /// <summary>
    /// Защищённый.
    /// </summary>
    Protected,
    /// <summary>
    /// Внутренний либо защищённый.
    /// </summary>
    ProtectedInternal,
    /// <summary>
    /// Внутренний.
    /// </summary>
    Internal,
    /// <summary>
    /// Публичный.
    /// </summary>
    Public,
    /// <summary>
    /// Ограничен текущим файлом.
    /// </summary>
    File,
}