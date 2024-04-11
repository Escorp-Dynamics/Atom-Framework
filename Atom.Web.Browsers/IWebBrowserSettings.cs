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
    /// Путь к дистрибутиву браузера.
    /// </summary>
    string DistributionPath { get; }

    /// <summary>
    /// Определяет, что браузер запущен в режиме headless.
    /// </summary>
    bool IsHeadless { get; }

    /// <summary>
    /// Определяет, что браузер запущен в режиме incognito.
    /// </summary>
    bool IsIncognito { get; }

    /// <summary>
    /// Возвращает путь к исполняемому файлу браузера.
    /// </summary>
    /// <returns>Путь к исполняемому файлу браузера.</returns>
    string GetNativeBinaryPath();

    /// <summary>
    /// Возвращает путь к дистрибутиву браузера.
    /// </summary>
    /// <returns>Путь к дистрибутиву браузера.</returns>
    string GetNativeDistributionPath();
}