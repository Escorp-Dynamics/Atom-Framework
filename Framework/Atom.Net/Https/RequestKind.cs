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
    /// Запрос Fetch API.
    /// </summary>
    Fetch,
    /// <summary>
    /// Запрос предзагрузки.
    /// </summary>
    Preload,
    /// <summary>
    /// Запрос сервисного воркера.
    /// </summary>
    ServiceWorker,
}