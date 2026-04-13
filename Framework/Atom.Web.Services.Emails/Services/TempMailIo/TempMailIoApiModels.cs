using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class TempMailIoCreateMailboxRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;
}

internal sealed class TempMailIoCreateMailboxResponse
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

internal sealed class TempMailIoMessageResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("body_text")]
    public string? TextBody { get; init; }

    [JsonPropertyName("body_html")]
    public string? HtmlBody { get; init; }

    [JsonPropertyName("seen")]
    public bool Seen { get; init; }
}