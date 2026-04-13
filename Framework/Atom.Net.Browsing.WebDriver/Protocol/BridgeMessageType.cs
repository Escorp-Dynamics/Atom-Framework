namespace Atom.Net.Browsing.WebDriver.Protocol;

internal enum BridgeMessageType
{
    Request,
    Response,
    Event,
    Handshake,
    Ping,
    Pong,
}