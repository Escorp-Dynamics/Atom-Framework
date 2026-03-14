using System.Net;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Политика повторных попыток для HTTP-запросов Polymarket REST API.
/// </summary>
/// <remarks>
/// Поддерживает экспоненциальный backoff с jitter. Повторяет:
/// <list type="bullet">
///   <item>429 Too Many Requests (с учётом заголовка Retry-After)</item>
///   <item>5xx серверные ошибки</item>
///   <item>Транзитные сетевые ошибки (<see cref="HttpRequestException"/>)</item>
///   <item><see cref="TaskCanceledException"/> (таймаут HTTP-клиента)</item>
/// </list>
/// Потокобезопасен. Совместим с NativeAOT.
/// </remarks>
public sealed class PolymarketRetryPolicy
{
    /// <summary>
    /// Максимальное количество попыток по умолчанию.
    /// </summary>
    public const int DefaultMaxRetries = 3;

    /// <summary>
    /// Начальная задержка по умолчанию.
    /// </summary>
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Максимальная задержка по умолчанию.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(30);

    private static readonly Random JitterRandom = new();

    /// <summary>
    /// Максимальное количество повторных попыток.
    /// </summary>
    public int MaxRetries { get; }

    /// <summary>
    /// Начальная задержка перед первым повтором.
    /// </summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>
    /// Максимальная задержка между попытками.
    /// </summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>
    /// Инициализирует политику повторных попыток с настройками по умолчанию.
    /// </summary>
    public PolymarketRetryPolicy()
        : this(DefaultMaxRetries, DefaultInitialDelay, DefaultMaxDelay) { }

    /// <summary>
    /// Инициализирует политику повторных попыток с пользовательскими настройками.
    /// </summary>
    /// <param name="maxRetries">Максимальное количество повторных попыток.</param>
    /// <param name="initialDelay">Начальная задержка.</param>
    /// <param name="maxDelay">Максимальная задержка.</param>
    public PolymarketRetryPolicy(int maxRetries, TimeSpan initialDelay, TimeSpan maxDelay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDelay, TimeSpan.Zero);

        MaxRetries = maxRetries;
        InitialDelay = initialDelay;
        MaxDelay = maxDelay;
    }

    /// <summary>
    /// Выполняет HTTP-операцию с политикой повторных попыток.
    /// </summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="operation">Асинхронная HTTP-операция.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат успешной операции.</returns>
    /// <exception cref="HttpRequestException">Все попытки исчерпаны.</exception>
    public async ValueTask<T> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var attempt = 0;
        var delay = InitialDelay;

        while (true)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries && IsRetryable(ex))
            {
                var retryAfter = GetRetryAfterDelay(ex);
                delay = retryAfter ?? delay;

                await WaitWithJitter(delay, cancellationToken).ConfigureAwait(false);

                attempt++;
                delay = NextDelay(delay);
            }
            catch (TaskCanceledException ex) when (attempt < MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                // Таймаут HTTP-клиента (не пользовательская отмена)
                await WaitWithJitter(delay, cancellationToken).ConfigureAwait(false);

                attempt++;
                delay = NextDelay(delay);
            }
        }
    }

    /// <summary>
    /// Определяет, можно ли повторить запрос для данной ошибки.
    /// </summary>
    internal static bool IsRetryable(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or null; // null StatusCode = сетевая ошибка

    /// <summary>
    /// Вычисляет следующую задержку (экспоненциальный backoff).
    /// </summary>
    private TimeSpan NextDelay(TimeSpan current) =>
        TimeSpan.FromTicks(Math.Min(current.Ticks * 2, MaxDelay.Ticks));

    /// <summary>
    /// Ожидает с добавлением jitter (±25%) для предотвращения thundering herd.
    /// </summary>
    private static async ValueTask WaitWithJitter(TimeSpan delay, CancellationToken ct)
    {
        // Jitter: ±25% от задержки
        double jitterFactor;
        lock (JitterRandom)
            jitterFactor = 0.75 + (JitterRandom.NextDouble() * 0.5);

        var jitteredDelay = TimeSpan.FromTicks((long)(delay.Ticks * jitterFactor));
        await Task.Delay(jitteredDelay, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Извлекает задержку из заголовка Retry-After (если он в секундах).
    /// </summary>
    private static TimeSpan? GetRetryAfterDelay(HttpRequestException ex) =>
        ex.StatusCode == HttpStatusCode.TooManyRequests && ex.Data.Contains("RetryAfterSeconds")
            ? TimeSpan.FromSeconds(Convert.ToDouble(ex.Data["RetryAfterSeconds"]))
            : null;
}
