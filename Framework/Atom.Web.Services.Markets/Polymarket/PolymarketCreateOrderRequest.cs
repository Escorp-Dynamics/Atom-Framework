using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Запрос на создание ордера в Polymarket CLOB.
/// </summary>
/// <remarks>
/// Подпись ордера (EIP-712) должна быть выполнена вне данного клиента
/// с использованием приватного ключа Ethereum-кошелька.
/// </remarks>
public sealed class PolymarketCreateOrderRequest
{
    /// <summary>
    /// Подписанный ордер (содержит EIP-712 подпись).
    /// </summary>
    [JsonPropertyName("order")]
    public required PolymarketSignedOrder Order { get; set; }

    /// <summary>
    /// Адрес владельца ордера.
    /// </summary>
    [JsonPropertyName("owner")]
    public required string Owner { get; set; }

    /// <summary>
    /// Тип ордера (GTC, GTD, FOK).
    /// </summary>
    [JsonPropertyName("orderType")]
    public required PolymarketOrderType OrderType { get; set; }
}

/// <summary>
/// Подписанный ордер для отправки в Polymarket CLOB.
/// </summary>
public sealed class PolymarketSignedOrder
{
    /// <summary>
    /// Случайная соль ордера.
    /// </summary>
    [JsonPropertyName("salt")]
    public required string Salt { get; set; }

    /// <summary>
    /// Адрес мейкера (создателя ордера).
    /// </summary>
    [JsonPropertyName("maker")]
    public required string Maker { get; set; }

    /// <summary>
    /// Адрес подписанта.
    /// </summary>
    [JsonPropertyName("signer")]
    public required string Signer { get; set; }

    /// <summary>
    /// Адрес тейкера (0x0 для открытых ордеров).
    /// </summary>
    [JsonPropertyName("taker")]
    public required string Taker { get; set; }

    /// <summary>
    /// Идентификатор токена.
    /// </summary>
    [JsonPropertyName("tokenId")]
    public required string TokenId { get; set; }

    /// <summary>
    /// Объём мейкера (в минимальных единицах).
    /// </summary>
    [JsonPropertyName("makerAmount")]
    public required string MakerAmount { get; set; }

    /// <summary>
    /// Объём тейкера (в минимальных единицах).
    /// </summary>
    [JsonPropertyName("takerAmount")]
    public required string TakerAmount { get; set; }

    /// <summary>
    /// Дата истечения (UNIX timestamp, "0" = бессрочный).
    /// </summary>
    [JsonPropertyName("expiration")]
    public required string Expiration { get; set; }

    /// <summary>
    /// Nonce ордера.
    /// </summary>
    [JsonPropertyName("nonce")]
    public required string Nonce { get; set; }

    /// <summary>
    /// Комиссия в базисных пунктах.
    /// </summary>
    [JsonPropertyName("feeRateBps")]
    public required string FeeRateBps { get; set; }

    /// <summary>
    /// Сторона ордера.
    /// </summary>
    [JsonPropertyName("side")]
    public required PolymarketSide Side { get; set; }

    /// <summary>
    /// Тип подписи (0 = EOA, 1 = POLY_PROXY, 2 = POLY_GNOSIS_SAFE).
    /// </summary>
    [JsonPropertyName("signatureType")]
    public required int SignatureType { get; set; }

    /// <summary>
    /// EIP-712 подпись ордера.
    /// </summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; set; }
}
