using System.Collections.Concurrent;

using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Снимок P&amp;L портфеля в определённый момент времени.
/// </summary>
public sealed class PolymarketPnLSnapshot
    : IMarketPnLSnapshot
{
    /// <summary>
    /// Временная метка снимка (Environment.TickCount64).
    /// </summary>
    public long TimestampTicks { get; init; }

    /// <summary>
    /// Абсолютное время снимка (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Суммарная рыночная стоимость открытых позиций.
    /// </summary>
    public double TotalMarketValue { get; init; }

    /// <summary>
    /// Суммарная стоимость входа открытых позиций.
    /// </summary>
    public double TotalCostBasis { get; init; }

    /// <summary>
    /// Нереализованный P&amp;L.
    /// </summary>
    public double UnrealizedPnL { get; init; }

    /// <summary>
    /// Реализованный P&amp;L (кумулятивный).
    /// </summary>
    public double RealizedPnL { get; init; }

    /// <summary>
    /// Суммарные комиссии (кумулятивные).
    /// </summary>
    public double TotalFees { get; init; }

    /// <summary>
    /// Чистый P&amp;L = Realized + Unrealized - Fees.
    /// </summary>
    public double NetPnL => RealizedPnL + UnrealizedPnL - TotalFees;

    /// <summary>
    /// Количество открытых позиций в момент снимка.
    /// </summary>
    public int OpenPositions { get; init; }
}

/// <summary>
/// Аргументы события нового снимка P&amp;L.
/// </summary>
public sealed class PolymarketPnLSnapshotEventArgs(PolymarketPnLSnapshot snapshot) : EventArgs
{
    /// <summary>
    /// Новый снимок P&amp;L.
    /// </summary>
    public PolymarketPnLSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// Записывает историю P&amp;L портфеля Polymarket с заданным интервалом.
/// Подключается к <see cref="PolymarketPortfolioTracker"/> и периодически сохраняет снимки.
/// </summary>
/// <remarks>
/// Совместим с NativeAOT. Потокобезопасен.
/// Хранит историю в памяти с настраиваемым максимальным размером.
/// </remarks>
public sealed class PolymarketPnLHistory : IMarketPnLHistory, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Интервал записи снимков по умолчанию (5 минут).
    /// </summary>
    private static readonly TimeSpan DefaultSnapshotInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Максимальное количество снимков по умолчанию (288 = 24 часа при 5-минутном интервале).
    /// </summary>
    private const int DefaultMaxSnapshots = 288;

    private readonly PolymarketPortfolioTracker tracker;
    private readonly TimeSpan snapshotInterval;
    private readonly int maxSnapshots;
    private readonly ConcurrentQueue<PolymarketPnLSnapshot> history = new();
    private CancellationTokenSource? snapshotCts;
    private Task? snapshotLoopTask;
    private bool isDisposed;

    /// <summary>
    /// Событие при записи нового снимка P&amp;L.
    /// </summary>
    public event AsyncEventHandler<PolymarketPnLHistory, PolymarketPnLSnapshotEventArgs>? SnapshotRecorded;

    /// <summary>
    /// Инициализирует историю P&amp;L.
    /// </summary>
    /// <param name="tracker">Трекер портфеля для получения данных.</param>
    /// <param name="snapshotInterval">Интервал записи снимков (по умолчанию 5 минут).</param>
    /// <param name="maxSnapshots">Максимальное количество снимков в буфере (по умолчанию 288).</param>
    public PolymarketPnLHistory(
        PolymarketPortfolioTracker tracker,
        TimeSpan snapshotInterval = default,
        int maxSnapshots = DefaultMaxSnapshots)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSnapshots);

        this.tracker = tracker;
        this.snapshotInterval = snapshotInterval == default ? DefaultSnapshotInterval : snapshotInterval;
        this.maxSnapshots = maxSnapshots;
    }

    /// <summary>
    /// История снимков P&amp;L (от старых к новым).
    /// </summary>
    public IReadOnlyCollection<PolymarketPnLSnapshot> Snapshots => history;

    /// <summary>
    /// Количество снимков в истории.
    /// </summary>
    public int Count => history.Count;

    /// <summary>
    /// Последний снимок P&amp;L (или null, если история пуста).
    /// </summary>
    public PolymarketPnLSnapshot? Latest
    {
        get
        {
            PolymarketPnLSnapshot? last = null;
            foreach (var s in history)
                last = s;
            return last;
        }
    }

    // IMarketPnLHistory — явная реализация
    IMarketPnLSnapshot? IMarketPnLHistory.Latest => Latest;
    IMarketPnLSnapshot IMarketPnLHistory.TakeSnapshot() => TakeSnapshot();

    /// <summary>
    /// Получает все снимки в виде массива (для построения графика).
    /// </summary>
    public PolymarketPnLSnapshot[] ToArray() => [.. history];

    /// <summary>
    /// Записывает один снимок P&amp;L прямо сейчас.
    /// </summary>
    public PolymarketPnLSnapshot TakeSnapshot()
    {
        var summary = tracker.GetSummary();
        var snapshot = new PolymarketPnLSnapshot
        {
            TimestampTicks = Environment.TickCount64,
            Timestamp = DateTimeOffset.UtcNow,
            TotalMarketValue = summary.TotalMarketValue,
            TotalCostBasis = summary.TotalCostBasis,
            UnrealizedPnL = summary.TotalUnrealizedPnL,
            RealizedPnL = summary.TotalRealizedPnL,
            TotalFees = summary.TotalFees,
            OpenPositions = summary.OpenPositions
        };

        Enqueue(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Запускает автоматическую периодическую запись снимков.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (snapshotLoopTask is not null && !snapshotLoopTask.IsCompleted)
            return;

        snapshotCts = new CancellationTokenSource();
        snapshotLoopTask = Task.Run(() => SnapshotLoopAsync(snapshotCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Останавливает автоматическую запись.
    /// </summary>
    public async ValueTask StopAsync()
    {
        if (snapshotCts is null)
            return;

        await snapshotCts.CancelAsync().ConfigureAwait(false);

        if (snapshotLoopTask is not null)
        {
            try { await snapshotLoopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        snapshotCts.Dispose();
        snapshotCts = null;
        snapshotLoopTask = null;
    }

    /// <summary>
    /// Запущена ли автоматическая запись.
    /// </summary>
    public bool IsRecording => snapshotLoopTask is not null && !snapshotLoopTask.IsCompleted;

    /// <summary>
    /// Очищает историю снимков.
    /// </summary>
    public void Clear()
    {
        while (history.TryDequeue(out _)) { }
    }

    #region Внутренняя логика

    private async Task SnapshotLoopAsync(CancellationToken cancellationToken)
    {
        // Первый снимок сразу
        TakeSnapshot();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(snapshotInterval, cancellationToken).ConfigureAwait(false);
                TakeSnapshot();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void Enqueue(PolymarketPnLSnapshot snapshot)
    {
        history.Enqueue(snapshot);

        // Удаление старых снимков при превышении лимита
        while (history.Count > maxSnapshots)
            history.TryDequeue(out _);

        SnapshotRecorded?.Invoke(this, new PolymarketPnLSnapshotEventArgs(snapshot));
    }

    #endregion

    /// <summary>
    /// Освобождает ресурсы асинхронно.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Освобождает ресурсы синхронно.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        snapshotCts?.Cancel();
        snapshotCts?.Dispose();
    }
}
