namespace Atom.Web.Browsing;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек контекста браузера.
/// </summary>
public interface IWebBrowserContextSettings : IWebBrowserSettings
{
    /// <summary>
    /// Настройки контекста браузера по умолчанию.
    /// </summary>
    static new abstract IWebBrowserContextSettings Default { get; }

    /// <summary>
    /// Создаёт новые настройки контекста браузера из базовых настроек браузера.
    /// </summary>
    /// <param name="baseSettings">Базовые настройки браузера.</param>
    /// <typeparam name="TBase">Тип базовых настроек</typeparam>
    /// <typeparam name="TResult">Тип получаемых настроек.</typeparam>
    static abstract TResult CreateFrom<TBase, TResult>(TBase baseSettings)
        where TBase : IWebBrowserSettings
        where TResult : IWebBrowserContextSettings, new();
}