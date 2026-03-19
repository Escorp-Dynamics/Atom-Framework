using System.Collections.Concurrent;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Бэктестер стратегий: прогоняет IMarketStrategy по историческим данным
// и вычисляет PnL-метрики (Sharpe, MaxDrawdown, WinRate и т.д.).
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация бэктестера стратегий.
/// </summary>
/// <remarks>
/// Симулирует торговлю: проходит по ценовым точкам, вызывает Evaluate для каждой,
/// исполняет Buy/Sell с учётом комиссии, собирает equity curve и метрики.
/// </remarks>
public sealed class MarketBacktester : IMarketBacktester
{
    /// <inheritdoc />
    public double InitialBalance { get; set; } = 10_000;

    /// <inheritdoc />
    public int FeeRateBps { get; set; } = 10; // 0.1%

    /// <inheritdoc />
    public IMarketBacktestResult Run(IMarketStrategy strategy, string assetId, IMarketPricePoint[] priceData)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priceData.Length);

        var balance = InitialBalance;
        var position = 0.0;       // количество актива
        var entryPrice = 0.0;     // средняя цена входа
        var totalTrades = 0;
        var wins = 0;
        var grossProfit = 0.0;
        var grossLoss = 0.0;
        var peakBalance = balance;
        var maxDrawdown = 0.0;
        var equityCurve = new double[priceData.Length];
        var returns = new List<double>();
        var feeRate = FeeRateBps / 10_000.0;

        using var priceStream = new BacktestPriceStream();

        for (var i = 0; i < priceData.Length; i++)
        {
            var point = priceData[i];
            var price = point.Midpoint;

            // Обновляем кеш цены
            var snapshot = new BacktestSnapshot
            {
                AssetId = assetId,
                BestBid = point.BestBid ?? price,
                BestAsk = point.BestAsk ?? price,
                LastTradePrice = price,
                LastUpdateTicks = point.Timestamp.Ticks
            };
            priceStream.SetPrice(assetId, snapshot);
            strategy.OnPriceUpdated(snapshot);

            // Оцениваем стратегию
            var signal = strategy.Evaluate(priceStream, assetId);

            if (signal.Action == TradeAction.Buy && position <= 0)
            {
                // Вход в позицию (или закрытие short + открытие long)
                if (position < 0)
                {
                    // Закрываем short
                    var pnl = (entryPrice - price) * Math.Abs(position);
                    var fee = price * Math.Abs(position) * feeRate;
                    pnl -= fee;
                    balance += pnl;

                    if (pnl > 0) { wins++; grossProfit += pnl; }
                    else grossLoss += Math.Abs(pnl);
                    totalTrades++;
                }

                // Открываем long
                var qty = signal.Quantity > 0 ? signal.Quantity : 1.0;
                var cost = price * qty;
                var openFee = cost * feeRate;

                if (cost + openFee <= balance)
                {
                    position = qty;
                    entryPrice = price;
                    balance -= cost + openFee;
                }
            }
            else if (signal.Action == TradeAction.Sell && position >= 0)
            {
                // Закрытие long (или открытие short)
                if (position > 0)
                {
                    var proceeds = price * position;
                    var fee = proceeds * feeRate;
                    var pnl = proceeds - fee - entryPrice * position;
                    balance += proceeds - fee;
                    position = 0;

                    if (pnl > 0) { wins++; grossProfit += pnl; }
                    else grossLoss += Math.Abs(pnl);
                    totalTrades++;
                }
            }

            // Equity = balance + unrealized PnL
            var equity = balance;
            if (position > 0)
                equity += price * position;
            else if (position < 0)
                equity += (entryPrice - price) * Math.Abs(position);
            equityCurve[i] = equity;

            // Drawdown
            if (equity > peakBalance) peakBalance = equity;
            var drawdown = peakBalance > 0 ? (peakBalance - equity) / peakBalance : 0;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;

            // Периодическая доходность
            if (i > 0 && equityCurve[i - 1] > 0)
                returns.Add((equity - equityCurve[i - 1]) / equityCurve[i - 1]);
        }

        // Закрываем оставшуюся позицию по последней цене
        if (position != 0 && priceData.Length > 0)
        {
            var lastPrice = priceData[^1].Midpoint;
            if (position > 0)
            {
                var proceeds = lastPrice * position;
                var fee = proceeds * feeRate;
                var pnl = proceeds - fee - entryPrice * position;
                balance += proceeds - fee;

                if (pnl > 0) { wins++; grossProfit += pnl; }
                else grossLoss += Math.Abs(pnl);
                totalTrades++;
            }
            else if (position < 0)
            {
                var pnl = (entryPrice - lastPrice) * Math.Abs(position);
                var fee = lastPrice * Math.Abs(position) * feeRate;
                pnl -= fee;
                balance += pnl;

                if (pnl > 0) { wins++; grossProfit += pnl; }
                else grossLoss += Math.Abs(pnl);
                totalTrades++;
            }
        }

        // Sharpe Ratio = avg(returns) / stddev(returns) * sqrt(252)
        var sharpe = 0.0;
        if (returns.Count > 1)
        {
            var avgReturn = returns.Average();
            var stdDev = Math.Sqrt(returns.Sum(r => (r - avgReturn) * (r - avgReturn)) / returns.Count);
            if (stdDev > 0)
                sharpe = avgReturn / stdDev * Math.Sqrt(252);
        }

        var netPnl = balance - InitialBalance;

        return new BacktestResult
        {
            StrategyName = strategy.Name,
            InitialBalance = InitialBalance,
            FinalBalance = balance,
            NetPnL = netPnl,
            ReturnPercent = InitialBalance > 0 ? netPnl / InitialBalance * 100 : 0,
            TotalTrades = totalTrades,
            WinRate = totalTrades > 0 ? (double)wins / totalTrades * 100 : 0,
            SharpeRatio = sharpe,
            MaxDrawdownPercent = maxDrawdown * 100,
            ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? double.PositiveInfinity : 0,
            EquityCurve = equityCurve
        };
    }

    #region Внутренние типы

    /// <summary>Результат бэктеста.</summary>
    private sealed class BacktestResult : IMarketBacktestResult
    {
        public required string StrategyName { get; init; }
        public required double InitialBalance { get; init; }
        public required double FinalBalance { get; init; }
        public required double NetPnL { get; init; }
        public required double ReturnPercent { get; init; }
        public required int TotalTrades { get; init; }
        public required double WinRate { get; init; }
        public required double SharpeRatio { get; init; }
        public required double MaxDrawdownPercent { get; init; }
        public required double ProfitFactor { get; init; }
        public required double[] EquityCurve { get; init; }
    }

    /// <summary>Ценовая точка для бэктеста.</summary>
    public sealed class PricePoint : IMarketPricePoint
    {
        /// <inheritdoc />
        public required double Midpoint { get; init; }

        /// <inheritdoc />
        public double? BestBid { get; init; }

        /// <inheritdoc />
        public double? BestAsk { get; init; }

        /// <inheritdoc />
        public required DateTimeOffset Timestamp { get; init; }
    }

    private sealed class BacktestPriceStream : IWritableMarketPriceStream
    {
        private readonly ConcurrentDictionary<string, IMarketPriceSnapshot> cache = new();

        public int TokenCount => cache.Count;
        public IMarketPriceSnapshot? GetPrice(string assetId) =>
            cache.TryGetValue(assetId, out var snap) ? snap : null;
        public void SetPrice(string assetId, IMarketPriceSnapshot snapshot) => cache[assetId] = snapshot;
        public void ClearCache() => cache.Clear();
        public void Dispose() => cache.Clear();
    }

    private sealed class BacktestSnapshot : IMarketPriceSnapshot
    {
        public required string AssetId { get; init; }
        public double? BestBid { get; set; }
        public double? BestAsk { get; set; }
        public double? Midpoint => (BestBid + BestAsk) / 2.0;
        public double? LastTradePrice { get; set; }
        public long LastUpdateTicks { get; set; }
    }

    #endregion
}
