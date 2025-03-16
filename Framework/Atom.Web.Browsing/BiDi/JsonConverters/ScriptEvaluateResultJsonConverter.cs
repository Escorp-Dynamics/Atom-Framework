using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// JSON-конвертер для объекта ScriptEvaluateResult.
/// </summary>
public class ScriptEvaluateResultJsonConverter : JsonConverter<EvaluateResult>
{
    /// <summary>
    /// Десериализует JSON-строку в значение EvaluateResult.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Подкласс объекта EvaluateResult, описанный в JSON.</returns>
    /// <exception cref="JsonException">Выбрасывается при обнаружении недопустимого JSON.</exception>
    public override EvaluateResult? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        var rootElement = doc.RootElement;

        if (rootElement.ValueKind is not JsonValueKind.Object)
            throw new JsonException($"JSON ответа скрипта должен быть объектом, но был {rootElement.ValueKind}");

        if (!rootElement.TryGetProperty("type", out var typeElement))
            throw new JsonException("Ответ скрипта должен содержать свойство 'type'");

        if (typeElement.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'type' в ответе скрипта должно быть строкой");

        var resultType = typeElement.GetString()!;

        if (resultType is "success") return rootElement.Deserialize(JsonContext.Default.EvaluateResultSuccess);
        if (resultType is "exception") return rootElement.Deserialize(JsonContext.Default.EvaluateResultException);

        throw new JsonException($"Некорректный ответ: неизвестный тип '{resultType}' для результата скрипта");
    }

    /// <summary>
    /// Сериализует объект EvaluateResult в JSON-строку.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Команда для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    /// <exception cref="NotImplementedException">Выбрасывается при вызове, так как этот конвертер используется только для десериализации.</exception>
    public override void Write(Utf8JsonWriter writer, EvaluateResult value, JsonSerializerOptions options) => throw new NotImplementedException();
}