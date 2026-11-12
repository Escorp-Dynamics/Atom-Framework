using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Проверяет выбор архива релиза, проверку SHA-256, распаковку в кэш и параметры запуска Obscura.
/// Сеть не используется: загрузка подменяется локальным архивом.
/// </summary>
[TestFixture]
public sealed class WebDriverObscuraProvisioningTests
{
    private string cacheRoot = null!;

    [SetUp]
    public void SetUp()
        => cacheRoot = Path.Combine(Path.GetTempPath(), "atom-obscura-tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(cacheRoot))
            Directory.Delete(cacheRoot, recursive: true);
    }

    [TestCase(ObscuraBuildVariant.Default, "linux", "obscura-x86_64-linux.tar.gz", "eed986e478c5ec5439f531f2d7fb6b0e36a356f1df7a6c44a8e8fd4f2486bd76")]
    [TestCase(ObscuraBuildVariant.Stealth, "linux", "obscura-x86_64-linux-stealth.tar.gz", "ac8057374ec1b5cabb78ea2d08b2ed890d21d156a42b1ac23357fd049edc08c5")]
    [TestCase(ObscuraBuildVariant.NoRenderStealth, "macos", "obscura-x86_64-macos-no-render-stealth.tar.gz", "7c5f9cba96a436fef36a85d919d89f8edce6350d724829c31c6c74985e47767c")]
    [TestCase(ObscuraBuildVariant.Default, "windows", "obscura-x86_64-windows.zip", "0a110d0693753a252ed7ffd61e682363e8622caf79697f564c529099a31a6242")]
    public void ResolveAssetPicksPinnedArchiveAndChecksum(ObscuraBuildVariant variant, string os, string expectedName, string expectedSha256)
    {
        var asset = ObscuraRelease.ResolveAsset(variant, Architecture.X64, os);

        Assert.Multiple(() =>
        {
            Assert.That(asset.Name, Is.EqualTo(expectedName));
            Assert.That(asset.Sha256, Is.EqualTo(expectedSha256));
            Assert.That(asset.DownloadUrl.AbsoluteUri, Is.EqualTo($"https://github.com/h4ckf0r0day/obscura/releases/download/v{ObscuraRelease.Version}/{expectedName}"));
            Assert.That(asset.ExecutableName, Is.EqualTo(os == "windows" ? "obscura.exe" : "obscura"));
        });
    }

    [Test]
    public void ResolveAssetRejectsUnpublishedPlatform()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<PlatformNotSupportedException>(() => ObscuraRelease.ResolveAsset(ObscuraBuildVariant.Default, Architecture.Arm64, "windows"));
            Assert.Throws<PlatformNotSupportedException>(() => ObscuraRelease.ResolveAsset(ObscuraBuildVariant.Default, Architecture.X86, "linux"));
        });
    }

    [Test]
    public async Task EnsureAsyncExtractsVerifiedArchiveAndReusesCache()
    {
        var archive = CreateTarGz(("obscura", "#!/bin/sh\necho obscura\n"), ("obscura-worker", "worker"));
        var asset = CreateAsset(archive, ObscuraArchiveKind.TarGz, "obscura");
        var downloads = 0;

        var path = await ObscuraBinaryProvisioner.EnsureAsync(asset, cacheRoot, Serve(archive, () => downloads++), CancellationToken.None).ConfigureAwait(false);
        var again = await ObscuraBinaryProvisioner.EnsureAsync(asset, cacheRoot, Serve(archive, () => downloads++), CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(again));
            Assert.That(downloads, Is.EqualTo(1), "Повторный вызов обязан брать сборку из кэша");
            Assert.That(File.ReadAllText(path), Does.Contain("echo obscura"));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "obscura-worker")), Is.True);
            Assert.That(path, Does.StartWith(Path.Combine(cacheRoot, ObscuraRelease.Version)));
            Assert.That(Directory.EnumerateFileSystemEntries(cacheRoot).Where(entry => Path.GetFileName(entry).StartsWith(".staging", StringComparison.Ordinal)), Is.Empty);
        });

        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute), Is.True);
    }

    [Test]
    public async Task EnsureAsyncExtractsZipArchive()
    {
        var archive = CreateZip(("obscura.exe", "MZ"));
        var asset = CreateAsset(archive, ObscuraArchiveKind.Zip, "obscura.exe");

        var path = await ObscuraBinaryProvisioner.EnsureAsync(asset, cacheRoot, Serve(archive), CancellationToken.None).ConfigureAwait(false);

        Assert.That(File.ReadAllText(path), Is.EqualTo("MZ"));
    }

    [Test]
    public async Task EnsureAsyncRejectsTamperedArchiveAndLeavesNoCache()
    {
        var archive = CreateTarGz(("obscura", "genuine"));
        var asset = CreateAsset(archive, ObscuraArchiveKind.TarGz, "obscura");
        var tampered = CreateTarGz(("obscura", "malicious"));

        var exception = await AsyncAssert.ThrowsAsync<InvalidDataException>(async () =>
            await ObscuraBinaryProvisioner.EnsureAsync(asset, cacheRoot, Serve(tampered), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("Контрольная сумма"));
            Assert.That(File.Exists(ObscuraBinaryProvisioner.GetCachedExecutablePath(asset, cacheRoot)), Is.False);
            Assert.That(Directory.EnumerateFileSystemEntries(cacheRoot).Where(entry => !Path.GetFileName(entry).Equals(ObscuraRelease.Version, StringComparison.Ordinal)), Is.Empty);
        });
    }

    [Test]
    public async Task EnsureAsyncFailsWhenArchiveLacksExecutable()
    {
        var archive = CreateTarGz(("README", "no binary here"));
        var asset = CreateAsset(archive, ObscuraArchiveKind.TarGz, "obscura");

        var exception = await AsyncAssert.ThrowsAsync<InvalidDataException>(async () =>
            await ObscuraBinaryProvisioner.EnsureAsync(asset, cacheRoot, Serve(archive), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.That(exception.Message, Does.Contain("obscura"));
    }

    [Test]
    public void ParseReadyFileBuildsBrowserEndpoint()
    {
        var endpoint = ObscuraServer.ParseReadyFile("""{"pid":117036,"host":"127.0.0.1","port":37417,"websocket_path":"/devtools/browser"}""");

        Assert.That(endpoint, Is.EqualTo(new Uri("ws://127.0.0.1:37417/devtools/browser")));
    }

    [TestCase("""{"pid":1,"host":"127.0.0.1","port":0,"websocket_path":"/devtools/browser"}""")]
    [TestCase("""{"pid":1,"port":9222,"websocket_path":"/devtools/browser"}""")]
    [TestCase("""{"pid":1,"host":"127.0.0.1","port":9222}""")]
    public void ParseReadyFileRejectsIncompleteRecord(string json)
        => Assert.Throws<InvalidDataException>(() => ObscuraServer.ParseReadyFile(json));

    [Test]
    public void BuildStartInfoKeepsSecretsOutOfCommandLine()
    {
        var options = new ObscuraServerOptions
        {
            UseStealth = true,
            AllowPrivateNetwork = true,
            UserAgent = "Custom/1.0",
            ProxyUrl = "http://user:p%40ss@proxy.example.com:8080",
            TimeZone = "Europe/Moscow",
            Geolocation = (55.75, 37.62),
        };

        var startInfo = ObscuraServer.BuildStartInfo("/opt/obscura", options, "/tmp/run/ready.json", "/tmp/run", "token-123");
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Is.EqualTo(new[]
            {
                "serve", "--host", "127.0.0.1", "--port", "0", "--ready-file", "/tmp/run/ready.json", "--quiet",
                "--stealth", "--allow-private-network", "--user-agent", "Custom/1.0",
            }));
            Assert.That(string.Join(' ', arguments), Does.Not.Contain("token-123").And.Not.Contain("p%40ss"));
            Assert.That(startInfo.Environment["OBSCURA_CDP_TOKEN"], Is.EqualTo("token-123"));
            Assert.That(startInfo.Environment["OBSCURA_PROXY"], Is.EqualTo("http://user:p%40ss@proxy.example.com:8080"));
            Assert.That(startInfo.Environment["OBSCURA_TIMEZONE"], Is.EqualTo("Europe/Moscow"));
            Assert.That(startInfo.Environment["OBSCURA_GEOLOCATION"], Is.EqualTo("55.75,37.62"));
        });
    }

    [Test]
    public void ResolveProxyUrlEmbedsEscapedCredentials()
    {
        var proxy = new WebProxy("http://proxy.example.com:8080") { Credentials = new NetworkCredential("user", "p@ss") };

        Assert.Multiple(() =>
        {
            Assert.That(ObscuraServer.ResolveProxyUrl(proxy), Is.EqualTo("http://user:p%40ss@proxy.example.com:8080"));
            Assert.That(ObscuraServer.ResolveProxyUrl(new WebProxy("socks5://127.0.0.1:1080")), Is.EqualTo("socks5://127.0.0.1:1080"));
            Assert.That(ObscuraServer.ResolveProxyUrl(null), Is.Null);
        });
    }

    private static ObscuraReleaseAsset CreateAsset(byte[] archive, ObscuraArchiveKind kind, string executableName)
        => new(
            kind == ObscuraArchiveKind.Zip ? "obscura-test.zip" : "obscura-test.tar.gz",
            new Uri("https://example.invalid/obscura"),
            Convert.ToHexStringLower(SHA256.HashData(archive)),
            kind,
            executableName);

    private static Func<Uri, Stream, CancellationToken, ValueTask> Serve(byte[] archive, Action? onDownload = null)
        => async (_, destination, cancellationToken) =>
        {
            onDownload?.Invoke();
            await destination.WriteAsync(archive, cancellationToken).ConfigureAwait(false);
        };

    private static byte[] CreateTarGz(params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                };
                writer.WriteEntry(entry);
            }
        }

        return output.ToArray();
    }

    private static byte[] CreateZip(params (string Name, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return output.ToArray();
    }
}
