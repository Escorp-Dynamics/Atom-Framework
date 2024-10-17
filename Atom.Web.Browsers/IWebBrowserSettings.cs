using System.Net;
using Atom.Web.Proxies;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек браузера.
/// </summary>
public interface IWebBrowserSettings
{
    /// <summary>
    /// Обработчик HTTP-запросов.
    /// </summary>
    HttpClientHandler Handler { get; set; }

    /// <summary>
    /// Прокси.
    /// </summary>
    Proxy? Proxy { get; set; }

    /// <summary>
    /// Импортируемые куки.
    /// </summary>
    IEnumerable<Cookie> Cookies { get; set; }

    /// <summary>
    /// Флаг, указывающий, является ли DOM доступным.
    /// </summary>
    bool IsDOMEnabled { get; set; }

    /// <summary>
    /// Флаг, указывающий, включен ли JavaScript.
    /// </summary>
    bool IsJavaScriptEnabled { get; set; }
}