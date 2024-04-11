using System.Net;

namespace Atom.Net.Http;

/// <summary>
/// Представляет аргументы события неудачного HTTP-запроса.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
/// </remarks>
/// <param name="url">Адрес запроса.</param>
/// <param name="method">Метод запроса.</param>
/// <param name="requestHeaders">Заголовки запроса.</param>
/// <param name="requestVersion">Версия запроса.</param>
/// <param name="proxy">Прокси запроса.</param>
/// <param name="ex">Исключение, возникшее в процессе выполнения процедуры.</param>
/// <param name="cancellationToken">Токен отмены задачи.</param>
public class HttpRequestFailedEventArgs(Uri? url, HttpMethod method, IDictionary<string, string> requestHeaders, Version requestVersion, IWebProxy? proxy, Exception? ex, CancellationToken cancellationToken)
    : HttpRequestEventArgs(url, method, requestHeaders, requestVersion, proxy, cancellationToken)
{
    /// <summary>
    /// Исключение, возникшее в процессе выполнения процедуры.
    /// </summary>
    public Exception? Exception { get; set; } = ex;

    /// <summary>
    /// Определяет, поддерживается ли возможность повторить выполнение процедуры после обработчика события.
    /// </summary>
    public bool IsRetry { get; set; }

    /// <summary>
    /// Время ожидания после выполнения обработчика.
    /// </summary>
    public TimeSpan Timeout { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestHeaders">Заголовки запроса.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="proxy">Прокси запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public HttpRequestFailedEventArgs(Uri? url, HttpMethod method, IDictionary<string, string> requestHeaders, Version requestVersion, IWebProxy? proxy, CancellationToken cancellationToken)
        : this(url, method, requestHeaders, requestVersion, proxy, default, cancellationToken) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestHeaders">Заголовки запроса.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="ex">Исключение, возникшее в процессе выполнения процедуры.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public HttpRequestFailedEventArgs(Uri? url, HttpMethod method, IDictionary<string, string> requestHeaders, Version requestVersion, Exception? ex, CancellationToken cancellationToken)
        : this(url, method, requestHeaders, requestVersion, default, ex, cancellationToken) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestHeaders">Заголовки запроса.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public HttpRequestFailedEventArgs(Uri? url, HttpMethod method, IDictionary<string, string> requestHeaders, Version requestVersion, CancellationToken cancellationToken)
        : this(url, method, requestHeaders, requestVersion, default, default, cancellationToken) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpRequestFailedEventArgs"/>.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestVersion">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public HttpRequestFailedEventArgs(HttpMethod method, Version requestVersion, CancellationToken cancellationToken) : this(default, method, new Dictionary<string, string>(), requestVersion, cancellationToken) { }
}