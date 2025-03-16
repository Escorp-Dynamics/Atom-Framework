using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Protocol;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// Представляет конвертер для сериализации <see cref="Command"/>.
/// </summary>
public class CommandJsonConverter : JsonConverter<Command>
{
    /// <summary>
    /// Десериализует JSON-строку в <see cref="Command"/>.
    /// </summary>
    /// <param name="reader"><see cref="Utf8JsonReader"/>, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа <see cref="Type"/> для преобразования.</param>
    /// <param name="options"><see cref="JsonSerializerOptions"/>, используемые для десериализации JSON.</param>
    /// <returns>Объект <see cref="Command"/>.</returns>
    /// <exception cref="NotImplementedException">Выбрасывается при вызове, так как этот конвертер используется только для сериализации.</exception>
    public override Command? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotImplementedException();

    /// <summary>
    /// Сериализует <see cref="Command"/> в JSON-строку.
    /// </summary>
    /// <param name="writer"><see cref="Utf8JsonWriter"/>, используемый для записи JSON-строки.</param>
    /// <param name="value">Команда, которая будет сериализована.</param>
    /// <param name="options"><see cref="JsonSerializerOptions"/>, используемые для сериализации объекта.</param>
    public override void Write([NotNull] Utf8JsonWriter writer, [NotNull] Command value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("id");
        writer.WriteNumberValue(value.CommandId);

        writer.WritePropertyName("method");
        writer.WriteStringValue(value.CommandName);

        writer.WritePropertyName("params");
        var serializedParams = JsonSerializer.Serialize(value.CommandParameters, value.ParametersTypeInfo);
        writer.WriteRawValue(serializedParams);

        foreach (var pair in value.AdditionalData)
        {
            writer.WritePropertyName(pair.Key);
            writer.WriteRawValue(JsonSerializer.Serialize(pair.Value, JsonContext.Default.Object));
        }

        writer.WriteEndObject();
        writer.Flush();
    }
}