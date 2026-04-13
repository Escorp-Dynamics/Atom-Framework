namespace Atom.Net.Https;

/// <summary>
/// Явный browser-shaped контекст запроса, который позволяет уточнить destination и fetch mode
/// без изменения базового API <see cref="HttpsRequestMessage"/>.
/// Для media elements отдельный kind пока не нужен: current audio/video semantics выражаются
/// через Destination=Audio|Video и FetchMode=NoCors поверх общего Fetch bucket.
/// </summary>
public sealed class HttpsBrowserRequestContext
{
    /// <summary>
    /// Тип инициатора, если он известен вызывающему коду.
    /// </summary>
    public HttpsRequestInitiatorType? InitiatorType { get; init; }

    /// <summary>
    /// Явный destination для sec-fetch-dest.
    /// </summary>
    public HttpsRequestDestination? Destination { get; init; }

    /// <summary>
    /// Явный fetch mode для sec-fetch-mode.
    /// </summary>
    public HttpsFetchMode? FetchMode { get; init; }

    /// <summary>
    /// Указывает, была ли navigation user-activated. Влияет на sec-fetch-user.
    /// </summary>
    public bool? IsUserActivated { get; init; }

    /// <summary>
    /// Указывает, что navigation является reload и не должна получать sec-fetch-user.
    /// </summary>
    public bool? IsReload { get; init; }

    /// <summary>
    /// Указывает, что navigation была инициирована HTML form submission.
    /// </summary>
    public bool? IsFormSubmission { get; init; }

    /// <summary>
    /// Указывает, является ли navigation top-level. Если false, navigation трактуется как iframe.
    /// </summary>
    public bool? IsTopLevelNavigation { get; init; }
}

/// <summary>
/// Нормализованный initiator type browser request context.
/// Это fallback inference signal, а не прямой заменитель destination.
/// </summary>
public enum HttpsRequestInitiatorType
{
    Default,
    Document,
    Iframe,
    Worker,
    SharedWorker,
    ServiceWorker,
    Script,
    Fetch,
    Preload,
}

/// <summary>
/// Нормализованный destination browser request context.
/// </summary>
public enum HttpsRequestDestination
{
    Empty,
    Document,
    Iframe,
    Script,
    Style,
    Image,
    Font,
    Audio,
    Video,
    Track,
    Manifest,
    ServiceWorker,
    Worker,
    SharedWorker,
}

/// <summary>
/// Нормализованный fetch mode browser request context.
/// </summary>
public enum HttpsFetchMode
{
    Navigate,
    NoCors,
    Cors,
    SameOrigin,
}