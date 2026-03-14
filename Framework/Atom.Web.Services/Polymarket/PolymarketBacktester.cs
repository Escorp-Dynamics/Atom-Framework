using System.Collections.Concurrent;
using System.Globalization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Результат бэктеста стратегии — итоговые метрики.
/// </summary>
public sealed class PolymarketBacktestResult
    : IMarketBacktestResult
{
    /// <summary>Имя стратегии.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Начальный баланс.</summary>
    public double InitialBalance { get; init; }

    /// <summary>Итоговый баланс.</summary>
    public double FinalBalance { get; init; }

    /// <summary>Чистая прибыль/убыток.</summary>
    public double NetPnL => FinalBalance - InitialBalance;

    /// <summary>Доходность в процентах.</summary>
    public double ReturnPercent => InitialBalance != 0 ? NetPnL / InitialBalance * 100 : 0;

    /// <summary>Всего сделок.</summary>
    public int TotalTrades { get; init; }

    /// <summary>Прибыльных сделок.</summary>
    public int WinningTrades { get; init; }

    /// <summary>Убыточных сделок.</summary>
    public int LosingTrades { get; init; }

    /// <summary>Win rate (%).</summary>
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;

    /// <summary>
    /// Коэффициент Шарпа (annualized, risk-free = 0).
    /// </summary>
    public double SharpeRatio { get; init; }

    /// <summary>
    /// Максимальная просадка в процентах (от пика до дна).
    /// </summary>
    public double MaxDrawdownPercent { get; init; }

    /// <summary>
    /// Профит-фактор (сумма прибылей / сумма убытков).
    /// </summary>
    public double ProfitFactor { get; init; }

    /// <summary>
    /// Средняя прибыль на сделку.
    /// </summary>
    public double AveragePnLPerTrade => TotalTrades > 0 ? NetPnL / TotalTrades : 0;

    /// <summary>
    /// Кривая баланса (equity curve) — массив значений баланса после каждой сделки.
    /// </summary>
    public required double[] EquityCurve { get; init; }

    /// <summary>
    /// Все сделки бэктеста.
    /// </summary>
    public required PolymarketBacktestTrade[] Trades { get; init; }
}

/// <summary>
/// Отдельная сделка бэктеста.
/// </summary>
public sealed class PolymarketBacktestTrade
{
    /// <summary>Идентификатор актива.</summary>
    public required string AssetId { get; init; }

    /// <summary>Действие.</summary>
    public PolymarketTradeAction Action { get; init; }

    /// <summary>Объём.</summary>
    public double Quantity { get; init; }

    /// <summary>Цена входа.</summary>
    public double EntryPrice { get; init; }

    /// <summary>Цена выхода (0, если позиция не закрыта).</summary>
    public double ExitPrice { get; init; }

    /// <summary>Прибыль/убыток сделки.</summary>
    public double PnL { get; init; }

    /// <summary>Индекс ценовой точки входа.</summary>
    public int EntryIndex { get; init; }

    /// <summary>Индекс ценовой точки выхода (-1, если не закрыта).</summary>
    public int ExitIndex { get; init; }
}

/// <summary>
/// Ценовая точка для бэктеста (исторические данные).
/// </summary>
public sealed class PolymarketPricePoint
    : IMarketPricePoint
{
    /// <summary>Цена midpoint.</summary>
    public required double Midpoint { get; init; }

    /// <summary>Best bid.</summary>
    public double? BestBid { get; init; }

    /// <summary>Best ask.</summary>
    public double? BestAsk { get; init; }

    /// <summary>Временная метка.</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Движок бэктестирования стратегий Polymarket.
/// Прогоняет стратегию по историческим ценовым данным и рассчитывает метрики.
/// </summary>
/// <remarks>
/// Совместим с NativeAOT. Потокобезопасен — можно запускать несколько бэктестов параллельно.
/// Поддерживает любую стратегию, реализующую <see cref="IPolymarketStrategy"/>.
/// </remarks>
public sealed class PolymarketBacktester : IMarketBacktester
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Комиссия на сделку в BPS (по умолчанию 0 = без комиссий).
    /// </summary>
    public int FeeRateBps { get; set; }

    /// <summary>
    /// Начальный баланс (по умолчанию 10 000 USDC).
    /// </summary>
    public double InitialBalance { get; set; } = 10_000;

    /// <summary>
    /// Запускает бэктест стратегии на исторических данных для одного актива.
    /// </summary>
    /// <param name="strategy">Стратегия для тестирования.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="priceData">Массив ценовых точек (хронологический порядок).</param>
    /// <returns>Результат бэктеста с метриками.</returns>
    public PolymarketBacktestResult Run(
        IPolymarketStrategy strategy,
        string assetId,
        PolymarketPricePoint[] priceData)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        ArgumentNullException.ThrowIfNull(priceData);

        if (priceData.Length == 0)
        {
            return new PolymarketBacktestResult
            {
                StrategyName = strategy.Name,
                InitialBalance = InitialBalance,
                FinalBalance = InitialBalance,
                EquityCurve = [InitialBalance],
                Trades = []
            };
        }

        // Создаём виртуальный PriceStream для стратегии
        using var virtualStream = new PolymarketPriceStream();

        var balance = InitialBalance;
        var position = 0.0;    // количество токенов
        var positionCost = 0.0; // стоимость входа
        var trades = new List<PolymarketBacktestTrade>();
        var equityCurve = new List<double> { balance };
        var returns = new List<double>();

        int? entryIndex = null;
        double entryPrice = 0;
        PolymarketTradeAction? currentSide = null;

        for (int i = 0; i < priceData.Length; i++)
        {
            var point = priceData[i];

            // Обновляем виртуальный PriceStream
            var snapshot = new PolymarketPriceSnapshot
            {
                AssetId = assetId,
                Midpoint = point.Midpoint.ToString("G", Inv),
                BestBid = point.BestBid?.ToString("G", Inv),
                BestAsk = point.BestAsk?.ToString("G", Inv)
            };

            // Подаём данные стратегии
            strategy.OnPriceUpdated(snapshot);

            // Получаем сигнал
            var signal = strategy.Evaluate(virtualStream, assetId);

            if (signal.Action == PolymarketTradeAction.Hold)
            {
                // Обновляем equity curve по текущей позиции
                var currentEquity = balance + position * point.Midpoint;
                equityCurve.Add(currentEquity);
                continue;
            }

            var executionPrice = signal.Action == PolymarketTradeAction.Buy
                ? (point.BestAsk ?? point.Midpoint)
                : (point.BestBid ?? point.Midpoint);

            var fee = signal.Quantity * executionPrice * FeeRateBps / 10_000.0;

            if (signal.Action == PolymarketTradeAction.Buy && currentSide != PolymarketTradeAction.Buy)
            {
                // Закрываем short, если есть
                if (position < 0 && currentSide == PolymarketTradeAction.Sell)
                    CloseShort(assetId, executionPrice, fee, i, ref balance, ref position, ref positionCost, entryPrice, entryIndex, trades, returns);

                // Открываем long
                OpenLong(executionPrice, fee, signal.Quantity, i, ref balance, ref position, ref positionCost, ref entryPrice, ref entryIndex, ref currentSide);
            }
            else if (signal.Action == PolymarketTradeAction.Sell && position > 0 && currentSide == PolymarketTradeAction.Buy)
            {
                CloseLong(assetId, executionPrice, fee, i, ref balance, ref position, ref positionCost, entryPrice, entryIndex, ref currentSide, trades, returns);
            }

            var equity = balance + position * point.Midpoint;
            equityCurve.Add(equity);
        }

        // Принудительно закрываем открытую позицию по последней цене
        if (position > 0 && priceData.Length > 0)
        {
            var lastPrice = priceData[^1].Midpoint;
            var revenue = position * lastPrice;
            var pnl = revenue - positionCost;
            balance += revenue;
            trades.Add(new PolymarketBacktestTrade
            {
                AssetId = assetId,
                Action = PolymarketTradeAction.Buy,
                Quantity = position,
                EntryPrice = entryPrice,
                ExitPrice = lastPrice,
                PnL = pnl,
                EntryIndex = entryIndex ?? 0,
                ExitIndex = priceData.Length - 1
            });
            returns.Add(pnl);
        }

        var tradesArr = trades.ToArray();
        var winTrades = tradesArr.Count(t => t.PnL > 0);
        var loseTrades = tradesArr.Count(t => t.PnL < 0);

        return new PolymarketBacktestResult
        {
            StrategyName = strategy.Name,
            InitialBalance = InitialBalance,
            FinalBalance = balance,
            TotalTrades = tradesArr.Length,
            WinningTrades = winTrades,
            LosingTrades = loseTrades,
            SharpeRatio = CalculateSharpeRatio(returns),
            MaxDrawdownPercent = CalculateMaxDrawdown([.. equityCurve]),
            ProfitFactor = CalculateProfitFactor(tradesArr),
            EquityCurve = [.. equityCurve],
            Trades = tradesArr
        };
    }

    /// <summary>Закрывает short-позицию.</summary>
    private static void CloseShort(
        string assetId, double executionPrice, double fee, int index,
        ref double balance, ref double position, ref double positionCost,
        double entryPrice, int? entryIndex,
        List<PolymarketBacktestTrade> trades, List<double> returns)
    {
        var closePnL = -position * (entryPrice - executionPrice) - fee;
        balance += closePnL + positionCost;
        trades.Add(new PolymarketBacktestTrade
        {
            AssetId = assetId,
            Action = PolymarketTradeAction.Sell,
            Quantity = -position,
            EntryPrice = entryPrice,
            ExitPrice = executionPrice,
            PnL = closePnL,
            EntryIndex = entryIndex ?? index,
            ExitIndex = index
        });
        position = 0;
        positionCost = 0;
        returns.Add(closePnL);
    }

    /// <summary>Открывает long-позицию.</summary>
    private static void OpenLong(
        double executionPrice, double fee, double signalQty, int index,
        ref double balance, ref double position, ref double positionCost,
        ref double entryPrice, ref int? entryIndex, ref PolymarketTradeAction? currentSide)
    {
        var qty = Math.Min(signalQty, balance / executionPrice);
        if (qty <= 0) return;

        var cost = qty * executionPrice + fee;
        balance -= cost;
        position = qty;
        positionCost = cost;
        entryPrice = executionPrice;
        entryIndex = index;
        currentSide = PolymarketTradeAction.Buy;
    }

    /// <summary>Закрывает long-позицию.</summary>
    private static void CloseLong(
        string assetId, double executionPrice, double fee, int index,
        ref double balance, ref double position, ref double positionCost,
        double entryPrice, int? entryIndex, ref PolymarketTradeAction? currentSide,
        List<PolymarketBacktestTrade> trades, List<double> returns)
    {
        var revenue = position * executionPrice - fee;
        var pnl = revenue - positionCost;
        balance += revenue;
        trades.Add(new PolymarketBacktestTrade
        {
            AssetId = assetId,
            Action = PolymarketTradeAction.Buy,
            Quantity = position,
            EntryPrice = entryPrice,
            ExitPrice = executionPrice,
            PnL = pnl,
            EntryIndex = entryIndex ?? index,
            ExitIndex = index
        });
        returns.Add(pnl);
        position = 0;
        positionCost = 0;
        entryIndex = null;
        currentSide = null;
    }

    /// <summary>
    /// Рассчитывает коэффициент Шарпа (assuming risk-free = 0, annualized для ≈252 торговых дней).
    /// </summary>
    private static double CalculateSharpeRatio(List<double> returns)
    {
        if (returns.Count < 2) return 0;

        var avg = returns.Average();
        var sumSq = returns.Sum(r => (r - avg) * (r - avg));
        var stdDev = Math.Sqrt(sumSq / returns.Count);

        return stdDev == 0 ? 0 : avg / stdDev * Math.Sqrt(252);
    }

    /// <summary>
    /// Рассчитывает максимальную просадку (от пика до дна) в процентах.
    /// </summary>
    private static double CalculateMaxDrawdown(double[] equityCurve)
    {
        if (equityCurve.Length < 2) return 0;

        var peak = equityCurve[0];
        var maxDrawdown = 0.0;

        for (int i = 1; i < equityCurve.Length; i++)
        {
            if (equityCurve[i] > peak)
                peak = equityCurve[i];

            var drawdown = peak > 0 ? (peak - equityCurve[i]) / peak * 100 : 0;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }

    /// <summary>
    /// Рассчитывает профит-фактор (gross profit / gross loss).
    /// </summary>
    private static double CalculateProfitFactor(PolymarketBacktestTrade[] trades)
    {
        var grossProfit = trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
        var grossLoss = Math.Abs(trades.Where(t => t.PnL < 0).Sum(t => t.PnL));
        return grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? double.PositiveInfinity : 0;
    }

    // IMarketBacktester — явная реализация
    IMarketBacktestResult IMarketBacktester.Run(IMarketStrategy strategy, string assetId, IMarketPricePoint[] priceData) =>
        Run((IPolymarketStrategy)strategy, assetId, priceData.Cast<PolymarketPricePoint>().ToArray());
}
