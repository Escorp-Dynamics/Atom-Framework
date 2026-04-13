namespace Atom.Net.Https;

/// <summary>
/// Тип запроса.
/// </summary>
public enum RequestKind
{
    /// <summary>
    /// Неизвестный запрос.
    /// </summary>
    Unknown,
    /// <summary>
    /// Запрос навигации.
    /// </summary>
    Navigation,
    /// <summary>
    /// Общий browser subresource/fetch bucket.
    /// Сюда попадают не только вызовы Fetch API, но и другие не-navigation запросы,
    /// когда точная wire semantics задается через <see cref="HttpsBrowserRequestContext"/>.
    /// </summary>
    Fetch,
    /// <summary>
    /// Выделенный preload bucket для browser-driven preload flows,
    /// например link rel=preload, когда request shaping отличается от общего Fetch bucket.
    /// </summary>
    Preload,
    /// <summary>
    /// Выделенный bucket для link rel=modulepreload.
    /// Chromium wire semantics здесь отличаются от общего preload bucket,
    /// прежде всего по cors mode, Origin и Accept-Encoding surface.
    /// </summary>
    ModulePreload,
    /// <summary>
    /// Выделенный bucket для link rel=prefetch.
    /// Chromium wire semantics здесь отличаются от общего fetch/preload bucket:
    /// sec-fetch-dest=empty, sec-fetch-mode=no-cors, Sec-Purpose=prefetch,
    /// а также более узкий Accept-Encoding surface.
    /// </summary>
    Prefetch,
    /// <summary>
    /// Выделенный bucket для service worker script bootstrap path.
    /// Другие worker-related requests могут оставаться в Fetch bucket с явным browser context.
    /// </summary>
    ServiceWorker,
}