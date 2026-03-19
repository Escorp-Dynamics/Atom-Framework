using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Atom.Net.Proxies;
using Atom.Text.Json;
using Atom.Web.Analytics;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет конвертер сериализатора JSON для <see cref="ServiceProxy"/>.
/// </summary>
/// <typeparam name="T">Тип прокси.</typeparam>
public class ServiceProxyJsonConverter<T> : ProxyJsonConverter<T> where T : ServiceProxy, new()
{
    /// <inheritdoc/>
    protected override T? OnReading(ref Utf8JsonReader reader, JsonElement root, Type typeToConvert, JsonSerializerOptions options)
    {
        var proxy = base.OnReading(ref reader, root, typeToConvert, options);
        if (proxy is null) return proxy;

        if (root.TryGetProperty("anonymity", out var anonymityProperty))
        {
            if (anonymityProperty.ValueKind is JsonValueKind.Number && anonymityProperty.TryGetInt32(out var number))
                proxy.Anonymity = (AnonymityLevel)number;
            else if (anonymityProperty.ValueKind is JsonValueKind.String && Enum.TryParse<AnonymityLevel>(anonymityProperty.GetString(), ignoreCase: true, out var type))
                proxy.Anonymity = type;
        }

        if (root.TryGetProperty("geolocation", out var geolocationElement))
            proxy.Geolocation = Geolocation.Deserialize(geolocationElement);

        if (root.TryGetProperty("asn", out var asnElement))
            proxy.ASN = asnElement.GetString();

        if (root.TryGetProperty("alive", out var aliveElement) && DateTime.TryParseExact(aliveElement.GetString(), DateTimeJsonConverter.DefaultFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var alive))
            proxy.Alive = alive;

        if (root.TryGetProperty("uptime", out var uptimeElement) && uptimeElement.TryGetByte(out var uptime))
            proxy.Uptime = uptime;

        return proxy;
    }

    /// <inheritdoc/>
    protected override void OnWriting([NotNull] Utf8JsonWriter writer, [NotNull] T value, JsonSerializerOptions options)
    {
        base.OnWriting(writer, value, options);

        WriteProperty(writer, nameof(ServiceProxy.Anonymity), value.Anonymity, options);
        WriteProperty(writer, nameof(ServiceProxy.Geolocation), value.Geolocation, options, Geolocation.TypeInfo);
        WriteProperty(writer, nameof(ServiceProxy.ASN), value.ASN, options);
        WriteProperty(writer, nameof(ServiceProxy.Alive), value.Alive, options, converter: DateTimeJsonConverter.Default);
        WriteProperty(writer, nameof(ServiceProxy.Uptime), value.Uptime, options);
    }
}

/// <summary>
/// Представляет конвертер сериализатора JSON для <see cref="ServiceProxy"/>.
/// </summary>
public class ServiceProxyJsonConverter : ServiceProxyJsonConverter<ServiceProxy>;