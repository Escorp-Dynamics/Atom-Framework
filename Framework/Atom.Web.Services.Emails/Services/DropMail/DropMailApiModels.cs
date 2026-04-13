using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class DropMailGraphQLRequest
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("variables")]
    public DropMailGraphQLVariables Variables { get; init; } = new();
}

internal sealed class DropMailGraphQLVariables
{
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("id")]
    public string? SessionId { get; init; }
}

internal sealed class DropMailIntroduceSessionEnvelope
{
    [JsonPropertyName("data")]
    public DropMailIntroduceSessionData? Data { get; init; }
}

internal sealed class DropMailIntroduceSessionData
{
    [JsonPropertyName("introduceSession")]
    public DropMailSessionResponse? Session { get; init; }
}

internal sealed class DropMailSessionEnvelope
{
    [JsonPropertyName("data")]
    public DropMailSessionData? Data { get; init; }
}

internal sealed class DropMailSessionData
{
    [JsonPropertyName("session")]
    public DropMailSessionResponse? Session { get; init; }
}

internal sealed class DropMailSessionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("addresses")]
    public DropMailAddressResponse[]? Addresses { get; init; }

    [JsonPropertyName("mails")]
    public DropMailMailResponse[]? Mails { get; init; }
}

internal sealed class DropMailAddressResponse
{
    [JsonPropertyName("address")]
    public string? Address { get; init; }
}

internal sealed class DropMailMailResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("fromAddr")]
    public string? FromAddress { get; init; }

    [JsonPropertyName("toAddr")]
    public string? ToAddress { get; init; }

    [JsonPropertyName("headerSubject")]
    public string? Subject { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("html")]
    public string? Html { get; init; }

    [JsonPropertyName("seen")]
    public bool Seen { get; init; }
}