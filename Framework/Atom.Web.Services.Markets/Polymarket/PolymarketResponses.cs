using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Ответ REST API Polymarket при создании ордера.
/// </summary>
public sealed class PolymarketOrderResponse
{
    /// <summary>
    /// Идентификатор созданного ордера.
    /// </summary>
    [JsonPropertyName("orderID")]
    public string? OrderId { get; set; }

    /// <summary>
    /// Признак успешного создания.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Сообщение об ошибке (если произошла).
    /// </summary>
    [JsonPropertyName("errorMsg")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Хеш транзакции (если ордер исполнен немедленно).
    /// </summary>
    [JsonPropertyName("transactionsHashes")]
    public string[]? TransactionHashes { get; set; }

    /// <summary>
    /// Статус ордера.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>
/// Ответ REST API Polymarket при отмене ордера.
/// </summary>
public sealed class PolymarketCancelResponse
{
    /// <summary>
    /// Список отменённых ордеров.
    /// </summary>
    [JsonPropertyName("canceled")]
    public string[]? Canceled { get; set; }

    /// <summary>
    /// Список ордеров, которые не удалось отменить.
    /// </summary>
    [JsonPropertyName("not_canceled")]
    public string[]? NotCanceled { get; set; }
}

/// <summary>
/// Ответ REST API Polymarket с информацией о балансе.
/// </summary>
public sealed class PolymarketBalanceAllowance
{
    /// <summary>
    /// Баланс USDC.
    /// </summary>
    [JsonPropertyName("balance")]
    public string? Balance { get; set; }

    /// <summary>
    /// Разрешённая сумма (allowance).
    /// </summary>
    [JsonPropertyName("allowance")]
    public string? Allowance { get; set; }
}
