namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Контекст HTTP-запроса, проходящий через pipeline middleware.
/// </summary>
public sealed class PolymarketRequestContext
{
    /// <summary>
    /// HTTP-метод запроса.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Путь запроса (без base URL).
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Полный URL запроса.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Тело запроса (null для GET/DELETE без тела).
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Является ли запрос аутентифицированным.
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Метка начала запроса.
    /// </summary>
    public long TimestampTicks { get; init; } = Environment.TickCount64;

    /// <summary>
    /// Пользовательские свойства для передачи данных между middleware.
    /// </summary>
    public Dictionary<string, object> Properties { get; } = [];
}

/// <summary>
/// Контекст HTTP-ответа, проходящий через pipeline middleware.
/// </summary>
public sealed class PolymarketResponseContext
{
    /// <summary>
    /// Исходный контекст запроса.
    /// </summary>
    public required PolymarketRequestContext Request { get; init; }

    /// <summary>
    /// HTTP-код ответа.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Время выполнения запроса (миллисекунды).
    /// </summary>
    public double ElapsedMs { get; init; }

    /// <summary>
    /// Исключение, если запрос завершился с ошибкой (null при успехе).
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Признак успешности запроса.
    /// </summary>
    public bool IsSuccess => Exception is null && StatusCode is >= 200 and < 300;
}

/// <summary>
/// Middleware для pipeline обработки HTTP-запросов/ответов Polymarket REST API.
/// </summary>
/// <remarks>
/// Позволяет перехватывать запросы перед отправкой и ответы после получения.
/// Типичные применения: логирование, метрики, кастомные заголовки, аудит.
/// </remarks>
public interface IPolymarketMiddleware
{
    /// <summary>
    /// Вызывается перед отправкой HTTP-запроса.
    /// </summary>
    /// <param name="context">Контекст запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true — продолжить выполнение; false — прервать pipeline.</returns>
    ValueTask<bool> OnRequestAsync(PolymarketRequestContext context, CancellationToken cancellationToken = default) =>
        new(true);

    /// <summary>
    /// Вызывается после получения HTTP-ответа (или ошибки).
    /// </summary>
    /// <param name="context">Контекст ответа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask OnResponseAsync(PolymarketResponseContext context, CancellationToken cancellationToken = default) =>
        default;
}
