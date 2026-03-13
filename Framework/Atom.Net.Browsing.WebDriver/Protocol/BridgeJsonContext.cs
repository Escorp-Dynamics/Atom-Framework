using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Контекст JSON-сериализации для протокола обмена сообщениями.
/// </summary>
[JsonSerializable(typeof(BridgeMessage))]
[JsonSerializable(typeof(BridgeMessageType))]
[JsonSerializable(typeof(BridgeCommand))]
[JsonSerializable(typeof(BridgeEvent))]
[JsonSerializable(typeof(BridgeStatus))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
internal sealed partial class BridgeJsonContext : JsonSerializerContext;
