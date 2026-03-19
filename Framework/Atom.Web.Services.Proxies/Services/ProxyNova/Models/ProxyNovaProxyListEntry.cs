using System.Text.Json.Serialization;

namespace Atom.Web.Proxies.Services;

internal sealed class ProxyNovaProxyListEntry
{
    [JsonPropertyName("ip")]
    public string? Ip { get; init; }

    [JsonPropertyName("port")]
    public int? Port { get; init; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("countryName")]
    public string? CountryName { get; init; }

    [JsonPropertyName("cityName")]
    public string? CityName { get; init; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("asn")]
    public string? Asn { get; init; }

    [JsonPropertyName("aliveSecondsAgo")]
    public double? AliveSecondsAgo { get; init; }

    [JsonPropertyName("uptime")]
    public byte? Uptime { get; init; }
}