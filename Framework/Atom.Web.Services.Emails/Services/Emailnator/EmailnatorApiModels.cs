using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class EmailnatorCreateMailboxRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;
}

internal sealed class EmailnatorCreateMailboxResponse
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }
}

internal sealed class EmailnatorMessageListRequest
{
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;
}

internal sealed class EmailnatorMessageListResponse
{
    [JsonPropertyName("messages")]
    public EmailnatorMessageSummaryResponse[]? Messages { get; init; }
}

internal sealed class EmailnatorMessageSummaryResponse
{
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }
}

internal sealed class EmailnatorMessageDetailRequest
{
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = string.Empty;
}

internal sealed class EmailnatorMessageDetailResponse
{
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

internal sealed class EmailnatorDeleteMessageRequest
{
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = string.Empty;
}