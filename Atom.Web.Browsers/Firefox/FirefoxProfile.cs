using System.Text;

namespace Atom.Web.Browsers.Firefox;

/// <summary>
/// Представляет профиль браузера Firefox.
/// </summary>
public class FirefoxProfile
{
    private readonly SortedDictionary<string, object?> preferences = new(StringComparer.InvariantCultureIgnoreCase);

    /// <summary>
    /// Путь к каталогу профиля.
    /// </summary>
    /// <value></value>
    public string Path { get; set; }

    /// <summary>
    /// Определяет, будут ли файлы профиля сохраняться на диске при закрытии браузера.
    /// </summary>
    public bool IsPersistent { get; set; }

    /// <summary>
    /// Включает или отключает DOM Storage.
    /// </summary>
    public bool IsDomStorageEnabled { get; set; }

    /// <summary>
    /// Создает новый экземпляр класса <see cref="FirefoxProfile"/>.
    /// </summary>
    public FirefoxProfile()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        if (!Directory.Exists(Path)) Directory.CreateDirectory(Path);
    }

    /// <summary>
    /// Профиль браузера Firefox по умолчанию.
    /// </summary>
    public static FirefoxProfile Default => new()
    {
        IsDomStorageEnabled = true,
    };

    /// <summary>
    /// Устанавливает параметр профиля.
    /// </summary>
    /// <param name="key">Ключ параметра.</param>
    /// <param name="value">Значение параметра.</param>
    /// <returns>Текущий экземпляр профиля.</returns>
    public FirefoxProfile UsePreference(string key, bool value)
    {
        preferences[key] = value;
        return this;
    }

    /// <summary>
    /// Устанавливает параметр профиля.
    /// </summary>
    /// <param name="key">Ключ параметра.</param>
    /// <param name="value">Значение параметра.</param>
    /// <returns>Текущий экземпляр профиля.</returns>
    public FirefoxProfile UsePreference(string key, int value)
    {
        preferences[key] = value;
        return this;
    }

    /// <summary>
    /// Устанавливает параметр профиля.
    /// </summary>
    /// <param name="key">Ключ параметра.</param>
    /// <param name="value">Значение параметра.</param>
    /// <returns>Текущий экземпляр профиля.</returns>
    public FirefoxProfile UsePreference(string key, string value)
    {
        preferences[key] = value;
        return this;
    }

    /// <summary>
    /// Преобразует текущий экземпляр <see cref="FirefoxProfile"/> в строку аргументов.
    /// </summary>
    /// <returns>Строка аргументов.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();

        foreach (var pref in preferences)
        {
            var value = pref.Value is int or bool ? pref.Value.ToString()!.ToLowerInvariant() : $"\"{pref.Value}\"";
            sb.AppendLine($"user_pref(\"{pref.Key}\", {value})");
        }

        return sb.ToString();
    }
}