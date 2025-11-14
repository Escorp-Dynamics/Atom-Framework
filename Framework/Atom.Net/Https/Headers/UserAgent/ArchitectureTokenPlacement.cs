namespace Atom.Net.Https.Headers;

/// <summary>
/// Определяет размещение токена архитектуры.
/// </summary>
public enum ArchitectureTokenPlacement : byte
{
    /// <summary>
    /// Архитектура не указана.
    /// </summary>
    None = 0,
    /// <summary>
    /// Расположение будет определено автоматически.
    /// </summary>
    Auto,
    /// <summary>
    /// Архитектура располагается перед токеном платформы.
    /// </summary>
    Prefix,
    /// <summary>
    /// Архитектура располагается после токена платформы.
    /// </summary>
    Suffix,
    /// <summary>
    /// Архитектура является отдельным токеном.
    /// </summary>
    Separate,
}