using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Log;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// JSON-конвертер для объекта LogEntry.
/// </summary>
public class LogEntryJsonConverter : JsonConverter<LogEntry>
{
    /// <summary>
    /// Десериализует JSON-строку в значение LogEntry.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Объект LogEntry, включая соответствующие подклассы.</returns>
    /// <exception cref="JsonException">Выбрасывается при обнаружении недопустимого JSON.</exception>
    public override LogEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        LogEntry? entry;
        var doc = JsonDocument.ParseValue(ref reader);
        var rootElement = doc.RootElement;

        if (rootElement.ValueKind is not JsonValueKind.Object)
            throw new JsonException($"LogEntry JSON должен быть объектом, но был {rootElement.ValueKind}");

        if (!rootElement.TryGetProperty("text", out var textElement))
            throw new JsonException("Свойство 'text' в LogEntry обязательно");

        if (!rootElement.TryGetProperty("type", out var typeElement))
            throw new JsonException("Свойство 'type' в LogEntry обязательно");

        if (!rootElement.TryGetProperty("level", out var levelElement))
            throw new JsonException("LogEntry должен содержать свойство 'level'");

        if (!rootElement.TryGetProperty("source", out var sourceElement))
            throw new JsonException("LogEntry должен содержать свойство 'source'");

        if (!rootElement.TryGetProperty("timestamp", out var timestampElement))
            throw new JsonException("LogEntry должен содержать свойство 'timestamp'");

        var hasStackTrace = rootElement.TryGetProperty("stackTrace", out var stackTraceElement);

        if (typeElement.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'type' в LogEntry должно быть строкой");

        var type = typeElement.GetString()!;

        if (type is "console")
        {
            var consoleEntry = new ConsoleLogEntry();

            if (!rootElement.TryGetProperty("method", out var methodElement))
                throw new JsonException("Свойство 'method' в ConsoleLogEntry обязательно");

            if (methodElement.ValueKind is not JsonValueKind.String)
                throw new JsonException("Свойство 'method' в ConsoleLogEntry должно быть строкой");

            var method = methodElement.GetString()!;
            consoleEntry.Method = method;

            if (!rootElement.TryGetProperty("args", out var argsElement))
                throw new JsonException("Свойство 'args' в ConsoleLogEntry обязательно");

            if (argsElement.ValueKind is not JsonValueKind.Array)
                throw new JsonException("Свойство 'args' в ConsoleLogEntry должно быть массивом");

            var args = new List<RemoteValue>();

            foreach (var arg in argsElement.EnumerateArray())
            {
                var value = arg.Deserialize(JsonContext.Default.RemoteValue)!;
                args.Add(value);
            }

            consoleEntry.SerializableArgs = args;
            entry = consoleEntry;
        }
        else
        {
            entry = new LogEntry();
        }

        entry.Type = type;
        entry.Text = textElement.GetString();
        entry.Level = levelElement.Deserialize(JsonContext.Default.LogLevel);

        entry.Source = sourceElement.Deserialize(JsonContext.Default.Source)!;
        entry.EpochTimestamp = timestampElement.GetInt64();

        if (hasStackTrace) entry.StackTrace = stackTraceElement.Deserialize(JsonContext.Default.StackTrace);

        return entry;
    }

    /// <summary>
    /// Сериализует объект LogEntry в JSON-строку.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Команда для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    /// <exception cref="NotImplementedException">Выбрасывается при вызове, так как этот конвертер используется только для десериализации.</exception>
    public override void Write(Utf8JsonWriter writer, LogEntry value, JsonSerializerOptions options) => throw new NotImplementedException();
}