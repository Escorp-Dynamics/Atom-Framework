using System.Collections.Concurrent;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Действие торгового сигнала.
/// </summary>
public enum PolymarketTradeAction : byte
{
    /// <summary>Удерживать / нет сигнала.</summary>
    Hold,

    /// <summary>Покупка.</summary>
    Buy,

    /// <summary>Продажа.</summary>
    Sell
}

/// <summary>
/// Торговый сигнал, генерируемый стратегией.
/// </summary>
public sealed class PolymarketTradeSignal
    : IMarketTradeSignal
{
    /// <summary>
    /// Идентификатор актива (tokenId).
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// Действие (Buy / Sell / Hold).
    /// </summary>
    public PolymarketTradeAction Action { get; init; }

    /// <summary>
    /// Рекомендуемый объём.
    /// </summary>
    public double Quantity { get; init; }

    /// <summary>
    /// Рекомендуемая цена (лимитная) или null для рыночной.
    /// </summary>
    public string? Price { get; init; }

    /// <summary>
    /// Уверенность сигнала (0.0 — минимум, 1.0 — максимум).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Описание причины сигнала.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Время генерации сигнала.
    /// </summary>
    public long TimestampTicks { get; init; } = Environment.TickCount64;

    // Явная реализация IMarketTradeSignal.Action (конверсия енумов)
    TradeAction IMarketTradeSignal.Action => (TradeAction)Action;
}

/// <summary>
/// Интерфейс торговой стратегии Polymarket.
/// Стратегия анализирует данные PriceStream и генерирует торговые сигналы.
/// </summary>
public interface IPolymarketStrategy : IMarketStrategy
{
    /// <summary>
    /// Уникальное имя стратегии.
    /// </summary>
    new string Name { get; }

    /// <summary>
    /// Оценивает текущее состояние для указанного актива и генерирует сигнал.
    /// </summary>
    /// <param name="priceStream">Стрим цен для получения данных.</param>
    /// <param name="assetId">Идентификатор токена.</param>
    /// <returns>Торговый сигнал.</returns>
    PolymarketTradeSignal Evaluate(PolymarketPriceStream priceStream, string assetId);

    /// <summary>
    /// Обновляет внутреннее состояние стратегии при появлении нового обновления цены.
    /// Вызывается автоматически при подключении к <see cref="PolymarketPriceStream.PriceUpdated"/>.
    /// </summary>
    /// <param name="snapshot">Актуальный снимок цены.</param>
    void OnPriceUpdated(PolymarketPriceSnapshot snapshot);
}

/// <summary>
/// Базовый класс стратегии с общей инфраструктурой для хранения истории цен.
/// </summary>
public abstract class PolymarketStrategyBase : IPolymarketStrategy
{
    /// <summary>
    /// Размер окна наблюдения (количество ценовых точек).
    /// </summary>
    protected readonly int LookbackPeriod;

    /// <summary>
    /// Размер позиции по умолчанию.
    /// </summary>
    protected readonly double PositionSize;

    /// <summary>
    /// История цен по каждому активу.
    /// Ключ — assetId, значение — кольцевой буфер цен (midpoint).
    /// </summary>
    protected readonly ConcurrentDictionary<string, PriceHistory> PriceHistories = new();

    private bool isDisposed;

    /// <summary>
    /// Инициализирует стратегию.
    /// </summary>
    /// <param name="name">Имя стратегии.</param>
    /// <param name="lookbackPeriod">Размер окна наблюдения.</param>
    /// <param name="positionSize">Размер позиции по умолчанию.</param>
    protected PolymarketStrategyBase(string name, int lookbackPeriod, double positionSize)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lookbackPeriod);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(positionSize);

        Name = name;
        LookbackPeriod = lookbackPeriod;
        PositionSize = positionSize;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public abstract PolymarketTradeSignal Evaluate(PolymarketPriceStream priceStream, string assetId);

    /// <inheritdoc />
    public virtual void OnPriceUpdated(PolymarketPriceSnapshot snapshot)
    {
        if (snapshot.Midpoint is null) return;

        if (!double.TryParse(snapshot.Midpoint, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var midpoint))
            return;

        var history = PriceHistories.GetOrAdd(snapshot.AssetId, _ => new PriceHistory(LookbackPeriod));
        history.Add(midpoint);
    }

    /// <summary>
    /// Создаёт сигнал Hold для актива.
    /// </summary>
    protected PolymarketTradeSignal HoldSignal(string assetId, string reason) => new()
    {
        AssetId = assetId,
        Action = PolymarketTradeAction.Hold,
        Quantity = 0,
        Reason = reason
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        PriceHistories.Clear();
        GC.SuppressFinalize(this);
    }

    // IMarketStrategy — явная реализация для универсального контракта
    IMarketTradeSignal IMarketStrategy.Evaluate(IMarketPriceStream priceStream, string assetId)
    {
        if (priceStream is PolymarketPriceStream polyStream)
            return Evaluate(polyStream, assetId);
        throw new ArgumentException($"Expected {nameof(PolymarketPriceStream)}", nameof(priceStream));
    }

    void IMarketStrategy.OnPriceUpdated(IMarketPriceSnapshot snapshot)
    {
        if (snapshot is PolymarketPriceSnapshot polySnapshot)
            OnPriceUpdated(polySnapshot);
    }

    /// <summary>
    /// Кольцевой буфер цен для одного актива.
    /// </summary>
    protected sealed class PriceHistory
    {
        private readonly double[] buffer;
        private int writeIndex;
        private int count;
        private readonly object syncRoot = new();

        /// <summary>
        /// Количество записей в буфере.
        /// </summary>
        public int Count { get { lock (syncRoot) return count; } }

        /// <summary>
        /// Создаёт буфер указанной ёмкости.
        /// </summary>
        public PriceHistory(int capacity) => buffer = new double[capacity];

        /// <summary>
        /// Добавляет цену в буфер.
        /// </summary>
        public void Add(double price)
        {
            lock (syncRoot)
            {
                buffer[writeIndex] = price;
                writeIndex = (writeIndex + 1) % buffer.Length;
                if (count < buffer.Length) count++;
            }
        }

        /// <summary>
        /// Возвращает все цены в хронологическом порядке.
        /// </summary>
        public double[] ToArray()
        {
            lock (syncRoot)
            {
                var result = new double[count];
                if (count < buffer.Length)
                {
                    Array.Copy(buffer, 0, result, 0, count);
                }
                else
                {
                    var start = writeIndex;
                    var firstLen = buffer.Length - start;
                    Array.Copy(buffer, start, result, 0, firstLen);
                    Array.Copy(buffer, 0, result, firstLen, start);
                }
                return result;
            }
        }

        /// <summary>
        /// Возвращает последнюю добавленную цену.
        /// </summary>
        public double? Last()
        {
            lock (syncRoot)
            {
                if (count == 0) return null;
                var idx = (writeIndex - 1 + buffer.Length) % buffer.Length;
                return buffer[idx];
            }
        }

        /// <summary>
        /// Средняя цена по всему буферу.
        /// </summary>
        public double Average()
        {
            lock (syncRoot)
            {
                if (count == 0) return 0;
                double sum = 0;
                var len = count < buffer.Length ? count : buffer.Length;
                for (int i = 0; i < len; i++)
                    sum += buffer[i];
                return sum / len;
            }
        }

        /// <summary>
        /// Стандартное отклонение цен.
        /// </summary>
        public double StandardDeviation()
        {
            lock (syncRoot)
            {
                if (count < 2) return 0;
                var avg = Average();
                double sumSq = 0;
                var len = count < buffer.Length ? count : buffer.Length;
                for (int i = 0; i < len; i++)
                {
                    var diff = buffer[i] - avg;
                    sumSq += diff * diff;
                }
                return Math.Sqrt(sumSq / len);
            }
        }
    }
}

/// <summary>
/// Стратегия Momentum (импульс): покупка при устойчивом росте цены, продажа при падении.
/// </summary>
/// <remarks>
/// Рассчитывает линейный наклон цены за lookback период.
/// Если наклон превышает порог — генерирует Buy, если ниже отрицательного порога — Sell.
/// </remarks>
public sealed class PolymarketMomentumStrategy : PolymarketStrategyBase
{
    private readonly double momentumThreshold;

    /// <summary>
    /// Инициализирует Momentum-стратегию.
    /// </summary>
    /// <param name="lookbackPeriod">Количество ценовых точек для анализа (по умолчанию 10).</param>
    /// <param name="momentumThreshold">Порог импульса (по умолчанию 0.05 = 5%).</param>
    /// <param name="positionSize">Размер позиции по умолчанию (в USDC).</param>
    public PolymarketMomentumStrategy(
        int lookbackPeriod = 10,
        double momentumThreshold = 0.05,
        double positionSize = 100.0)
        : base("Momentum", lookbackPeriod, positionSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(momentumThreshold);
        this.momentumThreshold = momentumThreshold;
    }

    /// <inheritdoc />
    public override PolymarketTradeSignal Evaluate(PolymarketPriceStream priceStream, string assetId)
    {
        ArgumentNullException.ThrowIfNull(priceStream);
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        if (!PriceHistories.TryGetValue(assetId, out var history) || history.Count < LookbackPeriod)
            return HoldSignal(assetId, "Недостаточно данных для анализа импульса");

        var prices = history.ToArray();
        var momentum = CalculateMomentum(prices);

        if (momentum > momentumThreshold)
        {
            var snap = priceStream.GetPrice(assetId);
            return new PolymarketTradeSignal
            {
                AssetId = assetId,
                Action = PolymarketTradeAction.Buy,
                Quantity = PositionSize,
                Price = snap?.BestAsk,
                Confidence = Math.Min(1.0, momentum / momentumThreshold * 0.5),
                Reason = $"Восходящий импульс: {momentum:P2} > порог {momentumThreshold:P2}"
            };
        }

        if (momentum < -momentumThreshold)
        {
            var snap = priceStream.GetPrice(assetId);
            return new PolymarketTradeSignal
            {
                AssetId = assetId,
                Action = PolymarketTradeAction.Sell,
                Quantity = PositionSize,
                Price = snap?.BestBid,
                Confidence = Math.Min(1.0, Math.Abs(momentum) / momentumThreshold * 0.5),
                Reason = $"Нисходящий импульс: {momentum:P2} < порог -{momentumThreshold:P2}"
            };
        }

        return HoldSignal(assetId, $"Импульс {momentum:P2} в пределах порога (±{momentumThreshold:P2})");
    }

    /// <summary>
    /// Рассчитывает нормализованный наклон методом наименьших квадратов.
    /// </summary>
    private static double CalculateMomentum(double[] prices)
    {
        int n = prices.Length;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += prices[i];
            sumXY += i * prices[i];
            sumXX += (double)i * i;
        }

        var denominator = n * sumXX - sumX * sumX;
        if (denominator == 0) return 0;

        var slope = (n * sumXY - sumX * sumY) / denominator;
        var avgPrice = sumY / n;
        return avgPrice == 0 ? 0 : slope / avgPrice;
    }
}

/// <summary>
/// Стратегия Mean Reversion (возврат к среднему): покупка при снижении ниже среднего, продажа — выше.
/// </summary>
/// <remarks>
/// Использует Z-score (отклонение от средней в стандартных отклонениях).
/// При Z-score ниже -порога — Buy, выше +порога — Sell.
/// </remarks>
public sealed class PolymarketMeanReversionStrategy : PolymarketStrategyBase
{
    private readonly double deviationThreshold;

    /// <summary>
    /// Инициализирует Mean Reversion-стратегию.
    /// </summary>
    /// <param name="lookbackPeriod">Количество ценовых точек для расчёта среднего (по умолчанию 20).</param>
    /// <param name="deviationThreshold">Порог Z-score для генерации сигнала (по умолчанию 2.0).</param>
    /// <param name="positionSize">Размер позиции по умолчанию (в USDC).</param>
    public PolymarketMeanReversionStrategy(
        int lookbackPeriod = 20,
        double deviationThreshold = 2.0,
        double positionSize = 50.0)
        : base("MeanReversion", lookbackPeriod, positionSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviationThreshold);
        this.deviationThreshold = deviationThreshold;
    }

    /// <inheritdoc />
    public override PolymarketTradeSignal Evaluate(PolymarketPriceStream priceStream, string assetId)
    {
        ArgumentNullException.ThrowIfNull(priceStream);
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        if (!PriceHistories.TryGetValue(assetId, out var history) || history.Count < LookbackPeriod)
            return HoldSignal(assetId, "Недостаточно данных для расчёта среднего");

        var avg = history.Average();
        var stdDev = history.StandardDeviation();

        if (stdDev == 0)
            return HoldSignal(assetId, "Нулевая волатильность — нет дисперсии цен");

        var currentPrice = history.Last() ?? 0;
        var zScore = (currentPrice - avg) / stdDev;

        if (zScore < -deviationThreshold)
        {
            var snap = priceStream.GetPrice(assetId);
            return new PolymarketTradeSignal
            {
                AssetId = assetId,
                Action = PolymarketTradeAction.Buy,
                Quantity = PositionSize,
                Price = snap?.BestAsk,
                Confidence = Math.Min(1.0, Math.Abs(zScore) / deviationThreshold * 0.5),
                Reason = $"Цена ниже среднего: Z={zScore:F2} < -{deviationThreshold:F1} (цена {currentPrice:F4}, среднее {avg:F4})"
            };
        }

        if (zScore > deviationThreshold)
        {
            var snap = priceStream.GetPrice(assetId);
            return new PolymarketTradeSignal
            {
                AssetId = assetId,
                Action = PolymarketTradeAction.Sell,
                Quantity = PositionSize,
                Price = snap?.BestBid,
                Confidence = Math.Min(1.0, zScore / deviationThreshold * 0.5),
                Reason = $"Цена выше среднего: Z={zScore:F2} > +{deviationThreshold:F1} (цена {currentPrice:F4}, среднее {avg:F4})"
            };
        }

        return HoldSignal(assetId, $"Z-score {zScore:F2} в пределах порога (±{deviationThreshold:F1})");
    }
}

/// <summary>
/// Стратегия арбитража: ищет расхождения между комплементарными токенами одного рынка.
/// </summary>
/// <remarks>
/// На бинарных рынках Polymarket (Yes/No) сумма цен должна быть ≈1.0.
/// При отклонении более порога — генерирует арбитражный сигнал (покупка дешёвого токена).
/// </remarks>
public sealed class PolymarketArbitrageStrategy : PolymarketStrategyBase
{
    private readonly double spreadThreshold;

    /// <summary>
    /// Реестр комплементарных пар (tokenA ↔ tokenB).
    /// </summary>
    private readonly ConcurrentDictionary<string, string> complementaryPairs = new();

    /// <summary>
    /// Инициализирует Arbitrage-стратегию.
    /// </summary>
    /// <param name="spreadThreshold">Минимальное расхождение для генерации сигнала (по умолчанию 0.02 = 2%).</param>
    /// <param name="positionSize">Размер позиции по умолчанию (в USDC).</param>
    public PolymarketArbitrageStrategy(
        double spreadThreshold = 0.02,
        double positionSize = 200.0)
        : base("Arbitrage", lookbackPeriod: 1, positionSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spreadThreshold);
        this.spreadThreshold = spreadThreshold;
    }

    /// <summary>
    /// Регистрирует комплементарную пару токенов (Yes ↔ No на одном рынке).
    /// </summary>
    /// <param name="tokenA">Идентификатор первого токена (например, Yes).</param>
    /// <param name="tokenB">Идентификатор второго токена (например, No).</param>
    public void RegisterPair(string tokenA, string tokenB)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenA);
        ArgumentException.ThrowIfNullOrEmpty(tokenB);
        complementaryPairs[tokenA] = tokenB;
        complementaryPairs[tokenB] = tokenA;
    }

    /// <summary>
    /// Удаляет комплементарную пару.
    /// </summary>
    public void UnregisterPair(string tokenA, string tokenB)
    {
        complementaryPairs.TryRemove(tokenA, out _);
        complementaryPairs.TryRemove(tokenB, out _);
    }

    /// <inheritdoc />
    public override PolymarketTradeSignal Evaluate(PolymarketPriceStream priceStream, string assetId)
    {
        ArgumentNullException.ThrowIfNull(priceStream);
        ArgumentException.ThrowIfNullOrEmpty(assetId);

        if (!complementaryPairs.TryGetValue(assetId, out var complementId))
            return HoldSignal(assetId, "Комплементарная пара не зарегистрирована");

        var snapA = priceStream.GetPrice(assetId);
        var snapB = priceStream.GetPrice(complementId);

        if (snapA?.Midpoint is null || snapB?.Midpoint is null)
            return HoldSignal(assetId, "Нет данных о ценах одного или обоих токенов");

        if (!double.TryParse(snapA.Midpoint, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var priceA))
            return HoldSignal(assetId, "Не удалось распарсить цену A");

        if (!double.TryParse(snapB.Midpoint, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var priceB))
            return HoldSignal(assetId, "Не удалось распарсить цену B");

        // На бинарном рынке: priceA + priceB должно быть ≈ 1.0
        var sum = priceA + priceB;
        var deviation = Math.Abs(sum - 1.0);

        if (deviation <= spreadThreshold)
            return HoldSignal(assetId, $"Расхождение {deviation:P2} в пределах порога ({spreadThreshold:P2})");

        // Покупаем дешёвый токен, если сумма < 1 (оба дёшевы)
        // Продаём дорогой, если сумма > 1 (оба дороги)
        if (sum < 1.0 - spreadThreshold)
        {
            // Сумма слишком мала — покупаем более дешёвый
            var cheaperId = priceA < priceB ? assetId : complementId;
            var cheaperPrice = Math.Min(priceA, priceB);
            var snap = priceStream.GetPrice(cheaperId);
            return new PolymarketTradeSignal
            {
                AssetId = cheaperId,
                Action = PolymarketTradeAction.Buy,
                Quantity = PositionSize,
                Price = snap?.BestAsk,
                Confidence = Math.Min(1.0, deviation / spreadThreshold * 0.5),
                Reason = $"Арбитраж: сумма {sum:F4} < 1.0 (расхождение {deviation:P2}), покупка дешёвого ({cheaperPrice:F4})"
            };
        }

        if (sum > 1.0 + spreadThreshold)
        {
            // Сумма слишком велика — продаём более дорогой
            var expensiveId = priceA > priceB ? assetId : complementId;
            var expensivePrice = Math.Max(priceA, priceB);
            var snap = priceStream.GetPrice(expensiveId);
            return new PolymarketTradeSignal
            {
                AssetId = expensiveId,
                Action = PolymarketTradeAction.Sell,
                Quantity = PositionSize,
                Price = snap?.BestBid,
                Confidence = Math.Min(1.0, deviation / spreadThreshold * 0.5),
                Reason = $"Арбитраж: сумма {sum:F4} > 1.0 (расхождение {deviation:P2}), продажа дорогого ({expensivePrice:F4})"
            };
        }

        return HoldSignal(assetId, $"Арбитражное отклонение {deviation:P2} не соответствует направлению");
    }
}
