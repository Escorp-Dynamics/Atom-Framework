using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class EmailOnDeckCreateMailboxResponse
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; init; }
}

internal sealed class EmailOnDeckMessageResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("read")]
    public bool Read { get; init; }
}