namespace Atom.Web.Browsers;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек браузера.
/// </summary>
public interface IWebBrowserSettings
{
    /// <summary>
    /// Путь к исполняемому файлу браузера.
    /// </summary>
    string BinaryPath { get; }

    /// <summary>
    /// Определяет, что браузер запущен в режиме headless.
    /// </summary>
    bool IsHeadless { get; }

    /// <summary>
    /// Определяет, что браузер запущен в режиме incognito.
    /// </summary>
    bool IsIncognito { get; }
}