using System.Net;
using Atom.Architect.Reactive;
using Atom.Web.Browsing.BOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет веб-страницу браузера.
/// </summary>
public interface IWebPage : IAsyncDisposable
{
    /// <summary>
    /// Происходит в момент начала навигации по странице.
    /// </summary>
    event MutableEventHandler<Uri>? Navigate;

    /// <summary>
    /// Происходит в момент окончания навигации по странице.
    /// </summary>
    event MutableEventHandler<Uri>? Navigated;

    /// <summary>
    /// Происходит в момент закрытия веб-страницы.
    /// </summary>
    event MutableEventHandler? Closed;

    /// <summary>
    /// Окно, в котором открыта текущая веб-страница.
    /// </summary>
    IWebWindow Window { get; }

    /// <summary>
    /// Контекст веб-браузера, в котором открыта страница.
    /// </summary>
    IWebBrowserContext Context => Window.Context;

    /// <summary>
    /// Веб-браузер, в котором открыта страница.
    /// </summary>
    IWebBrowser Browser => Context.Browser;

    /// <summary>
    /// Настройки веб-страницы.
    /// </summary>
    IWebPageSettings Settings { get; }

    /// <summary>
    /// Указывает, была ли веб-страница закрыта.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Исходный код веб-страницы браузера.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Текущий адрес страницы.
    /// </summary>
    Uri Url { get; }

    /// <summary>
    /// Консоль отладки страницы.
    /// </summary>
    IConsole Console { get; }

    /// <summary>
    /// Куки страницы.
    /// </summary>
    CookieContainer Cookies { get; }

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait) => GoToAsync(url, headers, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) => GoToAsync(url, headers, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, IReadOnlyDictionary<string, string> headers) => GoToAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer, ReadinessState wait, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(referer)) headers["referer"] = referer;
        return GoToAsync(url, headers, wait, cancellationToken);
    }

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer, ReadinessState wait) => GoToAsync(url, referer, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer, CancellationToken cancellationToken) => GoToAsync(url, referer, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, string referer) => GoToAsync(url, referer, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, ReadinessState wait, CancellationToken cancellationToken) => GoToAsync(url, string.Empty, wait, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, ReadinessState wait) => GoToAsync(url, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url, CancellationToken cancellationToken) => GoToAsync(url, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    ValueTask<HttpStatusCode> GoToAsync(Uri url) => GoToAsync(url, CancellationToken.None);

    /// <summary>
    /// Закрывает веб-страницу.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает веб-страницу.
    /// </summary>
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);
}