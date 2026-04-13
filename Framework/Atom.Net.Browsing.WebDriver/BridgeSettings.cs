using System.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeManagedExtensionDelivery(
    string ExtensionId,
    string ExtensionVersion,
    string UpdateUrl,
    string PackageUrl,
    byte[] PackageBytes)
{
    internal static BridgeManagedExtensionDelivery CreateDiagnosticStub(
        string extensionId,
        string extensionVersion,
        string updateUrl,
        string packageUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(updateUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageUrl);

        var packageBytes = Encoding.UTF8.GetBytes(
            $$"""
            ATOM-CHROMIUM-EXTENSION-STUB
            extensionId={{extensionId}}
            version={{extensionVersion}}
            updateUrl={{updateUrl}}
            packageUrl={{packageUrl}}
            """);

        return new BridgeManagedExtensionDelivery(
            ExtensionId: extensionId,
            ExtensionVersion: extensionVersion,
            UpdateUrl: updateUrl,
            PackageUrl: packageUrl,
            PackageBytes: packageBytes);
    }
}

internal sealed record BridgeManagedDeliveryTrustDiagnostics(
    string Status,
    string Method,
    string? Detail,
    bool RequiresCertificateBypass)
{
    internal static BridgeManagedDeliveryTrustDiagnostics Trusted(string method, string? detail = null)
        => new(
            Status: "trusted",
            Method: method,
            Detail: detail,
            RequiresCertificateBypass: false);

    internal static BridgeManagedDeliveryTrustDiagnostics BypassRequired(string method, string? detail = null)
        => new(
            Status: "bypass-required",
            Method: method,
            Detail: detail,
            RequiresCertificateBypass: true);
}

internal sealed record BridgeManagedPolicyPublishDiagnostics(
    string Status,
    string Method,
    string? Detail,
    string TargetPath,
    bool RequiresSystemPath)
{
    internal static BridgeManagedPolicyPublishDiagnostics ProfileLocal(string targetPath, string? detail = null)
        => new(
            Status: "profile-local",
            Method: "profile-local",
            Detail: detail,
            TargetPath: targetPath,
            RequiresSystemPath: false);

    internal static BridgeManagedPolicyPublishDiagnostics SystemPublished(string method, string targetPath, string? detail = null)
        => new(
            Status: "system-published",
            Method: method,
            Detail: detail,
            TargetPath: targetPath,
            RequiresSystemPath: true);

    internal static BridgeManagedPolicyPublishDiagnostics BrowserInstallationPublished(string method, string targetPath, string? detail = null)
        => new(
            Status: "browser-installation-published",
            Method: method,
            Detail: detail,
            TargetPath: targetPath,
            RequiresSystemPath: false);

    internal static BridgeManagedPolicyPublishDiagnostics SystemPublishRequired(string method, string targetPath, string? detail = null)
        => new(
            Status: "system-publish-required",
            Method: method,
            Detail: detail,
            TargetPath: targetPath,
            RequiresSystemPath: true);
}

/// <summary>
/// Настройки моста между драйвером и browser-side transport.
/// </summary>
internal sealed class BridgeSettings
{
    /// <summary>
    /// Хост, на котором должен слушать bridge endpoint.
    /// </summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// Порт bridge endpoint. <c>0</c> означает автоматический выбор.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Порт HTTPS delivery endpoint для managed Chromium install path. <c>0</c> означает автоматический выбор.
    /// </summary>
    public int ManagedDeliveryPort { get; init; }

    /// <summary>
    /// Порт WSS transport endpoint для browser-side bridge. <c>0</c> означает автоматический выбор.
    /// </summary>
    public int SecureTransportPort { get; init; }

    /// <summary>
    /// Порт локального navigation proxy endpoint для request-side fulfill path. <c>0</c> означает автоматический выбор.
    /// </summary>
    public int NavigationProxyPort { get; init; }

    /// <summary>
    /// Секретный токен для аутентификации browser-side bridge.
    /// </summary>
    public required string Secret { get; init; }

    /// <summary>
    /// Логгер operational diagnostics для bridge слоёв.
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Таймаут ожидания ответа на bridge request.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Таймаут ожидания первичного bootstrap discovery-поверхности.
    /// </summary>
    public TimeSpan BootstrapTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Интервал ping/pong keepalive.
    /// </summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Максимальный размер одного bridge сообщения в байтах.
    /// </summary>
    public int MaxMessageSize { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    /// Автоматически создавать отдельный virtual display для bridge-backed browser session на Linux.
    /// </summary>
    public bool AutoCreateVirtualDisplay { get; init; } = true;

    /// <summary>
    /// Включает opt-in rootless bootstrap для Chromium.
    /// При true Linux stable branded Chromium (Chrome/Edge/Brave/Opera/Vivaldi) использует profile-seeded extension bootstrap вместо system managed policy.
    /// </summary>
    public bool UseRootlessChromiumBootstrap { get; init; }

    /// <summary>
    /// Диагностические артефакты managed-delivery для branded Chromium family.
    /// </summary>
    public BridgeManagedExtensionDelivery? ManagedExtensionDelivery { get; init; }

    /// <summary>
    /// Требуется ли браузеру fallback-обход сертификата для managed-delivery HTTPS.
    /// </summary>
    public bool ManagedDeliveryRequiresCertificateBypass { get; init; } = true;

    /// <summary>
    /// Диагностика результата установки доверия для managed-delivery HTTPS.
    /// </summary>
    public BridgeManagedDeliveryTrustDiagnostics ManagedDeliveryTrustDiagnostics { get; init; } = BridgeManagedDeliveryTrustDiagnostics.BypassRequired("not-evaluated");
}