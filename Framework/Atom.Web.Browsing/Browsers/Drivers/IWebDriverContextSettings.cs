namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовые настройки контекста драйвера веб-браузера.
/// </summary>
public interface IWebDriverContextSettings : IWebDriverSettings, IWebBrowserContextSettings
{
    /// <summary>
    /// Настройки контекста драйвера по умолчанию.
    /// </summary>
    static abstract new IWebDriverContextSettings Default { get; }
}