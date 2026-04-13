using Atom.Net.Https.Headers;

namespace Atom.Net.Https;

/// <summary>
/// Внутренний режим обработки referrer policy для browser-shaped request layer.
/// </summary>
public enum ReferrerPolicyMode
{
    NoReferrer,
    Origin,
    SameOrigin,
    StrictOrigin,
    StrictOriginWhenCrossOrigin,
    UnsafeUrl,
}

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
    /// Явный override для referrer policy текущего запроса.
    /// Если не задан, effective policy выбирается из browser profile.
    /// </summary>
    public ReferrerPolicyMode? ReferrerPolicy { get; init; }

    internal IHeadersFormattingPolicy? HeadersFormattingPolicy { get; set; }

    internal bool UseCookieCrumbling { get; set; }

    internal RequestKind EffectiveKind { get; set; }

    internal ReferrerPolicyMode EffectiveReferrerPolicy { get; set; }

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