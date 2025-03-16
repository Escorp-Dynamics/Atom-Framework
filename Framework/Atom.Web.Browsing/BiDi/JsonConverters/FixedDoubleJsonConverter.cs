using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// Представляет конвертер для сохранения формата значений double при сериализации в JSON.
/// </summary>
public class FixedDoubleJsonConverter : JsonConverter<double>
{
    /// <summary>
    /// Десериализует JSON-строку в значение double.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Значение типа double.</returns>
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();

    /// <summary>
    /// Сериализует значение double в JSON-строку, сохраняя десятичные разряды для целочисленных значений.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Значение double для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    public override void Write([NotNull] Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        var numberAsString = value.ToString("0.0###########################", CultureInfo.InvariantCulture);
        writer.WriteRawValue(numberAsString);
    }
}