using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// JSON-конвертер для объекта RemoteValue.
/// </summary>
public class RemoteValueJsonConverter : JsonConverter<RemoteValue>
{
    /// <summary>
    /// Десериализует JSON-строку в значение RemoteValue.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Объект RemoteValue, описанный в JSON.</returns>
    /// <exception cref="JsonException">Выбрасывается при обнаружении недопустимого JSON.</exception>
    public override RemoteValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        var rootElement = doc.RootElement;

        if (rootElement.ValueKind is not JsonValueKind.Object)
            throw new JsonException($"RemoteValue JSON должен быть объектом, но был {rootElement.ValueKind}");

        return ProcessObject(rootElement, options);
    }

    /// <summary>
    /// Сериализует объект RemoteValue в JSON-строку.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Команда для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    /// <exception cref="NotImplementedException">Выбрасывается при вызове, так как этот конвертер используется только для десериализации.</exception>
    public override void Write(Utf8JsonWriter writer, RemoteValue value, JsonSerializerOptions options) => throw new NotImplementedException();

    private static object ProcessNumber(JsonElement token)
    {
        if (token.ValueKind is JsonValueKind.String)
        {
            var specialValue = token.GetString()!;

            if (specialValue is "Infinity") return double.PositiveInfinity;
            if (specialValue is "-Infinity") return double.NegativeInfinity;
            if (specialValue is "NaN") return double.NaN;
            if (specialValue is "-0") return decimal.Negate(decimal.Zero);

            throw new JsonException($"RemoteValue недопустимое значение '{specialValue}' для свойства 'value' числа");
        }
        else if (token.ValueKind is JsonValueKind.Number)
        {
            if (token.TryGetInt64(out var longValue)) return longValue;
            return token.GetDouble();
        }
        else
        {
            var tokenKind = token.ValueKind is JsonValueKind.True or JsonValueKind.False ? "Boolean" : token.ValueKind.ToString();
            throw new JsonException($"RemoteValue недопустимый тип {tokenKind} для свойства 'value' числа");
        }
    }

    private RemoteValue ProcessObject(JsonElement jsonObject, JsonSerializerOptions options)
    {
        if (!jsonObject.TryGetProperty("type", out var typeToken))
            throw new JsonException("RemoteValue должен содержать свойство 'type'");

        if (typeToken.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'type' в RemoteValue должно быть строкой");

        var valueTypeString = typeToken!.GetString()!;

        if (string.IsNullOrEmpty(valueTypeString))
            throw new JsonException("RemoteValue должен содержать непустое свойство 'type', которое является строкой");

        if (!RemoteValue.IsValidRemoteValueType(valueTypeString))
            throw new JsonException($"Значение свойства 'type' в RemoteValue '{valueTypeString}' не является допустимым типом RemoteValue");

        var result = new RemoteValue(valueTypeString);

        if (jsonObject.TryGetProperty("value", out var valueToken))
            ProcessValue(result, valueTypeString, valueToken, options);

        if (jsonObject.TryGetProperty("handle", out var handleToken))
        {
            if (handleToken.ValueKind is not JsonValueKind.String)
                throw new JsonException($"Свойство 'handle' в RemoteValue, если присутствует, должно быть строкой");

            var handle = handleToken.GetString();
            result.Handle = handle;
        }

        if (jsonObject.TryGetProperty("internalId", out var internalIdToken))
        {
            if (internalIdToken.ValueKind is not JsonValueKind.String)
                throw new JsonException($"Свойство 'internalId' в RemoteValue, если присутствует, должно быть строкой");

            var internalId = internalIdToken.GetString();
            result.InternalId = internalId;
        }

        if (result.Type is "node" && jsonObject.TryGetProperty("sharedId", out var sharedIdToken))
        {
            if (sharedIdToken.ValueKind is not JsonValueKind.String)
                throw new JsonException($"Свойство 'sharedId' в RemoteValue, если присутствует, должно быть строкой");

            var sharedId = sharedIdToken.GetString();
            result.SharedId = sharedId;
        }

        return result;
    }

    private void ProcessValue(RemoteValue result, string valueType, JsonElement valueToken, JsonSerializerOptions options)
    {
        if (valueType is "string")
        {
            if (valueToken.ValueKind is not JsonValueKind.String)
                throw new JsonException($"Свойство 'value' в RemoteValue для {valueType} должно быть непустой строкой");

            var stringValue = valueToken.GetString();
            result.Value = stringValue;
        }

        if (valueType is "boolean")
        {
            if (valueToken.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                throw new JsonException($"Свойство 'value' в RemoteValue для {valueType} должно быть логическим значением");

            var boolValue = valueToken.GetBoolean();
            result.Value = boolValue;
        }

        if (valueType is "number") result.Value = ProcessNumber(valueToken);

        if (valueType is "bigint")
        {
            if (valueToken.ValueKind is not JsonValueKind.String)
                throw new JsonException($"RemoteValue для {valueType} должен содержать непустое свойство 'value', значение которого является строкой");

            var bigintString = valueToken.GetString();

            if (!BigInteger.TryParse(bigintString, out var bigintValue))
                throw new JsonException($"RemoteValue не может разобрать недопустимое значение '{bigintString}' для {valueType}");

            result.Value = bigintValue;
        }

        if (valueType is "date")
        {
            if (valueToken.ValueKind is not JsonValueKind.String)
                throw new JsonException($"RemoteValue для {valueType} должен содержать непустое свойство 'value', значение которого является строкой");

            var dateString = valueToken.GetString();

            if (!DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dateTimeValue))
                throw new JsonException($"RemoteValue не может разобрать недопустимое значение '{dateString}' для {valueType}");

            result.Value = dateTimeValue;
        }

        if (valueType is "regexp")
        {
            if (valueToken.ValueKind is not JsonValueKind.Object)
                throw new JsonException($"RemoteValue для {valueType} должен содержать непустое свойство 'value', значение которого является объектом");

            var regexProperties = valueToken.Deserialize(JsonContext.Default.RegularExpressionValue)!;
            result.Value = regexProperties;
        }

        if (valueType is "node")
        {
            if (valueToken.ValueKind is not JsonValueKind.Object)
                throw new JsonException($"RemoteValue для {valueType} должен содержать непустое свойство 'value', значение которого является объектом");

            var nodeProperties = valueToken.Deserialize(JsonContext.Default.NodeProperties)!;
            result.Value = nodeProperties;
        }

        if (valueType is "window")
        {
            if (valueToken.ValueKind is not JsonValueKind.Object)
                throw new JsonException($"RemoteValue для {valueType} должен содержать непустое свойство 'value', значение которого является объектом");

            var windowProxyProperties = valueToken.Deserialize(JsonContext.Default.WindowProxyProperties)!;
            result.Value = windowProxyProperties;
        }

        if (valueType is "array" or "set" or "nodelist" or "htmlcollection")
        {
            if (valueToken.ValueKind is not JsonValueKind.Array)
                throw new JsonException($"RemoteValue для {valueType} должен содержать непустое свойство 'value', значение которого является массивом");

            result.Value = ProcessList(valueToken, options);
        }

        if (valueType is "map" or "object")
        {
            if (valueToken.ValueKind is not JsonValueKind.Array)
                throw new JsonException($"RemoteValue для {valueType} должен содержать непустое свойство 'value', значение которого является массивом");

            result.Value = ProcessMap(valueToken, options);
        }
    }

    private RemoteValueList ProcessList(JsonElement arrayObject, JsonSerializerOptions options)
    {
        var remoteValueList = new List<RemoteValue>();

        foreach (var arrayItem in arrayObject.EnumerateArray())
        {
            if (arrayItem.ValueKind is not JsonValueKind.Object)
                throw new JsonException($"Каждый элемент списка в RemoteValue должен быть объектом");

            remoteValueList.Add(ProcessObject(arrayItem, options));
        }

        return new RemoteValueList(remoteValueList);
    }

    private RemoteValueDictionary ProcessMap(JsonElement mapArray, JsonSerializerOptions options)
    {
        var remoteValueDictionary = new Dictionary<object, RemoteValue>();

        foreach (var mapElementToken in mapArray.EnumerateArray())
        {
            if (mapElementToken.ValueKind is not JsonValueKind.Array)
                throw new JsonException($"Элемент массива для словаря в RemoteValue должен быть массивом");

            if (mapElementToken.GetArrayLength() is not 2)
                throw new JsonException($"Элемент массива для словаря в RemoteValue должен быть массивом с двумя элементами");

            var keyToken = mapElementToken[0];

            if (keyToken.ValueKind is not JsonValueKind.String and not JsonValueKind.Object)
                throw new JsonException($"Первый элемент (ключ) массива для словаря в RemoteValue должен быть строкой или объектом");

            var pairKey = ProcessMapKey(keyToken, options);
            var valueToken = mapElementToken[1];

            if (valueToken.ValueKind is not JsonValueKind.Object)
                throw new JsonException($"Второй элемент (значение) массива для словаря в RemoteValue должен быть объектом");

            var pairValue = ProcessObject(valueToken, options);
            remoteValueDictionary[pairKey] = pairValue;
        }

        return new RemoteValueDictionary(remoteValueDictionary);
    }

    private object ProcessMapKey(JsonElement keyToken, JsonSerializerOptions options)
    {
        object pairKey;

        if (keyToken.ValueKind is JsonValueKind.String)
        {
            pairKey = keyToken.GetString()!;
        }
        else
        {
            var keyRemoteValue = ProcessObject(keyToken, options);

            pairKey = (keyRemoteValue.IsPrimitive || keyRemoteValue.Type is "date" or "regexp") && keyRemoteValue.Value is not null
                ? keyRemoteValue.Value : keyRemoteValue.Handle is not null
                ? keyRemoteValue.Handle : keyRemoteValue.InternalId is not null
                ? keyRemoteValue.InternalId : keyRemoteValue;
        }

        return pairKey;
    }
}