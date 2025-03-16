namespace Atom.Web.Browsing;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек страницы браузера.
/// </summary>
public interface IWebPageSettings : IWebWindowSettings
{
    /// <summary>
    /// Настройки страницы браузера по умолчанию.
    /// </summary>
    static new abstract IWebPageSettings Default { get; }
}