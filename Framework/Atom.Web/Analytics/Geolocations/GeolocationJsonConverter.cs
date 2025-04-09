using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Analytics;

/// <summary>
/// Представляет JSON-конвертер для <see cref="Geolocation"/>.
/// </summary>
/// <typeparam name="T">Тип геолокации.</typeparam>
public class GeolocationJsonConverter<T> : ExtendableJsonConverter<T> where T : Geolocation, new()
{
    /// <inheritdoc/>
    protected override T? OnReading(ref Utf8JsonReader reader, JsonElement root, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartArray || root.ValueKind is JsonValueKind.Array)
        {
            var rootElement = root;

            if (root.ValueKind is not JsonValueKind.Array)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                rootElement = doc.RootElement;
            }

            T? geo = default;
            byte i = default;

            foreach (var item in rootElement.EnumerateArray())
            {
                if (!item.TryGetDouble(out var coords)) continue;
                geo ??= Geolocation.Rent<T>();

                if (i is 0)
                    geo.Latitude = coords;
                else if (i is 1)
                    geo.Longitude = coords;
                else if (i is 2)
                    geo.Altitude = coords;
                else
                    break;

                ++i;
            }

            return geo;
        }

        if (reader.TokenType is JsonTokenType.StartObject || root.ValueKind is JsonValueKind.Object)
        {
            var rootElement = root;

            if (root.ValueKind is not JsonValueKind.Object)
            {
                using var jsonDocument = JsonDocument.ParseValue(ref reader);
                rootElement = jsonDocument.RootElement;
            }

            if (!rootElement.TryGetProperty("latitude", out var latitudeElement) || !latitudeElement.TryGetDouble(out var latitude) || !rootElement.TryGetProperty("longitude", out var longitudeElement) || !longitudeElement.TryGetDouble(out var longitude)) return default;

            var geolocation = Geolocation.Rent<T>();
            geolocation.Latitude = latitude;
            geolocation.Longitude = longitude;

            if (rootElement.TryGetProperty("continent", out var continentElement) && Continent.TryParse(continentElement.GetString(), out var continent))
                geolocation.Continent = continent;

            if (rootElement.TryGetProperty("country", out var countryElement) && Country.TryParse(countryElement.GetString(), out var country))
                geolocation.Country = country;

            if (rootElement.TryGetProperty("region", out var regionElement))
                geolocation.Region = regionElement.GetString();

            if (rootElement.TryGetProperty("city", out var cityElement))
                geolocation.City = cityElement.GetString();

            if (rootElement.TryGetProperty("altitude", out var altitudeElement) && altitudeElement.TryGetDouble(out var altitude))
                geolocation.Altitude = altitude;

            return geolocation;
        }

        return default;
    }

    /// <inheritdoc/>
    protected override void OnWriting([NotNull] Utf8JsonWriter writer, [NotNull] T value, JsonSerializerOptions options)
    {
        WriteProperty(writer, nameof(Geolocation.Continent), value.Continent, options, Continent.TypeInfo);
        WriteProperty(writer, nameof(Geolocation.Country), value.Country, options, Country.TypeInfo);
        WriteProperty(writer, nameof(Geolocation.Region), value.Region, options);
        WriteProperty(writer, nameof(Geolocation.City), value.City, options);
        WriteProperty(writer, nameof(Geolocation.Latitude), value.Latitude, options);
        WriteProperty(writer, nameof(Geolocation.Longitude), value.Longitude, options);
        WriteProperty(writer, nameof(Geolocation.Altitude), value.Altitude, options);
    }
}

/// <summary>
/// Представляет конвертер сериализатора JSON для <see cref="Geolocation"/>.
/// </summary>
public class GeolocationJsonConverter : GeolocationJsonConverter<Geolocation>;