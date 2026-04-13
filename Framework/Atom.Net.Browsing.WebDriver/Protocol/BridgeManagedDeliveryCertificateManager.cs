using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Atom.Net.Browsing.WebDriver;

[SuppressMessage("Usage", "CA1001:Types that own disposable fields should be disposable", Justification = "Certificate manager is a process-lifetime singleton.")]
internal sealed class BridgeManagedDeliveryCertificateManager
{
    private const string AuthorityCertificateFileName = "managed-delivery-cert.pfx";
    private const string ServerCertificateFilePrefix = "managed-delivery-server-";
    private static readonly Lazy<BridgeManagedDeliveryCertificateManager> shared = new(static () => new BridgeManagedDeliveryCertificateManager());

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, X509Certificate2> serverCertificates = new(StringComparer.OrdinalIgnoreCase);
    private X509Certificate2? authorityCertificate;
    private string? certificateDirectory;

    private BridgeManagedDeliveryCertificateManager()
    {
    }

    internal static BridgeManagedDeliveryCertificateManager Instance => shared.Value;

    internal X509Certificate2 GetOrCreateAuthorityCertificate()
    {
        if (Volatile.Read(ref authorityCertificate) is { } current)
            return current;

        gate.Wait();
        try
        {
            return GetOrCreateAuthorityCertificateCore();
        }
        finally
        {
            gate.Release();
        }
    }

    internal X509Certificate2 GetOrCreateCertificate(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (serverCertificates.TryGetValue(host, out var current))
            return current;

        gate.Wait();
        try
        {
            if (serverCertificates.TryGetValue(host, out current))
                return current;

            var authority = GetOrCreateAuthorityCertificateCore();
            var serverCertificatePath = ResolveServerCertificatePath(host);
            current = LoadExistingCertificate(serverCertificatePath) ?? CreateAndPersistServerCertificate(serverCertificatePath, authority, host);
            serverCertificates[host] = current;
            return current;
        }
        finally
        {
            gate.Release();
        }
    }

    private X509Certificate2 GetOrCreateAuthorityCertificateCore()
    {
        if (authorityCertificate is not null)
            return authorityCertificate;

        var authorityCertificatePath = ResolveAuthorityCertificatePath();
        authorityCertificate = LoadExistingCertificate(authorityCertificatePath) ?? CreateAndPersistAuthorityCertificate(authorityCertificatePath);
        Volatile.Write(ref this.authorityCertificate, authorityCertificate);
        return authorityCertificate;
    }

    private string ResolveAuthorityCertificatePath()
        => Path.Combine(ResolveCertificateDirectory(), AuthorityCertificateFileName);

    private string ResolveServerCertificatePath(string host)
        => Path.Combine(ResolveCertificateDirectory(), string.Concat(ServerCertificateFilePrefix, SanitizeHostForFileName(host), ".pfx"));

    private string ResolveCertificateDirectory()
    {
        if (!string.IsNullOrWhiteSpace(certificateDirectory))
            return certificateDirectory;

        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
            basePath = Path.Combine(Path.GetTempPath(), "Escorp", "Atom");

        var directory = Path.Combine(basePath, "Escorp", "Atom", "WebDriver");
        Directory.CreateDirectory(directory);
        certificateDirectory = directory;
        return directory;
    }

    private static X509Certificate2? LoadExistingCertificate(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var loaded = X509CertificateLoader.LoadPkcs12FromFile(path, password: null);
            if (!IsUsable(loaded))
            {
                loaded.Dispose();
                return null;
            }

            return loaded;
        }
        catch (Exception ex)
        {
            Observe(ex);
            return null;
        }
    }

    private static bool IsUsable(X509Certificate2 certificate)
        => certificate.HasPrivateKey && certificate.NotAfter > DateTime.UtcNow.AddDays(7);

    private static X509Certificate2 CreateAndPersistAuthorityCertificate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Atom Local WebDriver Delivery", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign |
            X509KeyUsageFlags.CrlSign |
            X509KeyUsageFlags.DigitalSignature,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(
            key: request.PublicKey,
            critical: false));

        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var export = created.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, export);
        return X509CertificateLoader.LoadPkcs12(export, password: null);
    }

    private static X509Certificate2 CreateAndPersistServerCertificate(string path, X509Certificate2 authorityCertificate, string host)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Atom Local WebDriver Loopback", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature |
            X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(
            key: request.PublicKey,
            critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new(value: "1.3.6.1.5.5.7.3.1", friendlyName: null),
        }, critical: false));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
        AddHostSubjectAlternativeName(subjectAlternativeNames, host);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        var serialNumber = new byte[16];
        RandomNumberGenerator.Fill(serialNumber);

        using var created = request.Create(authorityCertificate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2), serialNumber);
        using var withPrivateKey = created.CopyWithPrivateKey(rsa);
        var export = withPrivateKey.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, export);
        return X509CertificateLoader.LoadPkcs12(export, password: null);
    }

    private static void AddHostSubjectAlternativeName(SubjectAlternativeNameBuilder subjectAlternativeNames, string host)
    {
        if (string.IsNullOrWhiteSpace(host)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal))
        {
            return;
        }

        if (IPAddress.TryParse(host, out var hostAddress))
        {
            subjectAlternativeNames.AddIpAddress(hostAddress);
        }
        else
        {
            subjectAlternativeNames.AddDnsName(host);
        }
    }

    private static string SanitizeHostForFileName(string host)
    {
        var sanitized = host.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalidCharacter, '_');

        return sanitized.Replace(':', '_').Replace('.', '_');
    }

    private static void Observe(Exception ex)
        => Trace.TraceWarning(ex.ToString());
}