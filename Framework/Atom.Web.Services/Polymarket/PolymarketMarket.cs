using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Полная модель рынка Polymarket, полученная через REST API.
/// </summary>
public sealed class PolymarketMarket
{
    /// <summary>
    /// Идентификатор условия (condition ID).
    /// </summary>
    [JsonPropertyName("condition_id")]
    public string? ConditionId { get; set; }

    /// <summary>
    /// Идентификатор вопроса.
    /// </summary>
    [JsonPropertyName("question_id")]
    public string? QuestionId { get; set; }

    /// <summary>
    /// Токены (исходы) рынка.
    /// </summary>
    [JsonPropertyName("tokens")]
    public PolymarketToken[]? Tokens { get; set; }

    /// <summary>
    /// Минимальный размер ордера.
    /// </summary>
    [JsonPropertyName("minimum_order_size")]
    public double MinimumOrderSize { get; set; }

    /// <summary>
    /// Минимальный шаг цены.
    /// </summary>
    [JsonPropertyName("minimum_tick_size")]
    public double MinimumTickSize { get; set; }

    /// <summary>
    /// Текстовое описание рынка.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Категория рынка.
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Дата окончания рынка (ISO формат).
    /// </summary>
    [JsonPropertyName("end_date_iso")]
    public string? EndDateIso { get; set; }

    /// <summary>
    /// Текст вопроса рынка.
    /// </summary>
    [JsonPropertyName("question")]
    public string? Question { get; set; }

    /// <summary>
    /// Slug рынка (для URL).
    /// </summary>
    [JsonPropertyName("market_slug")]
    public string? MarketSlug { get; set; }

    /// <summary>
    /// Активен ли рынок.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>
    /// Закрыт ли рынок.
    /// </summary>
    [JsonPropertyName("closed")]
    public bool Closed { get; set; }

    /// <summary>
    /// Задержка исполнения ордеров (секунды).
    /// </summary>
    [JsonPropertyName("seconds_delay")]
    public int SecondsDelay { get; set; }

    /// <summary>
    /// URL иконки рынка.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Адрес FPMM-контракта.
    /// </summary>
    [JsonPropertyName("fpmm")]
    public string? Fpmm { get; set; }

    /// <summary>
    /// Является ли рынок neg-risk.
    /// </summary>
    [JsonPropertyName("neg_risk")]
    public bool NegRisk { get; set; }

    /// <summary>
    /// Идентификатор neg-risk рынка.
    /// </summary>
    [JsonPropertyName("neg_risk_market_id")]
    public string? NegRiskMarketId { get; set; }

    /// <summary>
    /// Принимает ли рынок ордера.
    /// </summary>
    [JsonPropertyName("accepting_orders")]
    public bool AcceptingOrders { get; set; }
}
