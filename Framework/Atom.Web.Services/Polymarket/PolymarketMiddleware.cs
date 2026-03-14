using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Middleware логирования HTTP-запросов и ответов.
/// </summary>
/// <remarks>
/// Логирует метод, путь, статус-код и время выполнения каждого запроса.
/// Используется делегат для вывода, чтобы не зависеть от конкретной системы логирования.
/// </remarks>
public sealed class PolymarketLoggingMiddleware(Action<string> log) : IPolymarketMiddleware
{
    /// <summary>
    /// Создаёт middleware с логированием в Console.
    /// </summary>
    public PolymarketLoggingMiddleware() : this(Console.WriteLine) { }

    /// <inheritdoc/>
    public ValueTask<bool> OnRequestAsync(PolymarketRequestContext context, CancellationToken cancellationToken = default)
    {
        log($"[Polymarket] → {context.Method} {context.Path}");
        return new(true);
    }

    /// <inheritdoc/>
    public ValueTask OnResponseAsync(PolymarketResponseContext context, CancellationToken cancellationToken = default)
    {
        if (context.Exception is not null)
            log($"[Polymarket] ✗ {context.Request.Method} {context.Request.Path} — {context.Exception.Message} ({context.ElapsedMs:F1}ms)");
        else
            log($"[Polymarket] ✓ {context.Request.Method} {context.Request.Path} → {context.StatusCode} ({context.ElapsedMs:F1}ms)");

        return default;
    }
}

/// <summary>
/// Middleware сбора метрик HTTP-запросов.
/// </summary>
/// <remarks>
/// Подсчитывает количество запросов, ошибок и среднее время ответа.
/// Потокобезопасен. Совместим с NativeAOT.
/// </remarks>
public sealed class PolymarketMetricsMiddleware : IPolymarketMiddleware
{
    private long totalRequests;
    private long failedRequests;
    private long totalElapsedTicks;
    private readonly ConcurrentDictionary<int, long> statusCodeCounts = new();

    /// <summary>
    /// Общее количество выполненных запросов.
    /// </summary>
    public long TotalRequests => Interlocked.Read(ref totalRequests);

    /// <summary>
    /// Количество неуспешных запросов (исключения или 4xx/5xx).
    /// </summary>
    public long FailedRequests => Interlocked.Read(ref failedRequests);

    /// <summary>
    /// Среднее время ответа (миллисекунды).
    /// </summary>
    public double AverageResponseTimeMs
    {
        get
        {
            var count = Interlocked.Read(ref totalRequests);
            if (count == 0) return 0;
            var ticks = Interlocked.Read(ref totalElapsedTicks);
            return (double)ticks / count / TimeSpan.TicksPerMillisecond;
        }
    }

    /// <summary>
    /// Статистика по HTTP-кодам ответа.
    /// </summary>
    public IReadOnlyDictionary<int, long> StatusCodeStats => statusCodeCounts;

    /// <inheritdoc/>
    public ValueTask OnResponseAsync(PolymarketResponseContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref totalRequests);
        Interlocked.Add(ref totalElapsedTicks, (long)(context.ElapsedMs * TimeSpan.TicksPerMillisecond));

        if (!context.IsSuccess)
            Interlocked.Increment(ref failedRequests);

        if (context.StatusCode > 0)
            statusCodeCounts.AddOrUpdate(context.StatusCode, 1, (_, count) => count + 1);

        return default;
    }

    /// <summary>
    /// Сбрасывает все метрики.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref totalRequests, 0);
        Interlocked.Exchange(ref failedRequests, 0);
        Interlocked.Exchange(ref totalElapsedTicks, 0);
        statusCodeCounts.Clear();
    }
}

/// <summary>
/// Middleware для добавления кастомных заголовков ко всем запросам.
/// </summary>
public sealed class PolymarketHeadersMiddleware : IPolymarketMiddleware
{
    private readonly Dictionary<string, string> headers = [];

    /// <summary>
    /// Добавляет заголовок, который будет отправляться со всеми запросами.
    /// </summary>
    /// <param name="name">Имя заголовка.</param>
    /// <param name="value">Значение заголовка.</param>
    public PolymarketHeadersMiddleware AddHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        headers[name] = value;
        return this;
    }

    /// <summary>
    /// Возвращает настроенные заголовки для применения к HttpRequestMessage.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers => headers;

    /// <inheritdoc/>
    public ValueTask<bool> OnRequestAsync(PolymarketRequestContext context, CancellationToken cancellationToken = default)
    {
        // Заголовки сохраняются в Properties для применения в RestClient
        context.Properties["CustomHeaders"] = headers;
        return new(true);
    }
}
