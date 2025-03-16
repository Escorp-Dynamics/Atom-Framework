using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Atom.Threading;
using Atom.Web.Browsing.BiDi;

namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет контекст драйвера веб-браузера.
/// </summary>
public class WebDriverContext : WebBrowserContext, IWebDriverContext
{
    private readonly IWebDriver driver;
    private readonly IWebDriverContextSettings settings;

    private Process? process;
    private Uri? url;

    /// <summary>
    /// Ссылка для подключения к браузеру.
    /// </summary>
    public Uri Url
    {
        get
        {
            ArgumentNullException.ThrowIfNull(url);
            return url;
        }

        protected set => url = value;
    }

    /// <inheritdoc/>
    public BiDiDriver BiDi { get; protected set; } = new();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverContext"/>.
    /// </summary>
    /// <param name="driver">Драйвер веб-браузера.</param>
    /// <param name="settings">Настройки контекста.</param>
    protected internal WebDriverContext(IWebDriver driver, IWebDriverContextSettings settings) : base(driver, settings)
    {
        Browser = this.driver = driver;
        this.settings = settings;
    }

    private Process CreateProcess()
    {
        if (!driver.IsInstalled) throw new InvalidOperationException("Браузер не установлен");

        var p = new Process
        {
            StartInfo = new ProcessStartInfo(settings.BinaryPath, settings.Arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        p.ErrorDataReceived += OnProcessDataReceived;
        p.OutputDataReceived += OnProcessDataReceived;

        return p;
    }

    /// <summary>
    /// Происходит в момент получения данных от процесса браузера.
    /// </summary>
    /// <param name="sender">Источник события.</param>
    /// <param name="e">Аргументы события.</param>
    protected virtual void OnProcessDataReceived(object sender, [NotNull] DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data)) settings.Logger?.BrowserProcessTrace(e.Data);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        BiDi.Dispose();
        process?.Dispose();

        if (Directory.Exists(settings.UserDataPath)) Directory.Delete(settings.UserDataPath, true);
    }

    /// <inheritdoc/>
    public virtual async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        process = CreateProcess();

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        settings.Logger?.BrowserProcessRunning();

        await Wait.UntilAsync(() => url is null && !process.HasExited, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

        if (process.HasExited) throw new WebDriverException("Не удалось запустить процесс браузера");
        if (url is null) throw new WebDriverException("Не удалось подключиться к процессу браузера");

        await BiDi.StartAsync(url).ConfigureAwait(false);
        settings.Logger?.SessionConnected(url);
    }

    /// <inheritdoc/>
    public ValueTask ConnectAsync() => ConnectAsync(CancellationToken.None);

    /// <inheritdoc/>
    public override async ValueTask DestroyAsync(CancellationToken cancellationToken)
    {
        if (IsDestroyed) return;

        await BiDi.StopAsync().ConfigureAwait(false);
        await base.DestroyAsync(cancellationToken).ConfigureAwait(false);

        if (process?.HasExited is false) process.Kill();
    }
}