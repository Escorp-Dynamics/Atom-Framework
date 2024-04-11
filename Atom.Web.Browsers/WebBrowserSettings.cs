namespace Atom.Web.Browsers;

/// <summary>
/// Представляет настройки браузера.
/// </summary>
public abstract class WebBrowserSettings : IWebBrowserSettings
{
    /// <inheritdoc/>
    public string BinaryPath { get; init; }

    /// <inheritdoc/>
    public bool IsHeadless { get; init; }

    /// <inheritdoc/>
    public bool IsIncognito { get; init; }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="WebBrowserSettings"/>.
    /// </summary>
    /// <param name="binaryPath">Путь к исполняемому файлу.</param>
    protected WebBrowserSettings(string binaryPath) => BinaryPath = binaryPath;
}