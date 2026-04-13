using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed record BridgeManagedDeliveryHealthPayload(
    int Port,
    bool RequiresCertificateBypass,
    string Status,
    string Method,
    string? Detail);

internal sealed record BridgeSecureTransportHealthPayload(
    int Port,
    string Status,
    string Scheme);

internal sealed record BridgeNavigationProxyHealthPayload(
    int Port,
    string Status,
    string Scheme);

internal sealed record BridgeServerHealthPayload(
    string Status,
    string Server,
    string Host,
    int Port,
    BridgeManagedDeliveryHealthPayload ManagedDelivery,
    BridgeSecureTransportHealthPayload SecureTransport,
    BridgeNavigationProxyHealthPayload NavigationProxy,
    int Connections,
    int Sessions,
    int Tabs,
    int PendingRequests,
    long CompletedRequests,
    long FailedRequests);