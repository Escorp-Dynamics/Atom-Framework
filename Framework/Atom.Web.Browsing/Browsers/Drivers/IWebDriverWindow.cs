using Atom.Web.Browsing.BiDi;

namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для реализации окон драйвера веб-браузера.
/// </summary>
public interface IWebDriverWindow : IWebWindow
{
    /// <summary>
    /// Соединение с протоколом WebDriver BiDi.
    /// </summary>
    BiDiDriver BiDi { get; }
}