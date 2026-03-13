using System.Text.Json.Serialization;

namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Сообщение протокола обмена между драйвером и расширением браузера.
/// </summary>
/// <remarks>
/// Каждое сообщение содержит уникальный идентификатор для сопоставления запросов и ответов,
/// идентификатор вкладки для маршрутизации и типизированную полезную нагрузку.
/// </remarks>
public sealed class BridgeMessage
{
    /// <summary>
    /// Уникальный идентификатор сообщения для корреляции запрос-ответ.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Тип сообщения.
    /// </summary>
    [JsonPropertyName("type")]
    public required BridgeMessageType Type { get; init; }

    /// <summary>
    /// Идентификатор вкладки, к которой относится сообщение.
    /// </summary>
    [JsonPropertyName("tabId")]
    public string? TabId { get; init; }

    /// <summary>
    /// Команда для выполнения (для запросов).
    /// </summary>
    [JsonPropertyName("command")]
    public BridgeCommand? Command { get; init; }

    /// <summary>
    /// Тип события (для событий).
    /// </summary>
    [JsonPropertyName("event")]
    public BridgeEvent? Event { get; init; }

    /// <summary>
    /// Статус выполнения (для ответов).
    /// </summary>
    [JsonPropertyName("status")]
    public BridgeStatus? Status { get; init; }

    /// <summary>
    /// Полезная нагрузка сообщения в виде JSON-элемента.
    /// </summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    /// <summary>
    /// Сообщение об ошибке (для ответов с ошибкой).
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Временная метка создания сообщения (UNIX ms).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
