using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Net.Proxies;

/// <summary>
/// Представляет конвертер сериализатора JSON для <see cref="Proxy"/>.
/// </summary>
/// <typeparam name="T">Тип прокси.</typeparam>
public class ProxyJsonConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T> : ExtendableJsonConverter<T> where T : Proxy, new()
{
    /// <inheritdoc/>
    protected override T? OnReading(ref Utf8JsonReader reader, JsonElement root, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("host", out var hostProperty)) return default;

        var host = hostProperty.GetString();
        if (string.IsNullOrEmpty(host)) return default;

        if (!root.TryGetProperty("port", out var portProperty) || !portProperty.TryGetInt32(out var port)) return default;

        var proxy = Proxy.Rent<T>();

        proxy.Host = host;
        proxy.Port = port;

        if (root.TryGetProperty("userName", out var userNameProperty))
            proxy.UserName = userNameProperty.GetString() ?? string.Empty;

        if (root.TryGetProperty("password", out var passwordProperty))
            proxy.Password = passwordProperty.GetString() ?? string.Empty;

        if (root.TryGetProperty("type", out var typeProperty))
        {
            if (typeProperty.ValueKind is JsonValueKind.Number && typeProperty.TryGetInt32(out var number))
                proxy.Type = (ProxyType)number;
            else if (typeProperty.ValueKind is JsonValueKind.String && Enum.TryParse<ProxyType>(typeProperty.GetString(), true, out var type))
                proxy.Type = type;
        }

        return proxy;
    }

    /// <inheritdoc/>
    protected override void OnWriting([NotNull] Utf8JsonWriter writer, [NotNull] T value, JsonSerializerOptions options)
    {
        WriteProperty(writer, nameof(Proxy.Host), value.Host, options);
        WriteProperty(writer, nameof(Proxy.Port), value.Port, options);

        WriteProperty(writer, nameof(Proxy.UserName), value.UserName, options);
        WriteProperty(writer, nameof(Proxy.Password), value.Password, options);

        WriteProperty(writer, nameof(Proxy.Type), value.Type, options);
    }
}

/// <summary>
/// Представляет конвертер сериализатора JSON для <see cref="Proxy"/>.
/// </summary>
public class ProxyJsonConverter : ProxyJsonConverter<Proxy>;