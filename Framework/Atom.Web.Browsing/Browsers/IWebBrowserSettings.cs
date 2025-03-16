using System.Net;
using Atom.Web.Browsing.Fingerprints;
using Atom.Web.Proxies;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек браузера.
/// </summary>
public interface IWebBrowserSettings
{
    /// <summary>
    /// Журнал событий.
    /// </summary>
    ILogger? Logger { get; set; }

    /// <summary>
    /// Обработчик HTTP-запросов.
    /// </summary>
    HttpClientHandler? Handler { get; set; }

    /// <summary>
    /// Прокси.
    /// </summary>
    Proxy? Proxy { get; set; }

    /// <summary>
    /// Импортируемые куки.
    /// </summary>
    IEnumerable<Cookie> Cookies { get; set; }

    /// <summary>
    /// Фингерпринт.
    /// </summary>
    IWebFingerprint? Fingerprint { get; set; }

    /// <summary>
    /// Указывает, является ли DOM доступным.
    /// </summary>
    bool IsDOMEnabled { get; set; }

    /// <summary>
    /// Указывает, включен ли JavaScript.
    /// </summary>
    bool IsJavaScriptEnabled { get; set; }

    /// <summary>
    /// Настройки браузера Selenium по умолчанию.
    /// </summary>
    static abstract IWebBrowserSettings Default { get; }
}