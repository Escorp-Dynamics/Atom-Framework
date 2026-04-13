using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class OneSecMailMessageSummaryResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

internal sealed class OneSecMailMessageDetailResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("htmlBody")]
    public string? HtmlBody { get; set; }

    [JsonPropertyName("textBody")]
    public string? TextBody { get; set; }
}