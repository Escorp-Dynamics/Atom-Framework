using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

internal sealed class HydraDomainCollectionResponse
{
    [JsonPropertyName("hydra:member")]
    public HydraDomainResponse[]? Members { get; set; }
}

internal sealed class HydraDomainResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("isPrivate")]
    public bool IsPrivate { get; set; }
}

internal sealed class HydraCreateAccountRequest
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

internal sealed class HydraAccountResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

internal sealed class HydraTokenRequest
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

internal sealed class HydraTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

internal sealed class HydraMessageCollectionResponse
{
    [JsonPropertyName("hydra:member")]
    public HydraMessageSummaryResponse[]? Members { get; set; }
}

internal sealed class HydraMessageSummaryResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("intro")]
    public string? Intro { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("seen")]
    public bool Seen { get; set; }

    [JsonPropertyName("from")]
    public HydraAddressResponse? From { get; set; }

    [JsonPropertyName("to")]
    public HydraAddressResponse[]? To { get; set; }
}

internal sealed class HydraMessageDetailResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("intro")]
    public string? Intro { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("html")]
    public string[]? Html { get; set; }

    [JsonPropertyName("seen")]
    public bool Seen { get; set; }

    [JsonPropertyName("from")]
    public HydraAddressResponse? From { get; set; }

    [JsonPropertyName("to")]
    public HydraAddressResponse[]? To { get; set; }
}

internal sealed class HydraAddressResponse
{
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class HydraMarkSeenRequest
{
    [JsonPropertyName("seen")]
    public bool Seen { get; set; }
}