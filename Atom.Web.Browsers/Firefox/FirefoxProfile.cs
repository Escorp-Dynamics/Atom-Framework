using System.Text;
using Atom.Net.Http;

namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет профиль браузера Firefox.
/// </summary>
public class FirefoxProfile
{
    private static readonly SemaphoreSlim locker = new(1, 1);
    private static bool isExtensionDownloaded;

    /// <summary>
    /// Путь к файлу профиля.
    /// </summary>
    /// <value></value>
    public string Path { get; set; }

    /// <summary>
    /// Включает или отключает DOM Storage.
    /// </summary>
    public bool IsDomStorageEnabled { get; set; }

    /// <summary>
    /// Создает новый экземпляр класса <see cref="FirefoxProfile"/>.
    /// </summary>
    public FirefoxProfile() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

    /// <summary>
    /// Профиль браузера Firefox по умолчанию.
    /// </summary>
    public static FirefoxProfile Default => new()
    {
        IsDomStorageEnabled = true,
    };

    /// <summary>
    /// Сохраняет профиль браузера Firefox в файл.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path)) Directory.CreateDirectory(Path);

        var path = System.IO.Path.Combine(Path, "prefs.js");
        if (File.Exists(path)) return;

        await File.WriteAllTextAsync(path, ToString(), cancellationToken).ConfigureAwait(false);

        var extensionsPath = System.IO.Path.Combine(Environment.CurrentDirectory, "atom@escorp.dynamics.xpi");

        if (!File.Exists(extensionsPath))
        {
            await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (!isExtensionDownloaded)
            {
                using var http = new SafetyHttpClient();
                using var response = await http.GetAsync(new Uri("https://gitflic.ru/project/escorp-lab/atom/blob/raw?file=Atom.Web.Browsers/Firefox/Extension/atom.xpi?inline=false"), cancellationToken).ConfigureAwait(false);
                var extension = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(extensionsPath, extension, cancellationToken).ConfigureAwait(false);
                isExtensionDownloaded = true;
            }

            locker.Release();
        }
    }

    /// <summary>
    /// Сохраняет профиль браузера Firefox в файл.
    /// </summary>
    public ValueTask SaveAsync() => SaveAsync(CancellationToken.None);

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="FirefoxProfile"/> в строку аргументов.
    /// </summary>
    /// <returns>Строка аргументов.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append($"user_pref(\"dom.storage.enabled\", {IsDomStorageEnabled});");

        return sb.ToString();
    }
}