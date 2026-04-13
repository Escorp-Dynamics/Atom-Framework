using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Atom.Net.Browsing.WebDriver;

internal static class BridgeManagedDeliveryCertificateTrustInstaller
{
    private const string CertificateAlias = "Atom Local WebDriver Delivery";
    private const string RootPasswordEnv = "ATOM_WEBDRIVER_ROOT_PASSWORD";
    private const string LegacyRootPasswordEnv = "ESCORP_ROOT_PASSWORD";

    internal static BridgeManagedDeliveryTrustDiagnostics EnsureTrusted(X509Certificate2 certificate)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return EnsureWindowsTrust(certificate);

            if (OperatingSystem.IsLinux())
                return EnsureLinuxTrust(certificate);

            if (OperatingSystem.IsMacOS())
                return EnsureMacTrust(certificate);
        }
        catch (Exception ex)
        {
            Observe(ex);
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("trust-install-exception", ex.Message);
        }

        return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("unsupported-platform", "Платформа не поддерживает автоматическую установку доверия");
    }

    internal static BridgeManagedDeliveryTrustDiagnostics EnsureTrustedForFirefoxProfile(X509Certificate2 certificate, string profilePath)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);

        try
        {
            if (!OperatingSystem.IsLinux())
                return BridgeManagedDeliveryTrustDiagnostics.Trusted("firefox-profile-trust-not-required");

            var certificateBytes = certificate.Export(X509ContentType.Cert);
            return TryInstallLinuxNssTrust(certificateBytes, profilePath, "linux-firefox-profile-nssdb", "C,,");
        }
        catch (Exception ex)
        {
            Observe(ex);
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("firefox-profile-trust-exception", ex.Message);
        }
    }

    [SuppressMessage("Security", "CA5380:Do not add certificates to root store", Justification = "Managed delivery must bootstrap local browser trust for loopback HTTPS updates.")]
    private static BridgeManagedDeliveryTrustDiagnostics EnsureWindowsTrust(X509Certificate2 certificate)
    {
        var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));

        using var userStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        userStore.Open(OpenFlags.ReadWrite);
        if (!ContainsCertificate(userStore, publicCertificate))
            userStore.Add(publicCertificate);
        if (ContainsCertificate(userStore, publicCertificate))
            return BridgeManagedDeliveryTrustDiagnostics.Trusted("windows-current-user-root");

        try
        {
            using var machineStore = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            machineStore.Open(OpenFlags.ReadWrite);
            if (!ContainsCertificate(machineStore, publicCertificate))
                machineStore.Add(publicCertificate);

            if (ContainsCertificate(machineStore, publicCertificate))
                return BridgeManagedDeliveryTrustDiagnostics.Trusted("windows-local-machine-root");
        }
        catch (Exception ex)
        {
            Observe(ex);
            // User-level trust is sufficient for browser launch fallback decisions.
        }

        return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("windows-root-store", "Не удалось подтвердить добавление сертификата в доверенное хранилище Windows");
    }

    private static BridgeManagedDeliveryTrustDiagnostics EnsureLinuxTrust(X509Certificate2 certificate)
    {
        var certificateBytes = certificate.Export(X509ContentType.Cert);
        var nssTrust = TryInstallLinuxNssTrust(certificateBytes);
        if (!nssTrust.RequiresCertificateBypass)
            return nssTrust;

        var systemTrust = TryInstallLinuxSystemTrust(certificateBytes);
        if (!systemTrust.RequiresCertificateBypass)
            return systemTrust;

        return BridgeManagedDeliveryTrustDiagnostics.BypassRequired(
            "linux-untrusted",
            string.Concat(nssTrust.Method, ": ", nssTrust.Detail ?? "без деталей", "; ", systemTrust.Method, ": ", systemTrust.Detail ?? "без деталей"));
    }

    private static BridgeManagedDeliveryTrustDiagnostics TryInstallLinuxNssTrust(byte[] certificateBytes)
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("linux-nssdb", "Переменная HOME не задана");

        var nssDbPath = Path.Combine(home, ".pki", "nssdb");
        return TryInstallLinuxNssTrust(certificateBytes, nssDbPath, "linux-nssdb", "C,,");
    }

    private static BridgeManagedDeliveryTrustDiagnostics TryInstallLinuxNssTrust(byte[] certificateBytes, string nssDbPath, string method, string trustFlags)
    {
        if (!IsCommandAvailable("certutil"))
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired(method, "Утилита certutil недоступна");

        Directory.CreateDirectory(nssDbPath);

        if (!EnsureLinuxNssDatabase(nssDbPath))
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired(method, "Не удалось инициализировать NSS database");

        if (IsLinuxNssCertificatePresent(nssDbPath))
            return BridgeManagedDeliveryTrustDiagnostics.Trusted(method, nssDbPath);

        var certPath = Path.Combine(nssDbPath, "atom-webdriver-delivery.crt");
        File.WriteAllBytes(certPath, certificateBytes);

        RemoveLinuxNssCertificate(nssDbPath);
        var (exitCode, _, standardError) = RunProcess(
            "certutil",
            ["-d", $"sql:{nssDbPath}", "-A", "-t", trustFlags, "-n", CertificateAlias, "-i", certPath],
            TimeSpan.FromSeconds(5));
        if (exitCode == 0)
            return BridgeManagedDeliveryTrustDiagnostics.Trusted(method, nssDbPath);

        var detail = string.IsNullOrWhiteSpace(standardError) ? "certutil import failed" : standardError.Trim();
        return BridgeManagedDeliveryTrustDiagnostics.BypassRequired(method, detail);
    }

    private static bool EnsureLinuxNssDatabase(string nssDbPath)
    {
        var certificateDatabasePath = Path.Combine(nssDbPath, "cert9.db");
        if (File.Exists(certificateDatabasePath))
            return true;

        return RunProcess(
            "certutil",
            ["-d", $"sql:{nssDbPath}", "-N", "--empty-password"],
            TimeSpan.FromSeconds(5)).ExitCode == 0;
    }

    private static bool IsLinuxNssCertificatePresent(string nssDbPath)
        => RunProcess(
            "certutil",
            ["-d", $"sql:{nssDbPath}", "-L", "-n", CertificateAlias],
            TimeSpan.FromSeconds(5)).ExitCode == 0;

    private static void RemoveLinuxNssCertificate(string nssDbPath)
        => RunProcess(
            "certutil",
            ["-d", $"sql:{nssDbPath}", "-D", "-n", CertificateAlias],
            TimeSpan.FromSeconds(5));

    private static BridgeManagedDeliveryTrustDiagnostics TryInstallLinuxSystemTrust(byte[] certificateBytes)
    {
        var password = GetRootPassword();
        if (string.IsNullOrWhiteSpace(password))
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("linux-system-ca", "Не задан пароль root в переменных окружения");
        if (!IsCommandAvailable("sudo"))
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("linux-system-ca", "Утилита sudo недоступна");

        var certificatePath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-delivery-{Guid.NewGuid():N}.crt");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-delivery-{Guid.NewGuid():N}.sh");
        File.WriteAllBytes(certificatePath, certificateBytes);

        try
        {
            WriteLinuxSystemTrustScript(scriptPath, certificatePath);
            return RunLinuxSystemTrustScript(scriptPath, password);
        }
        catch (Exception ex)
        {
            Observe(ex);
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("linux-system-ca", ex.Message);
        }
        finally
        {
            TryDeleteFile(certificatePath);
            TryDeleteFile(scriptPath);
        }
    }

    private static BridgeManagedDeliveryTrustDiagnostics EnsureMacTrust(X509Certificate2 certificate)
    {
        var certificatePath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-delivery-{Guid.NewGuid():N}.cer");
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Cert));

        try
        {
            var loginKeychainPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Keychains",
                "login.keychain-db");
            var (exitCode, _, standardError) = RunProcess(
                "/usr/bin/security",
                ["add-trusted-cert", "-d", "-r", "trustAsRoot", "-p", "ssl", "-k", loginKeychainPath, certificatePath],
                TimeSpan.FromSeconds(10));
            if (exitCode == 0)
                return BridgeManagedDeliveryTrustDiagnostics.Trusted("macos-login-keychain", loginKeychainPath);

            var detail = string.IsNullOrWhiteSpace(standardError) ? "security add-trusted-cert failed" : standardError.Trim();
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("macos-login-keychain", detail);
        }
        catch (Exception ex)
        {
            Observe(ex);
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("macos-login-keychain", ex.Message);
        }
        finally
        {
            TryDeleteFile(certificatePath);
        }
    }

    private static bool ContainsCertificate(X509Store store, X509Certificate2 certificate)
        => store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false).Count > 0;

    private static bool IsCommandAvailable(string command)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        string[] executableNames = OperatingSystem.IsWindows()
            ? [command, $"{command}.exe", $"{command}.cmd", $"{command}.bat"]
            : [command];

        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var executableName in executableNames)
            {
                try
                {
                    if (File.Exists(Path.Combine(segment, executableName)))
                        return true;
                }
                catch (Exception ex)
                {
                    Observe(ex);
                    // Ignore malformed PATH segments.
                }
            }
        }

        return false;
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunProcess(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return (-1, string.Empty, string.Empty);

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Observe(ex);
                    // Ignore timeout cleanup failures.
                }

                return (-1, string.Empty, string.Empty);
            }

            return (process.ExitCode, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
        }
        catch (Exception ex)
        {
            Observe(ex);
            return (-1, string.Empty, string.Empty);
        }
    }

    private static void WriteLinuxSystemTrustScript(string scriptPath, string certificatePath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/sh");
        builder.AppendLine("set -e");
        builder.AppendLine("if command -v update-ca-trust >/dev/null 2>&1; then");
        builder.AppendLine("  mkdir -p /etc/ca-certificates/trust-source/anchors");
        builder.AppendLine($"  install -m 0644 '{EscapeForShell(certificatePath)}' '/etc/ca-certificates/trust-source/anchors/atom-webdriver-delivery.crt'");
        builder.AppendLine("  update-ca-trust extract >/dev/null 2>&1");
        builder.AppendLine("elif command -v update-ca-certificates >/dev/null 2>&1; then");
        builder.AppendLine("  mkdir -p /usr/local/share/ca-certificates");
        builder.AppendLine($"  install -m 0644 '{EscapeForShell(certificatePath)}' '/usr/local/share/ca-certificates/atom-webdriver-delivery.crt'");
        builder.AppendLine("  update-ca-certificates >/dev/null 2>&1");
        builder.AppendLine("else");
        builder.AppendLine("  exit 1");
        builder.AppendLine("fi");
        File.WriteAllText(scriptPath, builder.ToString(), encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static BridgeManagedDeliveryTrustDiagnostics RunLinuxSystemTrustScript(string scriptPath, string password)
    {
        using var process = StartSudoProcess(scriptPath);
        if (process is null)
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("linux-system-ca", "Не удалось запустить sudo процесс");

        process.StandardInput.WriteLine(password);
        process.StandardInput.Flush();
        process.StandardInput.Close();

        if (!process.WaitForExit(milliseconds: 20000))
        {
            TryKillProcess(process);
            return BridgeManagedDeliveryTrustDiagnostics.BypassRequired("linux-system-ca", "Превышен таймаут обновления системного trust store");
        }

        return process.ExitCode == 0
            ? BridgeManagedDeliveryTrustDiagnostics.Trusted("linux-system-ca")
            : BridgeManagedDeliveryTrustDiagnostics.BypassRequired("linux-system-ca", process.StandardError.ReadToEnd().Trim());
    }

    private static Process? StartSudoProcess(string scriptPath)
    {
        var startInfo = new ProcessStartInfo("sudo")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-S");
        startInfo.ArgumentList.Add("/bin/sh");
        startInfo.ArgumentList.Add(scriptPath);
        return Process.Start(startInfo);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Observe(ex);
        }
    }

    private static string? GetRootPassword()
    {
        var password = Environment.GetEnvironmentVariable(RootPasswordEnv);
        if (!string.IsNullOrWhiteSpace(password))
            return password;

        password = Environment.GetEnvironmentVariable(LegacyRootPasswordEnv);
        return string.IsNullOrWhiteSpace(password) ? null : password;
    }

    private static string EscapeForShell(string value)
        => value.Replace("'", "'\\''", StringComparison.Ordinal);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Observe(ex);
            // Ignore best-effort cleanup failures.
        }
    }

    private static void Observe(Exception ex)
        => Trace.TraceWarning(ex.ToString());
}