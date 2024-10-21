using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Analytics;

/// <summary>
/// Представляет JSON-конвертер для <see cref="Country"/>.
/// </summary>
/// <typeparam name="T">Тип десериализации.</typeparam>
public class CountryJsonConverter<T> : JsonConverter<Country>
{
    /// <inheritdoc />
    public override Country? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Number && reader.TryGetUInt16(out var code) && Country.TryParse(code, out var country)) return country;
        if (reader.TokenType is JsonTokenType.String && Country.TryParse(reader.GetString(), out country)) return country;
        if (reader.TokenType is not JsonTokenType.StartObject) return default;

        var jsonDocument = JsonDocument.ParseValue(ref reader);
        var rootElement = jsonDocument.RootElement;

        return rootElement.TryGetProperty("code", out var valueElement) && valueElement.TryGetUInt16(out code) && Country.TryParse(code, out country)
            ? country
            : rootElement.TryGetProperty("isoCode", out valueElement) && Country.TryParse(valueElement.GetString(), out country)
            ? country
            : rootElement.TryGetProperty("isoCode2", out valueElement) && Country.TryParse(valueElement.GetString(), out country)
            ? country
            : default;
    }

    /// <inheritdoc />
    public override void Write([NotNull] Utf8JsonWriter writer, Country? value, JsonSerializerOptions options)
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
                writer.WriteString("isoCode2", value.IsoCode2);
                writer.WriteNumber("dialCode", value.DialCode);
                writer.WriteEndObject();
                break;

            case TypeCode.UInt16:
                writer.WriteNumberValue(value.Code);
                break;

            case TypeCode.String:
                writer.WriteStringValue(value.IsoCode);
                break;
            case TypeCode.Empty:
                break;
            case TypeCode.DBNull:
                break;
            case TypeCode.Boolean:
                break;
            case TypeCode.Char:
                break;
            case TypeCode.SByte:
                break;
            case TypeCode.Byte:
                break;
            case TypeCode.Int16:
                break;
            case TypeCode.Int32:
                break;
            case TypeCode.UInt32:
                break;
            case TypeCode.Int64:
                break;
            case TypeCode.UInt64:
                break;
            case TypeCode.Single:
                break;
            case TypeCode.Double:
                break;
            case TypeCode.Decimal:
                break;
            case TypeCode.DateTime:
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

/// <summary>
/// Представляет JSON-конвертер для <see cref="Country"/>.
/// </summary>
public class CountryJsonConverter : CountryJsonConverter<string>;