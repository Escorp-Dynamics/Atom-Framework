using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// Представляет средства для преобразования десериализованных данных JSON в правильные форматы.
/// </summary>
public static class JsonConverterUtilities
{
    private static object? ProcessJsonElement(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Object) return ProcessObject(element);
        if (element.ValueKind is JsonValueKind.Array) return ProcessList(element);
        return ProcessValue(element);
    }

    private static ReceivedDataDictionary ProcessObject(JsonElement objectElement)
    {
        var processedObject = new Dictionary<string, object?>();

        foreach (var objectProperty in objectElement.EnumerateObject())
            processedObject[objectProperty.Name] = ProcessJsonElement(objectProperty.Value);

        return new ReceivedDataDictionary(processedObject);
    }

    private static ReceivedDataList ProcessList(JsonElement listElement)
    {
        var processedList = new List<object?>();

        foreach (var listItem in listElement.EnumerateArray())
            processedList.Add(ProcessJsonElement(listItem));

        return new ReceivedDataList(processedList);
    }

    private static object? ProcessValue(JsonElement valueElement)
    {
        if (valueElement.ValueKind is JsonValueKind.Null) return null;
        if (valueElement.ValueKind is JsonValueKind.True) return true;
        if (valueElement.ValueKind is JsonValueKind.False) return false;

        if (valueElement.ValueKind is JsonValueKind.Number)
        {
            if (valueElement.TryGetInt64(out var longValue)) return longValue;

            _ = valueElement.TryGetDouble(out var doubleValue);
            return doubleValue;
        }

        return valueElement.ToString();
    }

    /// <summary>
    /// Преобразует избыточные данные JSON в соответствующие неизменяемые структуры данных .NET.
    /// </summary>
    /// <param name="overflowData">Словарь, содержащий JsonElements для преобразования.</param>
    /// <returns>Неизменяемая структура данных .NET.</returns>
    public static ReceivedDataDictionary ConvertIncomingExtensionData([NotNull] IDictionary<string, JsonElement> overflowData)
    {
        var receivedData = new Dictionary<string, object?>();

        foreach (var entry in overflowData)
            receivedData[entry.Key] = ProcessJsonElement(entry.Value);

        return new ReceivedDataDictionary(receivedData);
    }
}