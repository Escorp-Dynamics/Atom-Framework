using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Тип условия срабатывания алерта.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketAlertCondition>))]
public enum PolymarketAlertCondition
{
    /// <summary>
    /// P&amp;L позиции достиг порога.
    /// </summary>
    PnLThreshold,

    /// <summary>
    /// Цена токена достигла порога.
    /// </summary>
    PriceThreshold,

    /// <summary>
    /// Рынок закрыт (торги остановлены).
    /// </summary>
    MarketClosed,

    /// <summary>
    /// Рынок разрешён (определён победитель).
    /// </summary>
    MarketResolved,

    /// <summary>
    /// Суммарный P&amp;L портфеля достиг порога.
    /// </summary>
    PortfolioPnLThreshold
}

/// <summary>
/// Направление сравнения для числовых порогов.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketAlertDirection>))]
public enum PolymarketAlertDirection
{
    /// <summary>
    /// Значение >= порога.
    /// </summary>
    Above,

    /// <summary>
    /// Значение &lt;= порога.
    /// </summary>
    Below
}

/// <summary>
/// Определение алерта Polymarket.
/// </summary>
public sealed class PolymarketAlertDefinition
    : IMarketAlertDefinition
{
    /// <summary>
    /// Уникальный идентификатор алерта.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Условие срабатывания.
    /// </summary>
    public PolymarketAlertCondition Condition { get; init; }

    /// <summary>
    /// Направление сравнения (для числовых порогов).
    /// </summary>
    public PolymarketAlertDirection Direction { get; init; }

    // IMarketAlertDefinition — явная реализация (конвертация enum)
    AlertCondition IMarketAlertDefinition.Condition => (AlertCondition)(byte)Condition;
    AlertDirection IMarketAlertDefinition.Direction => (AlertDirection)(byte)Direction;

    /// <summary>
    /// Пороговое значение (для PnLThreshold, PriceThreshold, PortfolioPnLThreshold).
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>
    /// Идентификатор актива (для PnLThreshold, PriceThreshold).
    /// Null = применяется ко всему портфелю.
    /// </summary>
    public string? AssetId { get; init; }

    /// <summary>
    /// Идентификатор рынка (для MarketClosed, MarketResolved).
    /// </summary>
    public string? ConditionId { get; init; }

    /// <summary>
    /// Пользовательское описание алерта.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Одноразовый алерт (автоматически удаляется после срабатывания).
    /// </summary>
    public bool OneShot { get; init; } = true;

    /// <summary>
    /// Алерт активен.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Алерт уже сработал.
    /// </summary>
    public bool HasTriggered { get; set; }
}

/// <summary>
/// Аргументы события срабатывания алерта.
/// </summary>
public sealed class PolymarketAlertTriggeredEventArgs(PolymarketAlertDefinition alert, double currentValue) : EventArgs
{
    /// <summary>
    /// Определение алерта, который сработал.
    /// </summary>
    public PolymarketAlertDefinition Alert { get; } = alert;

    /// <summary>
    /// Текущее значение, вызвавшее срабатывание.
    /// </summary>
    public double CurrentValue { get; } = currentValue;

    /// <summary>
    /// Время срабатывания (UTC).
    /// </summary>
    public DateTimeOffset TriggeredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Система алертов Polymarket — мониторит позиции, цены и рыночные события,
/// и уведомляет при достижении пороговых значений.
/// </summary>
/// <remarks>
/// Подключается к <see cref="PolymarketPortfolioTracker"/> и <see cref="PolymarketEventResolver"/>
/// для получения событий обновления позиций и разрешения рынков.
/// Совместим с NativeAOT. Потокобезопасен.
/// </remarks>
public sealed class PolymarketAlertSystem : IMarketAlertSystem, IDisposable
{
    private readonly ConcurrentDictionary<string, PolymarketAlertDefinition> alerts = new();
    private PolymarketPortfolioTracker? connectedTracker;
    private PolymarketEventResolver? connectedResolver;
    private bool isDisposed;

    /// <summary>
    /// Событие срабатывания алерта.
    /// </summary>
    public event AsyncEventHandler<PolymarketAlertSystem, PolymarketAlertTriggeredEventArgs>? AlertTriggered;

    /// <summary>
    /// Все зарегистрированные алерты.
    /// </summary>
    public IReadOnlyDictionary<string, PolymarketAlertDefinition> Alerts => alerts;

    /// <summary>
    /// Количество активных алертов.
    /// </summary>
    public int ActiveCount => alerts.Values.Count(a => a.IsEnabled && !a.HasTriggered);

    /// <summary>
    /// Регистрирует новый алерт.
    /// </summary>
    /// <param name="alert">Определение алерта.</param>
    public void AddAlert(PolymarketAlertDefinition alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        alerts[alert.Id] = alert;
    }

    /// <summary>
    /// Удаляет алерт по идентификатору.
    /// </summary>
    /// <param name="alertId">Идентификатор алерта.</param>
    public void RemoveAlert(string alertId) =>
        alerts.TryRemove(alertId, out _);

    /// <summary>
    /// Получает алерт по идентификатору.
    /// </summary>
    /// <param name="alertId">Идентификатор алерта.</param>
    public PolymarketAlertDefinition? GetAlert(string alertId) =>
        alerts.TryGetValue(alertId, out var alert) ? alert : null;

    // IMarketAlertSystem — явная реализация
    void IMarketAlertSystem.AddAlert(IMarketAlertDefinition alert) => AddAlert((PolymarketAlertDefinition)alert);
    IMarketAlertDefinition? IMarketAlertSystem.GetAlert(string alertId) => GetAlert(alertId);

    /// <summary>
    /// Удаляет все алерты.
    /// </summary>
    public void ClearAlerts() => alerts.Clear();

    /// <summary>
    /// Подключает систему алертов к трекеру портфеля для отслеживания P&amp;L и цен.
    /// </summary>
    /// <param name="tracker">Трекер портфеля.</param>
    public void ConnectTracker(PolymarketPortfolioTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (connectedTracker is not null)
            connectedTracker.PositionChanged -= OnPositionChanged;

        connectedTracker = tracker;
        connectedTracker.PositionChanged += OnPositionChanged;
    }

    /// <summary>
    /// Подключает систему алертов к EventResolver для отслеживания закрытия/разрешения рынков.
    /// </summary>
    /// <param name="resolver">EventResolver.</param>
    public void ConnectResolver(PolymarketEventResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        if (connectedResolver is not null)
        {
            connectedResolver.MarketClosed -= OnMarketClosed;
            connectedResolver.MarketResolved -= OnMarketResolved;
        }

        connectedResolver = resolver;
        connectedResolver.MarketClosed += OnMarketClosed;
        connectedResolver.MarketResolved += OnMarketResolved;
    }

    /// <summary>
    /// Отключает все подключённые источники событий.
    /// </summary>
    public void DisconnectAll()
    {
        if (connectedTracker is not null)
        {
            connectedTracker.PositionChanged -= OnPositionChanged;
            connectedTracker = null;
        }

        if (connectedResolver is not null)
        {
            connectedResolver.MarketClosed -= OnMarketClosed;
            connectedResolver.MarketResolved -= OnMarketResolved;
            connectedResolver = null;
        }
    }

    #region Обработчики событий

    private ValueTask OnPositionChanged(PolymarketPortfolioTracker sender, PolymarketPositionChangedEventArgs e)
    {
        var position = e.Position;

        foreach (var alert in alerts.Values)
        {
            if (!alert.IsEnabled || alert.HasTriggered)
                continue;

            switch (alert.Condition)
            {
                case PolymarketAlertCondition.PnLThreshold when alert.AssetId == position.AssetId:
                    CheckThreshold(alert, position.UnrealizedPnL);
                    break;

                case PolymarketAlertCondition.PriceThreshold when alert.AssetId == position.AssetId:
                    CheckThreshold(alert, position.CurrentPrice);
                    break;

                case PolymarketAlertCondition.PortfolioPnLThreshold when connectedTracker is not null:
                    var summary = connectedTracker.GetSummary();
                    CheckThreshold(alert, summary.NetPnL);
                    break;
            }
        }

        return default;
    }

    private ValueTask OnMarketClosed(PolymarketEventResolver sender, PolymarketMarketClosedEventArgs e)
    {
        foreach (var alert in alerts.Values)
        {
            if (!alert.IsEnabled || alert.HasTriggered)
                continue;

            if (alert.Condition == PolymarketAlertCondition.MarketClosed &&
                alert.ConditionId == e.Market.ConditionId)
            {
                TriggerAlert(alert, 0);
            }
        }

        return default;
    }

    private ValueTask OnMarketResolved(PolymarketEventResolver sender, PolymarketMarketResolvedEventArgs e)
    {
        foreach (var alert in alerts.Values)
        {
            if (!alert.IsEnabled || alert.HasTriggered)
                continue;

            if (alert.Condition == PolymarketAlertCondition.MarketResolved &&
                alert.ConditionId == e.Resolution.ConditionId)
            {
                TriggerAlert(alert, 0);
            }
        }

        return default;
    }

    #endregion

    #region Вспомогательные методы

    private void CheckThreshold(PolymarketAlertDefinition alert, double currentValue)
    {
        var triggered = alert.Direction == PolymarketAlertDirection.Above
            ? currentValue >= alert.Threshold
            : currentValue <= alert.Threshold;

        if (triggered)
            TriggerAlert(alert, currentValue);
    }

    private void TriggerAlert(PolymarketAlertDefinition alert, double currentValue)
    {
        alert.HasTriggered = true;

        if (alert.OneShot)
            alert.IsEnabled = false;

        AlertTriggered?.Invoke(this, new PolymarketAlertTriggeredEventArgs(alert, currentValue));
    }

    #endregion

    /// <summary>
    /// Освобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        DisconnectAll();
    }
}
