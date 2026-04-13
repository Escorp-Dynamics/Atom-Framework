using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Atom.Net.Browsing.WebDriver.Protocol;

[JsonSerializable(typeof(BridgeMessage))]
[JsonSerializable(typeof(BridgeMessageType))]
[JsonSerializable(typeof(BridgeCommand))]
[JsonSerializable(typeof(BridgeEvent))]
[JsonSerializable(typeof(BridgeStatus))]
[JsonSerializable(typeof(BridgeHandshakeClientPayload))]
[JsonSerializable(typeof(BridgeHandshakeAcceptPayload))]
[JsonSerializable(typeof(BridgeHandshakeRejectPayload))]
[JsonSerializable(typeof(BridgeManagedDeliveryHealthPayload))]
[JsonSerializable(typeof(BridgeSecureTransportHealthPayload))]
[JsonSerializable(typeof(BridgeNavigationProxyHealthPayload))]
[JsonSerializable(typeof(BridgeServerHealthPayload))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
internal sealed partial class BridgeJsonContext : JsonSerializerContext;