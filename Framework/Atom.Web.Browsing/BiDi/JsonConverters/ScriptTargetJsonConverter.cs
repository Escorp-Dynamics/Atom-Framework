using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// JSON-конвертер для объекта ScriptTarget.
/// </summary>
public class ScriptTargetJsonConverter : JsonConverter<Target>
{
    /// <summary>
    /// Десериализует JSON-строку в значение Target.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Подкласс объекта Target, описанный в JSON.</returns>
    /// <exception cref="JsonException">Выбрасывается при обнаружении недопустимого JSON.</exception>
    public override Target? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var rootElement = doc.RootElement;

        if (rootElement.ValueKind is not JsonValueKind.Object)
            throw new JsonException($"JSON цели скрипта должен быть объектом, но был {rootElement.ValueKind}");
            
        if (rootElement.TryGetProperty("realm", out _))
            return rootElement.Deserialize(JsonContext.Default.RealmTarget);

        if (rootElement.TryGetProperty("context", out _))
            return rootElement.Deserialize(JsonContext.Default.ContextTarget);

        throw new JsonException("Некорректный ответ: ScriptTarget должен содержать либо свойство 'realm', либо свойство 'context'");
    }

    /// <summary>
    /// Сериализует подкласс объекта Target в JSON-строку.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Команда для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    public override void Write([NotNull] Utf8JsonWriter writer, Target value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.Flush();
    }
}