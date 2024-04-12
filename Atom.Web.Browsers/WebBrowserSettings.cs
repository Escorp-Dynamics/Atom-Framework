namespace Atom.Web.Browsers;

/// <summary>
/// Представляет настройки браузера.
/// </summary>
public abstract class WebBrowserSettings : IWebBrowserSettings
{
    /// <inheritdoc/>
    public string BinaryPath { get; init; }

    /// <inheritdoc/>
    public string DistributionPath { get; init; }

    /// <inheritdoc/>
    public string? AdminPassword { get; init; }

    /// <inheritdoc/>
    public bool IsHeadless { get; init; }

    /// <inheritdoc/>
    public bool IsIncognito { get; init; }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="WebBrowserSettings"/>.
    /// </summary>
    /// <param name="binaryPath">Путь к исполняемому файлу браузера.</param>
    /// <param name="distributionPath">Путь к дистрибутиву браузера.</param>
    protected WebBrowserSettings(string binaryPath, string distributionPath) => (BinaryPath, DistributionPath) = (binaryPath, distributionPath);

    /// <inheritdoc/>
    public abstract string GetNativeBinaryPath();

    /// <inheritdoc/>
    public abstract string GetNativeDistributionPath();
}