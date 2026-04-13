using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class GuerrillaMailSetEmailAddressResponse
{
    [JsonPropertyName("email_addr")]
    public string? EmailAddress { get; set; }

    [JsonPropertyName("sid_token")]
    public string? SessionToken { get; set; }
}

internal sealed class GuerrillaMailInboxResponse
{
    [JsonPropertyName("list")]
    public GuerrillaMailMessageSummaryResponse[]? Messages { get; set; }
}

internal sealed class GuerrillaMailMessageSummaryResponse
{
    [JsonPropertyName("mail_id")]
    public string? MailId { get; set; }

    [JsonPropertyName("mail_from")]
    public string? From { get; set; }

    [JsonPropertyName("mail_subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("mail_excerpt")]
    public string? Excerpt { get; set; }

    [JsonPropertyName("mail_read")]
    public int MailRead { get; set; }
}

internal sealed class GuerrillaMailMessageDetailResponse
{
    [JsonPropertyName("mail_id")]
    public string? MailId { get; set; }

    [JsonPropertyName("mail_from")]
    public string? From { get; set; }

    [JsonPropertyName("mail_subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("mail_body")]
    public string? Body { get; set; }
}