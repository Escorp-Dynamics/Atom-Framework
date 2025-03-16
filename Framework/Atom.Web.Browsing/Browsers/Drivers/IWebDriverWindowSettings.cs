namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек окна драйвера веб-браузера.
/// </summary>
public interface IWebDriverWindowSettings : IWebDriverContextSettings, IWebWindowSettings
{
    /// <summary>
    /// Настройки окна драйвера по умолчанию.
    /// </summary>
    static abstract new IWebDriverWindowSettings Default { get; }
}