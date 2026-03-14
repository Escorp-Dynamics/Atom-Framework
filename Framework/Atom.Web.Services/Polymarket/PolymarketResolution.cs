using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Результат разрешения рынка Polymarket.
/// </summary>
public sealed class PolymarketResolution
{
    /// <summary>
    /// Идентификатор рынка (condition ID).
    /// </summary>
    public required string ConditionId { get; init; }

    /// <summary>
    /// Текст вопроса рынка.
    /// </summary>
    public string? Question { get; init; }

    /// <summary>
    /// Исход рынка ("Yes" / "No" или другой).
    /// </summary>
    public string? WinningOutcome { get; init; }

    /// <summary>
    /// Идентификатор выигравшего токена.
    /// </summary>
    public string? WinnerTokenId { get; init; }

    /// <summary>
    /// Идентификатор проигравшего токена.
    /// </summary>
    public string? LoserTokenId { get; init; }

    /// <summary>
    /// Является ли рынок neg-risk.
    /// </summary>
    public bool NegRisk { get; init; }

    /// <summary>
    /// Время разрешения рынка (UNIX timestamp).
    /// </summary>
    public long ResolvedAtTicks { get; init; }

    /// <summary>
    /// Рынок был "закрыт без разрешения" (спорное событие).
    /// </summary>
    public bool IsVoided { get; init; }
}

/// <summary>
/// Аргументы события разрешения рынка.
/// </summary>
public sealed class PolymarketMarketResolvedEventArgs(PolymarketResolution resolution) : EventArgs
{
    /// <summary>
    /// Данные о разрешении рынка.
    /// </summary>
    public PolymarketResolution Resolution { get; } = resolution;
}

/// <summary>
/// Аргументы события обнаружения нового закрытого рынка.
/// </summary>
public sealed class PolymarketMarketClosedEventArgs(PolymarketMarket market) : EventArgs
{
    /// <summary>
    /// Данные рынка.
    /// </summary>
    public PolymarketMarket Market { get; } = market;
}

/// <summary>
/// Состояние отслеживания рынка в EventResolver.
/// </summary>
public sealed class PolymarketTrackedMarket
{
    /// <summary>
    /// Идентификатор рынка (condition ID).
    /// </summary>
    public required string ConditionId { get; init; }

    /// <summary>
    /// Текст вопроса рынка.
    /// </summary>
    public string? Question { get; set; }

    /// <summary>
    /// Является ли рынок neg-risk.
    /// </summary>
    public bool NegRisk { get; set; }

    /// <summary>
    /// Рынок закрыт.
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Рынок разрешён (есть победитель).
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Токены рынка.
    /// </summary>
    public PolymarketToken[]? Tokens { get; set; }

    /// <summary>
    /// Время последней проверки (UNIX timestamp).
    /// </summary>
    public long LastCheckTicks { get; set; }
}

/// <summary>
/// Статус рынка с точки зрения EventResolver.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketMarketStatus>))]
public enum PolymarketMarketStatus
{
    /// <summary>
    /// Рынок активен — принимает ордера.
    /// </summary>
    Active,

    /// <summary>
    /// Рынок закрыт — торги остановлены, ожидание разрешения.
    /// </summary>
    Closed,

    /// <summary>
    /// Рынок разрешён — определён победитель.
    /// </summary>
    Resolved,

    /// <summary>
    /// Рынок аннулирован — спорный исход или отмена.
    /// </summary>
    Voided,

    /// <summary>
    /// Статус неизвестен (ошибка при получении данных).
    /// </summary>
    Unknown
}
