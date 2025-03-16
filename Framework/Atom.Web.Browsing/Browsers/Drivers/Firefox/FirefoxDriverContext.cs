using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Atom.Web.Browsing.Drivers.Firefox;

/// <summary>
/// Представляет контекст драйвера браузера Mozilla Firefox.
/// </summary>
public partial class FirefoxDriverContext : WebDriverContext
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FirefoxDriverContext"/>.
    /// </summary>
    /// <param name="driver">Драйвер.</param>
    /// <param name="settings">Настройки контекста драйвера.</param>
    protected internal FirefoxDriverContext(IWebDriver driver, IWebDriverContextSettings settings) : base(driver, settings) { }

    /// <inheritdoc/>
    protected override void OnProcessDataReceived(object sender, [NotNull] DataReceivedEventArgs e)
    {
        base.OnProcessDataReceived(sender, e);
        if (string.IsNullOrEmpty(e.Data)) return;

        var websocketUrlMatcher = GetWebSocketUrlRegex();
        var regexMatch = websocketUrlMatcher.Match(e.Data);
        if (regexMatch.Success) Url = new Uri(regexMatch.Groups[1].Value);
    }

    /// <inheritdoc/>
    public override async ValueTask<IWebWindow> OpenWindowAsync(IWebWindowSettings settings, CancellationToken cancellationToken)
    {
        if (!Windows.Any())
        {
            var contexts = await BiDi.GetUserContextsAsync().ConfigureAwait(false);
            ;
        }

        return await base.OpenWindowAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override ValueTask<IWebWindow> OpenWindowAsync(CancellationToken cancellationToken) => OpenWindowAsync(FirefoxWindowSettings.CreateFrom<FirefoxDriverContextSettings, FirefoxWindowSettings>((FirefoxDriverContextSettings)Settings), cancellationToken);

    [GeneratedRegex(@"WebDriver BiDi listening on (ws:\/\/.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GetWebSocketUrlRegex();
}