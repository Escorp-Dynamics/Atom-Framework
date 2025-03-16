using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// JSON-конвертер для объекта RealmInfo.
/// </summary>
public class RealmInfoJsonConverter : JsonConverter<RealmInfo>
{
    /// <summary>
    /// Десериализует JSON-строку в значение RealmInfo.
    /// </summary>
    /// <param name="reader">Utf8JsonReader, используемый для чтения входящего JSON.</param>
    /// <param name="typeToConvert">Описание типа Type для преобразования.</param>
    /// <param name="options">JsonSerializationOptions, используемые для десериализации JSON.</param>
    /// <returns>Объект RealmInfo, включая соответствующие подклассы.</returns>
    /// <exception cref="JsonException">Выбрасывается при обнаружении недопустимого JSON.</exception>
    public override RealmInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        var rootElement = doc.RootElement;

        if (rootElement.ValueKind is not JsonValueKind.Object)
            throw new JsonException($"RealmInfo JSON должен быть объектом, но был {rootElement.ValueKind}");

        if (!rootElement.TryGetProperty("type", out var typeElement))
            throw new JsonException("Свойство 'type' в RealmInfo обязательно");

        if (typeElement.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'type' в RealmInfo должно быть строкой");

        if (!rootElement.TryGetProperty("realm", out var realmIdElement))
            throw new JsonException("Свойство 'realm' в RealmInfo обязательно");

        if (realmIdElement.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'realm' в RealmInfo должно быть строкой");

        if (!rootElement.TryGetProperty("origin", out var originElement))
            throw new JsonException("Свойство 'origin' в RealmInfo обязательно");

        if (originElement.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'origin' в RealmInfo должно быть строкой");

        var type = typeElement.Deserialize(JsonContext.Default.RealmType);

        var realmInfo = type switch
        {
            RealmType.Window => ProcessWindowRealmInfo(rootElement),
            RealmType.DedicatedWorker => ProcessDedicatedWorkerRealmInfo(rootElement),
            RealmType.SharedWorker => new SharedWorkerRealmInfo(),
            RealmType.ServiceWorker => new ServiceWorkerRealmInfo(),
            RealmType.AudioWorklet => new AudioWorkletRealmInfo(),
            RealmType.PaintWorklet => new PaintWorkletRealmInfo(),
            _ => new RealmInfo(),
        };

        realmInfo.Type = type;

        var realmId = realmIdElement.GetString()!;
        realmInfo.RealmId = realmId;

        var origin = originElement.GetString()!;
        realmInfo.Origin = origin;

        return realmInfo;
    }

    /// <summary>
    /// Сериализует объект RealmInfo в JSON-строку.
    /// </summary>
    /// <param name="writer">Utf8JsonWriter, используемый для записи JSON-строки.</param>
    /// <param name="value">Команда для сериализации.</param>
    /// <param name="options">JsonSerializationOptions, используемые для сериализации объекта.</param>
    /// <exception cref="NotImplementedException">Выбрасывается при вызове, так как этот конвертер используется только для десериализации.</exception>
    public override void Write(Utf8JsonWriter writer, RealmInfo value, JsonSerializerOptions options) => throw new NotImplementedException();

    private static WindowRealmInfo ProcessWindowRealmInfo(JsonElement rootElement)
    {
        var windowRealmInfo = new WindowRealmInfo();

        if (!rootElement.TryGetProperty("context", out var contextIdElement))
            throw new JsonException("Свойство 'context' в WindowRealmInfo обязательно");

        if (contextIdElement.ValueKind is not JsonValueKind.String)
            throw new JsonException("Свойство 'context' в WindowRealmInfo должно быть строкой");

        var contextId = contextIdElement.GetString()!;
        windowRealmInfo.BrowsingContext = contextId;

        if (rootElement.TryGetProperty("sandbox", out var sandboxElement))
        {
            if (sandboxElement.ValueKind is not JsonValueKind.String)
                throw new JsonException("Свойство 'sandbox' в WindowRealmInfo должно быть строкой");

            var sandbox = sandboxElement.GetString()!;
            windowRealmInfo.Sandbox = sandbox;
        }

        return windowRealmInfo;
    }

    private static DedicatedWorkerRealmInfo ProcessDedicatedWorkerRealmInfo(JsonElement rootElement)
    {
        if (!rootElement.TryGetProperty("owners", out var ownersElement))
            throw new JsonException($"Свойство 'owners' в DedicatedWorkerRealmInfo обязательно");

        if (ownersElement.ValueKind is not JsonValueKind.Array)
            throw new JsonException($"Свойство 'owners' в DedicatedWorkerRealmInfo должно быть массивом");

        var owners = new List<string>();

        foreach (var ownerElement in ownersElement.EnumerateArray())
        {
            if (ownerElement.ValueKind is not JsonValueKind.String)
                throw new JsonException($"Все элементы массива 'owners' в DedicatedWorkerRealmInfo должны быть строками");

            var ownerId = ownerElement.GetString();

            if (string.IsNullOrEmpty(ownerId))
                throw new JsonException($"Все элементы массива 'owners' в DedicatedWorkerRealmInfo должны быть непустыми строками");

            owners.Add(ownerId!);
        }

        var dedicatedWorkerRealmInfo = new DedicatedWorkerRealmInfo();
        dedicatedWorkerRealmInfo.SerializableOwners.AddRange(owners);

        return dedicatedWorkerRealmInfo;
    }
}