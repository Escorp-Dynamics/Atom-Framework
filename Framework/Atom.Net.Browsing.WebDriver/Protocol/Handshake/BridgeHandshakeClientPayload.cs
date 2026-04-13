using System.Text.Json.Nodes;

namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed record BridgeHandshakeClientPayload(
    string SessionId,
    string Secret,
    int ProtocolVersion,
    string BrowserFamily,
    string ExtensionVersion,
    string? BrowserVersion = null,
    JsonObject? Capabilities = null);