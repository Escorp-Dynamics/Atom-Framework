using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Обеспечивает наличие бинарного файла Obscura: берёт его из кэша либо скачивает закреплённый релиз.
/// </summary>
/// <remarks>
/// Архив скачивается во временный файл, сверяется с SHA-256 из <see cref="ObscuraRelease"/> и только после
/// успешной проверки распаковывается во временный каталог, который затем атомарно переименовывается
/// в каталог кэша. Поэтому частично скачанная или подменённая сборка в кэш не попадает.
/// </remarks>
internal static class ObscuraBinaryProvisioner
{
    /// <summary>Переменная окружения с явным путём к каталогу кэша.</summary>
    public const string CacheDirectoryVariableName = "ATOM_WEBDRIVER_OBSCURA_CACHE_DIR";

    private static readonly SemaphoreSlim ProvisionGate = new(1, 1);

    /// <summary>
    /// Каталог кэша сборок Obscura по умолчанию.
    /// </summary>
    public static string GetCacheRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(CacheDirectoryVariableName);
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden.Trim();

        return IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atom-webdriver", "obscura");
    }

    /// <summary>
    /// Путь, по которому лежит (или будет лежать) бинарный файл варианта в кэше.
    /// </summary>
    public static string GetCachedExecutablePath(ObscuraBuildVariant variant)
        => GetCachedExecutablePath(ObscuraRelease.ResolveAsset(variant), GetCacheRoot());

    internal static string GetCachedExecutablePath(ObscuraReleaseAsset asset, string cacheRoot)
        => IOPath.Combine(GetInstallDirectory(asset, cacheRoot), asset.ExecutableName);

    /// <summary>
    /// Возвращает путь к бинарному файлу варианта, при необходимости скачивая и проверяя релиз.
    /// </summary>
    public static ValueTask<string> EnsureAsync(ObscuraBuildVariant variant, CancellationToken cancellationToken)
        => EnsureAsync(ObscuraRelease.ResolveAsset(variant), GetCacheRoot(), DownloadAsync, cancellationToken);

    internal static async ValueTask<string> EnsureAsync(
        ObscuraReleaseAsset asset,
        string cacheRoot,
        Func<Uri, Stream, CancellationToken, ValueTask> download,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        ArgumentNullException.ThrowIfNull(download);

        var executablePath = GetCachedExecutablePath(asset, cacheRoot);
        if (File.Exists(executablePath))
            return executablePath;

        await ProvisionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(executablePath))
                return executablePath;

            Directory.CreateDirectory(cacheRoot);
            var installDirectory = GetInstallDirectory(asset, cacheRoot);
            var stagingDirectory = IOPath.Combine(cacheRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            var archivePath = stagingDirectory + ".download";

            try
            {
                var archive = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
                await using (archive.ConfigureAwait(false))
                {
                    await download(asset.DownloadUrl, archive, cancellationToken).ConfigureAwait(false);
                    archive.Position = 0;
                    await VerifySha256Async(archive, asset, cancellationToken).ConfigureAwait(false);
                    archive.Position = 0;

                    Directory.CreateDirectory(stagingDirectory);
                    await ExtractAsync(archive, asset.ArchiveKind, stagingDirectory, cancellationToken).ConfigureAwait(false);
                }

                var stagedExecutable = IOPath.Combine(stagingDirectory, asset.ExecutableName);
                if (!File.Exists(stagedExecutable))
                    throw new InvalidDataException($"Архив '{asset.Name}' не содержит ожидаемый файл '{asset.ExecutableName}'");

                EnsureExecutable(stagingDirectory);

                if (Directory.Exists(installDirectory))
                    Directory.Delete(installDirectory, recursive: true);

                Directory.CreateDirectory(IOPath.GetDirectoryName(installDirectory)!);
                Directory.Move(stagingDirectory, installDirectory);
                return executablePath;
            }
            finally
            {
                TryDeleteFile(archivePath);
                TryDeleteDirectory(stagingDirectory);
            }
        }
        finally
        {
            ProvisionGate.Release();
        }
    }

    internal static async ValueTask VerifySha256Async(Stream archive, ObscuraReleaseAsset asset, CancellationToken cancellationToken)
    {
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Контрольная сумма архива '{asset.Name}' не совпала: ожидалась {asset.Sha256}, получена {actual}. Архив отброшен.");
        }
    }

    private static async ValueTask ExtractAsync(Stream archive, ObscuraArchiveKind kind, string destination, CancellationToken cancellationToken)
    {
        if (kind == ObscuraArchiveKind.Zip)
        {
            await ZipFile.ExtractToDirectoryAsync(archive, destination, overwriteFiles: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
        await using (gzip.ConfigureAwait(false))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, destination, overwriteFiles: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnsureExecutable(string directory)
    {
        if (OperatingSystem.IsWindows())
            return;

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var mode = File.GetUnixFileMode(file);
            File.SetUnixFileMode(file, mode | UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
    }

    private static string GetInstallDirectory(ObscuraReleaseAsset asset, string cacheRoot)
    {
        var name = asset.Name;
        foreach (var extension in (ReadOnlySpan<string>)[".tar.gz", ".zip"])
        {
            if (name.EndsWith(extension, StringComparison.Ordinal))
            {
                name = name[..^extension.Length];
                break;
            }
        }

        return IOPath.Combine(cacheRoot, ObscuraRelease.Version, name);
    }

    private static async ValueTask DownloadAsync(Uri url, Stream destination, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Временный файл подберёт следующая попытка; на результат это не влияет.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Временный каталог подберёт следующая попытка; на результат это не влияет.
        }
    }
}
