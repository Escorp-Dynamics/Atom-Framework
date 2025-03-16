using Atom.Web.Browsing.BiDi;

namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для реализации страниц драйвера веб-браузера.
/// </summary>
public interface IWebDriverPage : IWebPage
{
    /// <summary>
    /// Соединение с протоколом WebDriver BiDi.
    /// </summary>
    BiDiDriver BiDi { get; }
}