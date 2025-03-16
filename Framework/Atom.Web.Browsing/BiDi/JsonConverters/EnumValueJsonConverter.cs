using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// Представляет конвертер, который преобразует перечисляемый тип в строки JSON и обратно.
/// </summary>
/// <typeparam name="T">Перечисление для преобразования.</typeparam>
public class EnumValueJsonConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> : JsonConverter<T> where T : struct, Enum
{
    private readonly T? defaultValue;
    private readonly Dictionary<T, string> enumValuesToStrings = [];
    private readonly Dictionary<string, T> stringToEnumValues = [];

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EnumValueJsonConverter{T}"/>.
    /// </summary>
    public EnumValueJsonConverter()
    {
        var enumType = typeof(T);
        var unmatchedValueAttribute = enumType.GetCustomAttribute<JsonEnumUnmatchedValueAttribute<T>>();

        if (unmatchedValueAttribute is not null) defaultValue = unmatchedValueAttribute.UnmatchedValue;

        var values = Enum.GetValues<T>();

        foreach (var value in values)
        {
            var valueAsString = value.ToString().ToLowerInvariant();
            var member = enumType.GetMember(value.ToString())[0];
            var attribute = member.GetCustomAttribute<JsonEnumValueAttribute>();

            if (attribute is not null) valueAsString = attribute.Value;

            stringToEnumValues[valueAsString] = value;
            enumValuesToStrings[value] = valueAsString;
        }
    }

    /// <summary>
    /// Десериализует JSON-строку в значение перечисления.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Значение указанного перечисления.</returns>
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String) throw new BiDiException("Ошибка десериализации при чтении строкового значения перечисления");

        var stringValue = reader.GetString()!;

        if (stringToEnumValues.TryGetValue(stringValue, out var enumValue)) return enumValue;
        if (defaultValue.HasValue) return defaultValue.Value;

        throw new BiDiException($"Ошибка десериализации: значение '{stringValue}' недопустимо для типа перечисления {typeof(T)}");
    }

    /// <summary>
    /// Сериализует значение перечисления в JSON-строку.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Значение перечисления для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    public override void Write([NotNull] Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(enumValuesToStrings[value]);
}