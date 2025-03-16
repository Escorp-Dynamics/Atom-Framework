namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовую реализацию драйвера веб-браузера.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="WebDriver"/>.
/// </remarks>
/// <param name="settings">Настройки драйвера.</param>
public abstract partial class WebDriver(IWebDriverSettings settings) : WebBrowser(settings), IWebDriver
{
    /// <inheritdoc/>
    public bool IsInstalled => Settings is IWebDriverSettings wdSettings && File.Exists(wdSettings.BinaryPath);

    /// <inheritdoc/>
    public bool IsRunning => Contexts.Any();
}