using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Analytics;

/// <summary>
/// Представляет JSON-конвертер для <see cref="Continent"/>.
/// </summary>
/// <typeparam name="T">Тип десериализации.</typeparam>
public class ContinentJsonConverter<T> : JsonConverter<Continent>
{
    /// <inheritdoc />
    public override Continent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.String && Continent.TryParse(reader.GetString(), out var continent)) return continent;
        if (reader.TokenType is not JsonTokenType.StartObject) return default;

        var jsonDocument = JsonDocument.ParseValue(ref reader);
        var rootElement = jsonDocument.RootElement;

        return rootElement.TryGetProperty("code", out var valueElement) && Continent.TryParse(valueElement.GetString(), out continent)
            ? continent
            : default;
    }

    /// <inheritdoc />
    public override void Write([NotNull] Utf8JsonWriter writer, Continent? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (Type.GetTypeCode(typeof(T)))
        {
            case TypeCode.Object:
                writer.WriteStartObject();
                writer.WriteString("code", value.Code);
                writer.WriteEndObject();
                break;

            case TypeCode.String:
                writer.WriteStringValue(value.Code);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

/// <summary>
/// Представляет JSON-конвертер для <see cref="Continent"/>.
/// </summary>
public class ContinentJsonConverter : ContinentJsonConverter<string>;