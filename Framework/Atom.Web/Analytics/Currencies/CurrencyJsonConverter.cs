using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Analytics;

/// <summary>
/// Представляет JSON-конвертер для <see cref="Currency"/>.
/// </summary>
/// <typeparam name="T">Тип десериализации.</typeparam>
public class CurrencyJsonConverter<T> : JsonConverter<Currency>
{
    /// <inheritdoc />
    public override Currency? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Number && reader.TryGetUInt16(out var code) && Currency.TryParse(code, out var currency)) return currency;
        if (reader.TokenType is JsonTokenType.String && Currency.TryParse(reader.GetString(), out currency)) return currency;
        if (reader.TokenType is not JsonTokenType.StartObject) return default;

        var jsonDocument = JsonDocument.ParseValue(ref reader);
        var rootElement = jsonDocument.RootElement;

        if (rootElement.TryGetProperty("code", out var valueElement) && valueElement.TryGetUInt16(out code) && Currency.TryParse(code, out currency)) return currency;

        if (rootElement.TryGetProperty("isoCode", out valueElement) && Currency.TryParse(valueElement.GetString(), out currency)) return currency;

        return default;
    }

    /// <inheritdoc />
    public override void Write([NotNull] Utf8JsonWriter writer, Currency? value, JsonSerializerOptions options)
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
                writer.WriteNumber("code", value.Code);
                writer.WriteString("isoCode", value.IsoCode);
                writer.WriteEndObject();
                break;

            case TypeCode.UInt16:
                writer.WriteNumberValue(value.Code);
                break;

            case TypeCode.String:
                writer.WriteStringValue(value.IsoCode);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

/// <summary>
/// Представляет JSON-конвертер для <see cref="Currency"/>.
/// </summary>
public class CurrencyJsonConverter : CurrencyJsonConverter<string>;