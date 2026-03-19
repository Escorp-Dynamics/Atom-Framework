using System.Text.Json.Serialization;
using Atom.Net.Proxies;
using Atom.Text.Json;
using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет <see cref="Proxy"/> с расширенной информацией из сервисов.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
[JsonConverter(typeof(ServiceProxyJsonConverter))]
[JsonContext(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
public partial class ServiceProxy : Proxy
{
    /// <summary>
    /// Имя провайдера, из которого был получен прокси.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Уровень анонимности прокси-сервера.
    /// </summary>
    public AnonymityLevel Anonymity { get; set; }

    /// <summary>
    /// Геолокация прокси-сервера.
    /// </summary>
    public Geolocation? Geolocation { get; set; }

    /// <summary>
    /// ASN.
    /// </summary>
    public string? ASN { get; set; }

    /// <summary>
    /// Время последней активности прокси-сервера.
    /// </summary>
    public DateTime Alive { get; set; }

    /// <summary>
    /// Средняя длительность жизни прокси-сервера.
    /// </summary>
    public byte Uptime { get; set; }
}