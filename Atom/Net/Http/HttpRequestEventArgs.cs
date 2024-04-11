using System.Net;

namespace Atom.Net.Http;

/// <summary>
/// Представляет аргументы события HTTP-запроса.
/// </summary>
public class HttpRequestEventArgs : AsyncEventArgs
{
    /// <summary>
    /// Адрес запроса.
    /// </summary>
    public Uri? Url { get; set; }

    /// <summary>
    /// Метод запроса.
    /// </summary>
    public HttpMethod Method { get; set; }

    /// <summary>
    /// Прокси запроса.
    /// </summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Заголовки запроса.
    /// </summary>
    public IDictionary<string, string> RequestHeaders { get; }

    /// <summary>
    /// Тело запроса.
    /// </summary>
    public string? RequestBody { get; set; }

    /// <summary>
    /// Версия запроса.
    /// </summary>
    public Version RequestVersion { get; set; }

    /// <summary>
    /// Заголовки ответа.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; set; }

    /// <summary>
    /// Тело ответа.
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Версия ответа.
    /// </summary>
    public Version? ResponseVersion { get; set; }

    /// <summary>
    /// Код статуса ответа.
    /// </summary>
    public HttpStatusCode? StatusCode { get; set; }

    /// <summary>
    /// Описание статуса ответа.
    /// </summary>
    public string? ReasonPhrase { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestHeaders">Заголовки запроса.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="proxy">Прокси запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public HttpRequestEventArgs(Uri? url, HttpMethod method, IDictionary<string, string> requestHeaders, Version requestVersion, IWebProxy? proxy, CancellationToken cancellationToken)
    {
        Url = url;
        Method = method;
        RequestHeaders = requestHeaders;
        RequestVersion = requestVersion;
        Proxy = proxy;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestHeaders">Заголовки запроса.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public HttpRequestEventArgs(Uri? url, HttpMethod method, IDictionary<string, string> requestHeaders, Version requestVersion, CancellationToken cancellationToken)
        : this(url, method, requestHeaders, requestVersion, default, cancellationToken) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public HttpRequestEventArgs(HttpMethod method, Version requestVersion, CancellationToken cancellationToken)
        : this(default, method, new Dictionary<string, string>(), requestVersion, cancellationToken) { }
}