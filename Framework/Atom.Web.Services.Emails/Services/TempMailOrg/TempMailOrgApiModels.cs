using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class TempMailOrgMessageResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("mail_from")]
    public string? From { get; init; }

    [JsonPropertyName("mail_subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("mail_text")]
    public string? Text { get; init; }

    [JsonPropertyName("mail_html")]
    public string? Html { get; init; }
}