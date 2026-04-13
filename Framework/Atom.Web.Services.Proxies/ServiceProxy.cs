using System.Text.Json.Serialization;
using System.Net;
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
    [JsonIgnore]
    internal long Id { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ServiceProxy"/>.
    /// </summary>
    public ServiceProxy() { }

    internal ServiceProxy(long id)
        => Id = id;

    internal ServiceProxy(long id, ServiceProxy source) : this(id)
    {
        ArgumentNullException.ThrowIfNull(source);

        Address = source.Address is null ? null : new Uri(source.Address.AbsoluteUri, UriKind.Absolute);
        Credentials = source.Credentials is NetworkCredential credentials
            ? new NetworkCredential(credentials.UserName, credentials.Password)
            : source.Credentials;
        BypassProxyOnLocal = source.BypassProxyOnLocal;
        Provider = source.Provider;
        Anonymity = source.Anonymity;
        Geolocation = source.Geolocation;
        ASN = source.ASN;
        Alive = source.Alive;
        Uptime = source.Uptime;
        Type = source.Type;
    }

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