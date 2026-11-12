namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет профиль headless-движка Obscura, управляемого через протокол отладки Chrome (CDP).
/// </summary>
/// <remarks>
/// Бинарный файл ищется по явному пути, затем в переменной окружения
/// <see cref="BinaryPathVariableName"/>, затем в PATH и, наконец, в кэше драйвера. Если ни один путь
/// не найден, <see cref="IsInstalled"/> равен <see langword="false"/>, и при запуске закреплённый
/// релиз скачивается в кэш с проверкой SHA-256.
/// <para>Obscura не поддерживает расширения, поэтому драйвер управляет ею напрямую по CDP. Не
/// поддерживаются: перехват ответов, замена и отмена вызовов в callback'ах (решение
/// обработчика игнорируется, исходная функция выполняется как есть), реальные границы окна
/// (окно совпадает с областью просмотра) и ОС-ввод — щелчки и клавиши доставляются самим
/// движком как доверенные события. Прокси, часовой пояс и геолокация действуют на процесс,
/// поэтому окна с разными значениями получают отдельные процессы <c lang="text">obscura serve</c>.</para>
/// </remarks>
public sealed class ObscuraProfile : WebBrowserProfile
{
    /// <summary>Переменная окружения с явным путём к бинарному файлу <c lang="text">obscura</c>.</summary>
    public const string BinaryPathVariableName = "ATOM_WEBDRIVER_OBSCURA_PATH";

    /// <summary>
    /// Инициализирует профиль Obscura с указанным бинарным файлом.
    /// </summary>
    public ObscuraProfile(string binaryPath)
        : base(binaryPath)
    {
    }

    /// <summary>
    /// Инициализирует профиль Obscura для заданного варианта сборки с автоматическим поиском бинарного файла.
    /// </summary>
    public ObscuraProfile(ObscuraBuildVariant variant)
        : base(ResolveDefaultBinaryPath(variant))
        => Variant = variant;

    /// <summary>
    /// Инициализирует профиль Obscura со сборкой по умолчанию.
    /// </summary>
    public ObscuraProfile()
        : this(ObscuraBuildVariant.Default)
    {
    }

    /// <summary>
    /// Вариант сборки, который скачивается, если бинарный файл не найден.
    /// </summary>
    public ObscuraBuildVariant Variant { get; init; }

    /// <summary>
    /// Включает stealth-режим Obscura (<c lang="text">--stealth</c>): согласованный отпечаток браузера,
    /// а в stealth-сборках — ещё и TLS-имперсонацию с блокировкой трекеров.
    /// </summary>
    public bool UseStealth { get; init; }

    /// <summary>
    /// Разрешает обращения к loopback- и частным адресам (<c lang="text">--allow-private-network</c>).
    /// По умолчанию Obscura блокирует их как защиту от SSRF.
    /// </summary>
    public bool AllowPrivateNetwork { get; init; }

    /// <summary>
    /// Часовой пояс процесса (IANA, например <c lang="text">Europe/Moscow</c>). Действует на весь процесс.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>
    /// Координаты для <c lang="text">navigator.geolocation</c>. Действуют на весь процесс.
    /// </summary>
    public (double Latitude, double Longitude)? Geolocation { get; init; }

    private static string ResolveDefaultBinaryPath(ObscuraBuildVariant variant)
    {
        var explicitPath = Environment.GetEnvironmentVariable(BinaryPathVariableName);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath.Trim();

        var fromPath = ResolveInstalledBinary(["obscura"]);
        if (!string.IsNullOrWhiteSpace(fromPath))
            return fromPath;

        try
        {
            return ObscuraBinaryProvisioner.GetCachedExecutablePath(variant);
        }
        catch (PlatformNotSupportedException)
        {
            return string.Empty;
        }
    }
}
