using System.Collections.Concurrent;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Мониторинг разрешения рынков Polymarket.
/// Периодически опрашивает REST API для обнаружения закрытых и разрешённых рынков.
/// </summary>
/// <remarks>
/// Позволяет отслеживать набор рынков и получать уведомления при:
/// - Закрытии рынка (торги остановлены).
/// - Разрешении рынка (определён победитель).
/// Совместим с NativeAOT. Потокобезопасен.
/// </remarks>
public sealed class PolymarketEventResolver : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Интервал опроса по умолчанию (60 секунд).
    /// </summary>
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(60);

    private readonly PolymarketRestClient restClient;
    private readonly bool disposeRestClient;
    private readonly TimeSpan pollInterval;
    private readonly ConcurrentDictionary<string, PolymarketTrackedMarket> trackedMarkets = new();
    private CancellationTokenSource? pollCts;
    private Task? pollLoopTask;
    private bool isDisposed;

    /// <summary>
    /// Событие при разрешении рынка (определён победитель).
    /// </summary>
    public event AsyncEventHandler<PolymarketEventResolver, PolymarketMarketResolvedEventArgs>? MarketResolved;

    /// <summary>
    /// Событие при закрытии рынка (торги остановлены, исход ещё не определён).
    /// </summary>
    public event AsyncEventHandler<PolymarketEventResolver, PolymarketMarketClosedEventArgs>? MarketClosed;

    /// <summary>
    /// Инициализирует EventResolver с новым REST-клиентом.
    /// </summary>
    /// <param name="pollInterval">Интервал опроса API (по умолчанию 60 секунд).</param>
    public PolymarketEventResolver(TimeSpan pollInterval = default)
    {
        restClient = new PolymarketRestClient();
        disposeRestClient = true;
        this.pollInterval = pollInterval == default ? DefaultPollInterval : pollInterval;
    }

    /// <summary>
    /// Инициализирует EventResolver с существующим REST-клиентом.
    /// </summary>
    /// <param name="restClient">REST-клиент Polymarket.</param>
    /// <param name="pollInterval">Интервал опроса API (по умолчанию 60 секунд).</param>
    public PolymarketEventResolver(PolymarketRestClient restClient, TimeSpan pollInterval = default)
    {
        ArgumentNullException.ThrowIfNull(restClient);
        this.restClient = restClient;
        disposeRestClient = false;
        this.pollInterval = pollInterval == default ? DefaultPollInterval : pollInterval;
    }

    /// <summary>
    /// Текущие отслеживаемые рынки.
    /// </summary>
    public IReadOnlyDictionary<string, PolymarketTrackedMarket> TrackedMarkets => trackedMarkets;

    /// <summary>
    /// Количество отслеживаемых рынков.
    /// </summary>
    public int TrackedCount => trackedMarkets.Count;

    /// <summary>
    /// Запущен ли цикл опроса.
    /// </summary>
    public bool IsPolling => pollLoopTask is not null && !pollLoopTask.IsCompleted;

    /// <summary>
    /// Добавляет рынок в мониторинг.
    /// </summary>
    /// <param name="conditionId">Идентификатор рынка (condition ID).</param>
    public void Track(string conditionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(conditionId);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        trackedMarkets.TryAdd(conditionId, new PolymarketTrackedMarket
        {
            ConditionId = conditionId
        });
    }

    /// <summary>
    /// Добавляет несколько рынков в мониторинг.
    /// </summary>
    /// <param name="conditionIds">Идентификаторы рынков.</param>
    public void TrackMany(string[] conditionIds)
    {
        ArgumentNullException.ThrowIfNull(conditionIds);
        foreach (var id in conditionIds)
            Track(id);
    }

    /// <summary>
    /// Удаляет рынок из мониторинга.
    /// </summary>
    /// <param name="conditionId">Идентификатор рынка.</param>
    public void Untrack(string conditionId) =>
        trackedMarkets.TryRemove(conditionId, out _);

    /// <summary>
    /// Получает текущий статус рынка в мониторинге.
    /// </summary>
    /// <param name="conditionId">Идентификатор рынка.</param>
    public PolymarketMarketStatus GetMarketStatus(string conditionId)
    {
        if (!trackedMarkets.TryGetValue(conditionId, out var tracked))
            return PolymarketMarketStatus.Unknown;

        if (tracked.IsResolved) return PolymarketMarketStatus.Resolved;
        if (tracked.IsClosed) return PolymarketMarketStatus.Closed;
        return PolymarketMarketStatus.Active;
    }

    /// <summary>
    /// Запускает цикл автоматического опроса рынков.
    /// </summary>
    public void StartPolling()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (IsPolling)
            return;

        pollCts = new CancellationTokenSource();
        pollLoopTask = Task.Run(() => PollLoopAsync(pollCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Останавливает цикл опроса.
    /// </summary>
    public async ValueTask StopPollingAsync()
    {
        if (pollCts is null)
            return;

        await pollCts.CancelAsync().ConfigureAwait(false);

        if (pollLoopTask is not null)
        {
            try { await pollLoopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        pollCts.Dispose();
        pollCts = null;
        pollLoopTask = null;
    }

    /// <summary>
    /// Выполняет единоразовую проверку всех отслеживаемых рынков.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask CheckAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        foreach (var kvp in trackedMarkets)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await CheckMarketAsync(kvp.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Удаляет все отслеживаемые рынки.
    /// </summary>
    public void ClearTracked() => trackedMarkets.Clear();

    #region Внутренняя логика опроса

    /// <summary>
    /// Цикл периодического опроса рынков.
    /// </summary>
    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach (var kvp in trackedMarkets)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Пропуск уже разрешённых рынков
                    if (kvp.Value.IsResolved)
                        continue;

                    await CheckMarketAsync(kvp.Value, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Ошибки опроса не должны останавливать цикл
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Проверяет отдельный рынок на закрытие/разрешение.
    /// </summary>
    private async ValueTask CheckMarketAsync(PolymarketTrackedMarket tracked, CancellationToken cancellationToken)
    {
        try
        {
            var market = await restClient.GetMarketAsync(tracked.ConditionId, cancellationToken).ConfigureAwait(false);
            if (market is null) return;

            tracked.Question = market.Question;
            tracked.NegRisk = market.NegRisk;
            tracked.Tokens = market.Tokens;
            tracked.LastCheckTicks = Environment.TickCount64;

            // Проверка закрытия
            if (market.Closed && !tracked.IsClosed)
            {
                tracked.IsClosed = true;
                if (MarketClosed is not null)
                    await MarketClosed.Invoke(this, new PolymarketMarketClosedEventArgs(market)).ConfigureAwait(false);
            }

            // Проверка разрешения — ищем токен с winner != null
            if (market.Closed && market.Tokens is { Length: > 0 } && !tracked.IsResolved)
            {
                var winnerToken = Array.Find(market.Tokens, t => t.Winner is not null);
                if (winnerToken is not null)
                {
                    tracked.IsResolved = true;
                    var loserToken = Array.Find(market.Tokens, t => t.TokenId != winnerToken.TokenId);

                    var resolution = new PolymarketResolution
                    {
                        ConditionId = tracked.ConditionId,
                        Question = market.Question,
                        WinningOutcome = winnerToken.Outcome,
                        WinnerTokenId = winnerToken.TokenId,
                        LoserTokenId = loserToken?.TokenId,
                        NegRisk = market.NegRisk,
                        ResolvedAtTicks = Environment.TickCount64,
                        IsVoided = false
                    };

                    if (MarketResolved is not null)
                        await MarketResolved.Invoke(this, new PolymarketMarketResolvedEventArgs(resolution)).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ошибки при проверке одного рынка не должны блокировать проверку остальных
        }
    }

    #endregion

    /// <summary>
    /// Освобождает ресурсы асинхронно.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        await StopPollingAsync().ConfigureAwait(false);

        if (disposeRestClient)
            restClient.Dispose();
    }

    /// <summary>
    /// Освобождает ресурсы синхронно.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        pollCts?.Cancel();
        pollCts?.Dispose();

        if (disposeRestClient)
            restClient.Dispose();
    }
}
