using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class MaildropMessageResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}