using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class BridgeExtensionBootstrapLogs
{
    [LoggerMessage(EventId = 1868, Level = LogLevel.Information, Message = "Firefox profile WSS trust status {Status}, method {Method}, detail {Detail}, profilePath {ProfilePath}")]
    public static partial void LogFirefoxProfileWssTrust(this ILogger logger, string status, string method, string? detail, string profilePath);
}