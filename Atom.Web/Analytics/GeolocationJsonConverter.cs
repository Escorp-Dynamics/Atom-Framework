using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Analytics;

// TODO: Закончить.
/*
public class GeolocationJsonConverter : JsonConverter<Geolocation>
{
    public override Geolocation? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartArray)
        {
            var doc = JsonDocument.ParseValue(ref reader);
            var coords = doc.RootElement.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            return new Geolocation(coords[0], coords[1]);
        }
        
        if (reader.TokenType is JsonTokenType.String && Geolocation.TryParse(reader.GetString(), out var geolocation)) return geolocation;
        if (reader.TokenType is not JsonTokenType.StartObject) return default;

        var jsonDocument = JsonDocument.ParseValue(ref reader);
        var rootElement = jsonDocument.RootElement;

        if (rootElement.TryGetProperty("code", out var valueElement) && valueElement.TryGetUInt16(out code) && Currency.TryParse(code, out currency)) return currency;
        if (rootElement.TryGetProperty("isoCode", out valueElement) && Currency.TryParse(valueElement.GetString(), out currency)) return currency;

        return default;
    }

    public override void Write(Utf8JsonWriter writer, Geolocation value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}*/