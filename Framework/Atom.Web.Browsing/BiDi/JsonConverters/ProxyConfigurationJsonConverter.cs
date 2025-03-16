using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Session;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// JSON-конвертер для объекта ProxyConfiguration.
/// </summary>
public class ProxyConfigurationJsonConverter : JsonConverter<ProxyConfiguration>
{
    /// <summary>
    /// Десериализует JSON-строку в значение ProxyConfiguration.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Подкласс объекта ProxyConfiguration, описанный в JSON.</returns>
    /// <exception cref="JsonException">Выбрасывается при обнаружении недопустимого JSON.</exception>
    public override ProxyConfiguration? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        var rootElement = doc.RootElement;

        if (rootElement.ValueKind is not JsonValueKind.Object)
            throw new JsonException($"JSON прокси должен быть объектом, но был {rootElement.ValueKind}");

        if (!rootElement.TryGetProperty("proxyType", out var typeElement))
        {
            // TODO (Issue #19): Раскомментировать throw и удалить return после исправления
            // https://bugzilla.mozilla.org/show_bug.cgi?id=1916463.
            // throw new JsonException("Ответ прокси должен содержать свойство 'proxyType'");
            return ProxyConfiguration.UnsetProxy;
        }

        if (typeElement.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'proxyType' в ответе прокси должно быть строкой");

        var proxyType = typeElement.Deserialize(JsonContext.Default.ProxyType);
        List<string> propertyNames = ["proxyType"];

        ProxyConfiguration? config;

        if (proxyType is ProxyType.AutoDetect)
        {
            config = rootElement.Deserialize(JsonContext.Default.AutoDetectProxyConfiguration);
        }
        else if (proxyType is ProxyType.Direct)
        {
            config = rootElement.Deserialize(JsonContext.Default.DirectProxyConfiguration);
        }
        else if (proxyType is ProxyType.Manual)
        {
            config = rootElement.Deserialize(JsonContext.Default.ManualProxyConfiguration);
            propertyNames.Add("httpProxy");
            propertyNames.Add("sslProxy");
            propertyNames.Add("ftpProxy");
            propertyNames.Add("socksProxy");
            propertyNames.Add("socksVersion");
            propertyNames.Add("noProxy");
        }
        else if (proxyType is ProxyType.ProxyAutoConfig)
        {
            config = rootElement.Deserialize(JsonContext.Default.PacProxyConfiguration);
            propertyNames.Add("proxyAutoconfigUrl");
        }
        else
        {
            config = rootElement.Deserialize(JsonContext.Default.SystemProxyConfiguration);
        }

        if (config is not null)
        {
            foreach (var property in rootElement.EnumerateObject())
            {
                if (!propertyNames.Contains(property.Name))
                    config.AdditionalData[property.Name] = property.Value;
            }
        }

        return config;
    }

    /// <summary>
    /// Сериализует объект ProxyConfiguration в JSON-строку.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Значение для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    public override void Write([NotNull] Utf8JsonWriter writer, [NotNull] ProxyConfiguration value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(JsonSerializer.Serialize(value, value.TypeInfo));
        writer.Flush();
    }
}