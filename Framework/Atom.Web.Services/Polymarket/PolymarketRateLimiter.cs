using System.Runtime.CompilerServices;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Rate limiter на основе алгоритма Token Bucket для REST API Polymarket.
/// </summary>
/// <remarks>
/// Polymarket CLOB API ограничивает количество запросов: ~100 запросов за 10 секунд.
/// При превышении лимита API возвращает 429 Too Many Requests.
/// Этот лимитер предотвращает превышение, ожидая доступности токена перед каждым запросом.
/// Потокобезопасен. Совместим с NativeAOT.
/// </remarks>
public sealed class PolymarketRateLimiter
{
    private readonly double maxTokens;
    private readonly double refillRate; // токенов в секунду
    private readonly object syncLock = new();
    private double currentTokens;
    private long lastRefillTicks;

    /// <summary>
    /// Инициализирует rate limiter с настройками по умолчанию (100 запросов за 10 секунд).
    /// </summary>
    public PolymarketRateLimiter() : this(maxTokens: 100, refillPeriodSeconds: 10) { }

    /// <summary>
    /// Инициализирует rate limiter с пользовательскими настройками.
    /// </summary>
    /// <param name="maxTokens">Максимальное количество токенов (размер корзины).</param>
    /// <param name="refillPeriodSeconds">Период пополнения всех токенов (секунды).</param>
    public PolymarketRateLimiter(double maxTokens, double refillPeriodSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxTokens, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refillPeriodSeconds, 0);

        this.maxTokens = maxTokens;
        refillRate = maxTokens / refillPeriodSeconds;
        currentTokens = maxTokens;
        lastRefillTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Максимальное количество токенов.
    /// </summary>
    public double MaxTokens => maxTokens;

    /// <summary>
    /// Текущее количество доступных токенов.
    /// </summary>
    public double AvailableTokens
    {
        get
        {
            lock (syncLock)
            {
                Refill();
                return currentTokens;
            }
        }
    }

    /// <summary>
    /// Ожидает доступности токена и потребляет его.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan waitTime;

            lock (syncLock)
            {
                Refill();

                if (currentTokens >= 1.0)
                {
                    currentTokens -= 1.0;
                    return;
                }

                // Вычисляем время ожидания до следующего токена
                var tokensNeeded = 1.0 - currentTokens;
                var waitSeconds = tokensNeeded / refillRate;
                waitTime = TimeSpan.FromSeconds(waitSeconds);
            }

            await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Пробует потребить токен без ожидания.
    /// </summary>
    /// <returns>true, если токен был доступен и потреблён.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire()
    {
        lock (syncLock)
        {
            Refill();

            if (currentTokens < 1.0)
                return false;

            currentTokens -= 1.0;
            return true;
        }
    }

    /// <summary>
    /// Пополняет токены на основе прошедшего времени.
    /// </summary>
    private void Refill()
    {
        var now = Environment.TickCount64;
        var elapsedMs = now - lastRefillTicks;

        if (elapsedMs <= 0) return;

        var tokensToAdd = (elapsedMs / 1000.0) * refillRate;
        currentTokens = Math.Min(maxTokens, currentTokens + tokensToAdd);
        lastRefillTicks = now;
    }
}
