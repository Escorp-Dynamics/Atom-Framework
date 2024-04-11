using System.Text;

namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет профиль браузера Firefox.
/// </summary>
public class FirefoxProfile
{
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
        var path = System.IO.Path.Combine(Path, "prefs.js");
        if (File.Exists(path)) return;

        await File.WriteAllTextAsync(path, ToString(), cancellationToken).ConfigureAwait(false);

        var extensionsPath = System.IO.Path.Combine(Path, "extensions");
        if (!Directory.Exists(extensionsPath)) Directory.CreateDirectory(extensionsPath);
        
        extensionsPath = System.IO.Path.Combine(extensionsPath, "atom.xpi");

        if (!File.Exists(extensionsPath))
        {
            using var http = new HttpClient();
            var extension = await http.GetByteArrayAsync(new Uri("https://github.com/mozilla/gecko-dev/raw/master/browser/extensions/firefox/extension.xpi"), cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(extensionsPath, extension, cancellationToken).ConfigureAwait(false);
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