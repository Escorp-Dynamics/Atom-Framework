namespace Atom.Web.Browsing;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек окна браузера.
/// </summary>
public interface IWebWindowSettings : IWebBrowserContextSettings
{
    /// <summary>
    /// Настройки окна браузера по умолчанию.
    /// </summary>
    static new abstract IWebWindowSettings Default { get; }
}