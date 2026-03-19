using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Учётные данные для аутентификации в WebSocket API Polymarket.
/// </summary>
public sealed class PolymarketAuth
{
    /// <summary>
    /// API-ключ пользователя.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; init; }

    /// <summary>
    /// Секретный ключ.
    /// </summary>
    [JsonPropertyName("secret")]
    public required string Secret { get; init; }

    /// <summary>
    /// Парольная фраза.
    /// </summary>
    [JsonPropertyName("passphrase")]
    public required string Passphrase { get; init; }
}
