namespace Atom.Web.Browsing.Fingerprints;

/// <summary>
/// Представляет базовый интерфейс для реализации фингерпринта браузера.
/// </summary>
public interface IWebFingerprint
{
    /// <summary>
    /// Информация об оборудовании.
    /// </summary>
    HardwareInfo Hardware { get; }

    /// <summary>
    /// Агент пользователя.
    /// </summary>
    string UserAgent { get; }
}