namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для реализации драйвера веб-браузера.
/// </summary>
public interface IWebDriver : IWebBrowser
{
    /// <summary>
    /// Определяет, установлен ли браузер.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Определяет, запущен ли браузер.
    /// </summary>
    bool IsRunning { get; }
}