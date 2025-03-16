using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Text.Json;

/// <summary>
/// Конвертер JSON для <see cref="DateTime"/>.
/// </summary>
public sealed class DateTimeJsonConverter : JsonConverter<DateTime>
{
    private static readonly Lazy<DateTimeJsonConverter> defaultConverter = new(() => new(), true);

    /// <summary>
    /// Формат.
    /// </summary>
    public static string DefaultFormat { get; set; } = "dd.MM.yyyy HH:mm:ss";

    /// <summary>
    /// Конвертер по умолчанию.
    /// </summary>
    public static DateTimeJsonConverter Default => defaultConverter.Value;

    /// <inheritdoc/>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (DateTime.TryParseExact(reader.GetString(), DefaultFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)) return result;
        throw new JsonException($"Формат даты и времени не соответствует шаблону '{DefaultFormat}'");
    }

    /// <inheritdoc/>
    public override void Write([NotNull] Utf8JsonWriter writer, DateTime value, [NotNull] JsonSerializerOptions options)
    {
        if (options.DefaultIgnoreCondition is JsonIgnoreCondition.Always) return;
        if (options.DefaultIgnoreCondition is JsonIgnoreCondition.WhenWritingDefault && value == default) return;
        writer.WriteStringValue(value.ToString(DefaultFormat));
    }
}