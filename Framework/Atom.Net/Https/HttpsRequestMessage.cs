namespace Atom.Net.Https;

/// <summary>
/// Представляет данные HTTPS-запроса.
/// </summary>
public class HttpsRequestMessage : HttpRequestMessage
{
    /// <summary>
    /// Тип запроса (эмулируется браузерное поведение).
    /// </summary>
    public RequestKind Kind { get; init; }

    /// <summary>
    /// Пользовательский контекст (например, связанный с движком V8).
    /// </summary>
    public object? Context { get; init; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsRequestMessage"/>.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestUri">Адрес запроса.</param>
    public HttpsRequestMessage(HttpMethod method, Uri? requestUri) : base(method, requestUri) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsRequestMessage"/>.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <param name="requestUri">Адрес запроса.</param>
    public HttpsRequestMessage(HttpMethod method, string? requestUri) : base(method, requestUri) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsRequestMessage"/>.
    /// </summary>
    public HttpsRequestMessage() : base() { }
}