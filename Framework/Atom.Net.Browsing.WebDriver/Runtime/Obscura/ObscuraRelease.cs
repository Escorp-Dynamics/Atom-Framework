using System.Runtime.InteropServices;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Закреплённый релиз Obscura и контрольные суммы его архивов.
/// </summary>
/// <remarks>
/// Скачивание идёт только с официальных GitHub Releases, и каждый архив сверяется с SHA-256,
/// опубликованным для этого релиза. Смена версии — это осознанное обновление таблицы ниже.
/// </remarks>
internal static class ObscuraRelease
{
    public const string Version = "0.2.6";

    private const string DownloadRoot = "https://github.com/h4ckf0r0day/obscura/releases/download/v" + Version + "/";

    // Значения сняты с поля digest ассетов релиза v0.2.6 (GitHub Releases API).
    private static readonly Dictionary<string, string> Sha256ByAsset = new(StringComparer.Ordinal)
    {
        ["obscura-aarch64-linux-no-render-stealth.tar.gz"] = "9680c736db45fc7cad067cd20ea04ed032ab4701337fb7029c81bd2a6b9a8c79",
        ["obscura-aarch64-linux-no-render.tar.gz"] = "5bd8a40e4ff9b35ae985e695f2445181b6d40a115c447f3bd2013fb7b77aeb69",
        ["obscura-aarch64-linux-stealth.tar.gz"] = "403b4c8e3bcf777748a870fbb32c9281e997a6c30c5f8377c17305287fc3811f",
        ["obscura-aarch64-linux.tar.gz"] = "e12aff8bbe40326386a471060083e9cec5255b67048996b84f649efef10cac39",
        ["obscura-aarch64-macos-no-render-stealth.tar.gz"] = "7eb66d8ad3bb8af2ff8cbec0ff2de91befd524a33e724c3397005613c2e49216",
        ["obscura-aarch64-macos-no-render.tar.gz"] = "743fb360e1a815020cd34bdd3c879cefe9221ae3169168d2f8bbb571d0f8baa5",
        ["obscura-aarch64-macos-stealth.tar.gz"] = "01a29bb9eb3a7eda89c036db187bbdd9a01b76d3d8e4860117c00fc881ac40bf",
        ["obscura-aarch64-macos.tar.gz"] = "19faa248ac762f466ddb822dbb03ed4f097312d10d1a4d5b2143e6040a9bebd0",
        ["obscura-x86_64-linux-no-render-stealth.tar.gz"] = "2b36b2d68d31d4430ccddd3c0d8a765a21bf91fb3c642fd9e551f360a98d6c83",
        ["obscura-x86_64-linux-no-render.tar.gz"] = "f1188c1a3b1ef70d56d594beebbb34b693ff89c280fa073820ad019ed555a02c",
        ["obscura-x86_64-linux-stealth.tar.gz"] = "ac8057374ec1b5cabb78ea2d08b2ed890d21d156a42b1ac23357fd049edc08c5",
        ["obscura-x86_64-linux.tar.gz"] = "eed986e478c5ec5439f531f2d7fb6b0e36a356f1df7a6c44a8e8fd4f2486bd76",
        ["obscura-x86_64-macos-no-render-stealth.tar.gz"] = "7c5f9cba96a436fef36a85d919d89f8edce6350d724829c31c6c74985e47767c",
        ["obscura-x86_64-macos-no-render.tar.gz"] = "90feb7c7ab44f66343dae3d8f2c1252e5ef44560ccc997396f16658d27e53da0",
        ["obscura-x86_64-macos-stealth.tar.gz"] = "23ececed3162e337f4cce97eb3c69611b253fc32f383753bee40f817a1220953",
        ["obscura-x86_64-macos.tar.gz"] = "897f03df4e3d704f946c5e14c6a90a34e0d4e4ef898c4b3201771712962cd7fb",
        ["obscura-x86_64-windows-no-render-stealth.zip"] = "11bb447a7b4b38e596cba8ec10754f46eb4dae55046b68cb09d82d4b2d4d32bf",
        ["obscura-x86_64-windows-no-render.zip"] = "a9b88f38eb1f1efdbbb0adf1f526140dbbd4866d01f35d3452f899f5eb7c4442",
        ["obscura-x86_64-windows-stealth.zip"] = "a32a26fc73fcc21850a67d9cd0d0cd7caf7b400a0e20aa91703126f0fc47c33f",
        ["obscura-x86_64-windows.zip"] = "0a110d0693753a252ed7ffd61e682363e8622caf79697f564c529099a31a6242",
    };

    /// <summary>
    /// Описание архива под текущую платформу.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Для платформы нет официальной сборки.</exception>
    public static ObscuraReleaseAsset ResolveAsset(ObscuraBuildVariant variant)
        => ResolveAsset(variant, RuntimeInformation.OSArchitecture, ResolveOperatingSystem());

    internal static ObscuraReleaseAsset ResolveAsset(ObscuraBuildVariant variant, Architecture architecture, string operatingSystem)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException($"Obscura не публикует сборку для архитектуры {architecture}"),
        };

        var suffix = variant switch
        {
            ObscuraBuildVariant.Default => string.Empty,
            ObscuraBuildVariant.Stealth => "-stealth",
            ObscuraBuildVariant.NoRender => "-no-render",
            ObscuraBuildVariant.NoRenderStealth => "-no-render-stealth",
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, message: null),
        };

        var isWindows = string.Equals(operatingSystem, "windows", StringComparison.Ordinal);
        var name = string.Concat("obscura-", arch, "-", operatingSystem, suffix, isWindows ? ".zip" : ".tar.gz");

        if (!Sha256ByAsset.TryGetValue(name, out var sha256))
            throw new PlatformNotSupportedException($"Obscura {Version} не публикует архив '{name}' для этой платформы");

        return new ObscuraReleaseAsset(
            name,
            new Uri(DownloadRoot + name),
            sha256,
            isWindows ? ObscuraArchiveKind.Zip : ObscuraArchiveKind.TarGz,
            isWindows ? "obscura.exe" : "obscura");
    }

    private static string ResolveOperatingSystem()
    {
        if (OperatingSystem.IsLinux())
            return "linux";

        if (OperatingSystem.IsMacOS())
            return "macos";

        if (OperatingSystem.IsWindows())
            return "windows";

        throw new PlatformNotSupportedException("Obscura публикует сборки только для Linux, macOS и Windows");
    }
}

/// <summary>Формат архива релиза.</summary>
internal enum ObscuraArchiveKind
{
    TarGz,
    Zip,
}

/// <summary>Архив релиза Obscura для конкретной платформы.</summary>
internal sealed record ObscuraReleaseAsset(
    string Name,
    Uri DownloadUrl,
    string Sha256,
    ObscuraArchiveKind ArchiveKind,
    string ExecutableName);
