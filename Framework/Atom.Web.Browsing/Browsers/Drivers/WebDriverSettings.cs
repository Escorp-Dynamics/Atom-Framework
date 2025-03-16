using System.Net;
using System.Net.Sockets;
using Atom.Web.Browsing.Drivers.Firefox;

namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет настройки драйвера веб-браузера.
/// </summary>
public abstract class WebDriverSettings : WebBrowserSettings, IWebDriverSettings
{
    private static readonly Lazy<FirefoxDriverSettings> defaultSettings = new(() => FirefoxDriverSettings.Default, true);

    /// <inheritdoc/>
    public string BinaryPath { get; set; }

    /// <inheritdoc/>
    public string UserDataPath { get; set; }

    /// <inheritdoc/>
    public int DebugPort { get; set; }

    /// <inheritdoc/>
    public WebDriverMode Mode { get; set; }

    /// <inheritdoc/>
    public IEnumerable<string> Arguments { get; set; }

    /// <inheritdoc/>
    public IUserProfile? Profile { get; set; }

    /// <inheritdoc/>
    public static new IWebDriverSettings Default => defaultSettings.Value;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverSettings"/>.
    /// </summary>
    protected WebDriverSettings()
    {
        BinaryPath = GetDefaultBinaryPathInternal();
        UserDataPath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-{Guid.NewGuid()}");
        DebugPort = FindFreePort();
        Mode = WebDriverMode.Default;
        Arguments = GetArguments();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverSettings"/>.
    /// </summary>
    /// <param name="profile">Настройки профиля браузера.</param>
    protected WebDriverSettings(IUserProfile profile) : this() => Profile = profile;

    private IEnumerable<string> GetArguments() => CreateArguments();

    private string GetDefaultBinaryPathInternal() => GetDefaultBinaryPath();

    /// <inheritdoc/>
    public abstract IEnumerable<string> CreateArguments();

    /// <inheritdoc/>
    public abstract string GetDefaultBinaryPath();

    /// <inheritdoc/>
    public virtual void Update()
    {
        UserDataPath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-{Guid.NewGuid()}");
        DebugPort = FindFreePort();
        Arguments = CreateArguments();
    }

    /// <summary>
    /// Возвращает первый найденный свободный порт.
    /// </summary>
    protected static int FindFreePort()
    {
        using var portSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        var socketEndPoint = new IPEndPoint(IPAddress.Any, 0);
        portSocket.Bind(socketEndPoint);

        return portSocket.LocalEndPoint is not IPEndPoint endPoint
            ? throw new InvalidOperationException("Не удалось найти свободный порт")
            : endPoint.Port;
    }
}