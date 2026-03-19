using System.Collections.Concurrent;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Трекер портфеля: позиции, P&L, сводка. Потокобезопасный.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация трекера портфеля.
/// </summary>
public sealed class MarketPortfolioTracker : IMarketPortfolioTracker
{
    private readonly ConcurrentDictionary<string, MarketPosition> positions = new();
    private readonly IMarketPriceStream priceStream;
    private bool isDisposed;

    /// <summary>
    /// Создаёт трекер с привязкой к стриму цен для обновления текущих цен.
    /// </summary>
    public MarketPortfolioTracker(IMarketPriceStream priceStream)
    {
        this.priceStream = priceStream;
    }

    /// <inheritdoc />
    public int OpenPositionCount => positions.Count(kv => !kv.Value.IsClosed);

    /// <inheritdoc />
    public IMarketPosition? GetPosition(string assetId)
    {
        if (!positions.TryGetValue(assetId, out var pos))
            return null;

        RefreshCurrentPrice(assetId, pos);
        return pos;
    }

    /// <inheritdoc />
    public IMarketPortfolioSummary GetSummary()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var openCount = 0;
        var closedCount = 0;
        var totalMarketValue = 0.0;
        var totalCostBasis = 0.0;
        var totalUnrealizedPnl = 0.0;
        var totalRealizedPnl = 0.0;
        var totalFees = 0.0;

        foreach (var (assetId, pos) in positions)
        {
            RefreshCurrentPrice(assetId, pos);

            if (pos.IsClosed) closedCount++;
            else openCount++;

            totalMarketValue += pos.MarketValue;
            totalCostBasis += pos.Quantity * pos.AverageCostBasis;
            totalUnrealizedPnl += pos.UnrealizedPnL;
            totalRealizedPnl += pos.RealizedPnL;
            totalFees += pos.TotalFees;
        }

        return new PortfolioSummary
        {
            OpenPositions = openCount,
            ClosedPositions = closedCount,
            TotalMarketValue = totalMarketValue,
            TotalCostBasis = totalCostBasis,
            TotalUnrealizedPnL = totalUnrealizedPnl,
            TotalRealizedPnL = totalRealizedPnl,
            TotalFees = totalFees,
            NetPnL = totalUnrealizedPnl + totalRealizedPnl - totalFees
        };
    }

    /// <summary>
    /// Записывает сделку (Buy/Sell). Обновляет или создаёт позицию.
    /// </summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="side">Buy или Sell.</param>
    /// <param name="quantity">Количество.</param>
    /// <param name="price">Цена исполнения.</param>
    /// <param name="fee">Комиссия сделки.</param>
    public void RecordTrade(string assetId, TradeSide side, double quantity, double price, double fee = 0)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        positions.AddOrUpdate(assetId,
            _ => CreatePositionFromTrade(assetId, side, quantity, price, fee),
            (_, existing) => UpdatePosition(existing, side, quantity, price, fee));
    }

    /// <inheritdoc />
    public void ClearPositions()
    {
        positions.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        positions.Clear();
    }

    private void RefreshCurrentPrice(string assetId, MarketPosition pos)
    {
        var snapshot = priceStream.GetPrice(assetId);
        if (snapshot?.LastTradePrice is { } price)
            pos.CurrentPrice = price;
    }

    private static MarketPosition CreatePositionFromTrade(string assetId, TradeSide side, double quantity, double price, double fee)
    {
        var pos = new MarketPosition { AssetId = assetId };
        if (side == TradeSide.Buy)
        {
            pos.Quantity = quantity;
            pos.AverageCostBasis = price;
            pos.CurrentPrice = price;
        }
        else
        {
            pos.Quantity = -quantity; // short
            pos.AverageCostBasis = price;
            pos.CurrentPrice = price;
        }
        pos.TotalFees = fee;
        pos.TradeCount = 1;
        return pos;
    }

    private static MarketPosition UpdatePosition(MarketPosition pos, TradeSide side, double quantity, double price, double fee)
    {
        if (side == TradeSide.Buy)
        {
            if (pos.Quantity >= 0)
            {
                // Усреднение long
                var totalCost = pos.Quantity * pos.AverageCostBasis + quantity * price;
                pos.Quantity += quantity;
                pos.AverageCostBasis = pos.Quantity > 0 ? totalCost / pos.Quantity : 0;
            }
            else
            {
                // Закрытие short
                var closedQty = Math.Min(quantity, Math.Abs(pos.Quantity));
                var pnl = (pos.AverageCostBasis - price) * closedQty;
                pos.RealizedPnL += pnl;
                pos.Quantity += quantity;
                if (pos.Quantity > 0) pos.AverageCostBasis = price; // переворот в long
            }
        }
        else // Sell
        {
            if (pos.Quantity > 0)
            {
                // Закрытие long
                var closedQty = Math.Min(quantity, pos.Quantity);
                var pnl = (price - pos.AverageCostBasis) * closedQty;
                pos.RealizedPnL += pnl;
                pos.Quantity -= quantity;
                if (pos.Quantity < 0) pos.AverageCostBasis = price; // переворот в short
            }
            else
            {
                // Усреднение short
                var totalCost = Math.Abs(pos.Quantity) * pos.AverageCostBasis + quantity * price;
                pos.Quantity -= quantity;
                pos.AverageCostBasis = Math.Abs(pos.Quantity) > 0 ? totalCost / Math.Abs(pos.Quantity) : 0;
            }
        }

        pos.CurrentPrice = price;
        pos.TotalFees += fee;
        pos.TradeCount++;
        return pos;
    }
}

/// <summary>Конкретная позиция.</summary>
public sealed class MarketPosition : IMarketPosition
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public double Quantity { get; set; }

    /// <inheritdoc />
    public double AverageCostBasis { get; set; }

    /// <inheritdoc />
    public double CurrentPrice { get; set; }

    /// <inheritdoc />
    public double MarketValue => Math.Abs(Quantity) * CurrentPrice;

    /// <inheritdoc />
    public double UnrealizedPnL => Quantity > 0
        ? (CurrentPrice - AverageCostBasis) * Quantity
        : Quantity < 0
            ? (AverageCostBasis - CurrentPrice) * Math.Abs(Quantity)
            : 0;

    /// <inheritdoc />
    public double UnrealizedPnLPercent => AverageCostBasis > 0 && Quantity != 0
        ? UnrealizedPnL / (Math.Abs(Quantity) * AverageCostBasis) * 100
        : 0;

    /// <inheritdoc />
    public double RealizedPnL { get; set; }

    /// <inheritdoc />
    public double TotalFees { get; set; }

    /// <inheritdoc />
    public int TradeCount { get; set; }

    /// <inheritdoc />
    public bool IsClosed => Quantity == 0 && TradeCount > 0;
}

/// <summary>Сводка портфеля.</summary>
internal sealed class PortfolioSummary : IMarketPortfolioSummary
{
    public required int OpenPositions { get; init; }
    public required int ClosedPositions { get; init; }
    public required double TotalMarketValue { get; init; }
    public required double TotalCostBasis { get; init; }
    public required double TotalUnrealizedPnL { get; init; }
    public required double TotalRealizedPnL { get; init; }
    public required double TotalFees { get; init; }
    public required double NetPnL { get; init; }
}

// ═══════════════════════════════════════════════════════════════════
// История P&L — периодические снимки.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация истории P&amp;L.
/// </summary>
public sealed class MarketPnLHistory : IMarketPnLHistory
{
    private readonly IMarketPortfolioTracker tracker;
    private readonly List<PnLSnapshot> snapshots = [];
    private readonly Lock syncLock = new();
    private Timer? timer;
    private bool isDisposed;

    /// <summary>Интервал записи.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Создаёт историю P&amp;L привязанную к трекеру.
    /// </summary>
    public MarketPnLHistory(IMarketPortfolioTracker tracker)
    {
        this.tracker = tracker;
    }

    /// <inheritdoc />
    public int Count
    {
        get { lock (syncLock) return snapshots.Count; }
    }

    /// <inheritdoc />
    public IMarketPnLSnapshot? Latest
    {
        get { lock (syncLock) return snapshots.Count > 0 ? snapshots[^1] : null; }
    }

    /// <inheritdoc />
    public bool IsRecording => timer is not null;

    /// <inheritdoc />
    public IMarketPnLSnapshot TakeSnapshot()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var summary = tracker.GetSummary();
        var snap = new PnLSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            TotalMarketValue = summary.TotalMarketValue,
            UnrealizedPnL = summary.TotalUnrealizedPnL,
            RealizedPnL = summary.TotalRealizedPnL,
            TotalFees = summary.TotalFees,
            NetPnL = summary.NetPnL
        };

        lock (syncLock)
            snapshots.Add(snap);

        return snap;
    }

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        timer ??= new Timer(_ => TakeSnapshot(), null, TimeSpan.Zero, Interval);
    }

    /// <inheritdoc />
    public ValueTask StopAsync()
    {
        if (timer is not null)
        {
            var t = timer;
            timer = null;
            return new ValueTask(t.DisposeAsync().AsTask());
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (syncLock)
            snapshots.Clear();
    }

    /// <summary>
    /// Получает все снимки (копия).
    /// </summary>
    public IMarketPnLSnapshot[] GetSnapshots()
    {
        lock (syncLock)
            return [.. snapshots];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        timer?.Dispose();
        timer = null;
    }
}

/// <summary>Снимок P&amp;L.</summary>
internal sealed class PnLSnapshot : IMarketPnLSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public required double TotalMarketValue { get; init; }
    public required double UnrealizedPnL { get; init; }
    public required double RealizedPnL { get; init; }
    public required double TotalFees { get; init; }
    public required double NetPnL { get; init; }
}
