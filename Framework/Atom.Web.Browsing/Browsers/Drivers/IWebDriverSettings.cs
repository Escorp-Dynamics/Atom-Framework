namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для реализации настроек драйвера веб-браузера.
/// </summary>
public interface IWebDriverSettings : IWebBrowserSettings
{
    /// <summary>
    /// Путь к исполняемому файлу браузера.
    /// </summary>
    string BinaryPath { get; set; }

    /// <summary>
    /// Путь к пользовательским данным.
    /// </summary>
    string UserDataPath { get; set; }

    /// <summary>
    /// Номер порта отладки.
    /// </summary>
    int DebugPort { get; set; }

    /// <summary>
    /// Режим работы браузера.
    /// </summary>
    WebDriverMode Mode { get; set; }

    /// <summary>
    /// Аргументы запуска процесса браузера.
    /// </summary>
    IEnumerable<string> Arguments { get; set; }

    /// <summary>
    /// Профиль настроек браузера.
    /// </summary>
    IUserProfile? Profile { get; set; }

    /// <summary>
    /// Настройки драйвера по умолчанию.
    /// </summary>
    static abstract new IWebDriverSettings Default { get; }

    /// <summary>
    /// Создаёт аргументы запуска процесса браузера.
    /// </summary>
    IEnumerable<string> CreateArguments();

    /// <summary>
    /// Возвращает путь к установленному браузеру по умолчанию.
    /// </summary>
    string GetDefaultBinaryPath();

    /// <summary>
    /// Обновляет номер порта отладки, путь к данным пользователя, а так же аргументы запуска процесса браузера.
    /// </summary>
    void Update();
}