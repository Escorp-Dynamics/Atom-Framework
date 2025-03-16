namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек страницы драйвера веб-браузера.
/// </summary>
public interface IWebDriverPageSettings : IWebDriverWindowSettings, IWebPageSettings
{
    /// <summary>
    /// Настройки страницы драйвера по умолчанию.
    /// </summary>
    static abstract new IWebDriverPageSettings Default { get; }
}