using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Atom.Web.Browsers.BOM;
using Atom.Web.Browsers.NativeMessaging;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет браузер.
/// </summary>
/// <typeparam name="TSettings">Тип настроек браузера.</typeparam>
/// <typeparam name="TServer">Тип сервера браузера.</typeparam>
public abstract class WebBrowser<TSettings, TServer> : IWebBrowser<TSettings, TServer>
    where TSettings : IWebBrowserSettings, new()
    where TServer : IWebBrowserServer, new()
{
    private readonly Process process;

    private static bool? isRunningAsAdmin;

    /// <inheritdoc/>
    public TServer Server { get; set; }

    /// <inheritdoc/>
    public TSettings Settings { get; init; }

    /// <inheritdoc/>
    public bool IsRunning { get; protected set; }

    /// <inheritdoc/>
    public bool IsRunningAsAdmin
    {
        get
        {
            if (isRunningAsAdmin.HasValue) return isRunningAsAdmin.Value;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return (isRunningAsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator)).Value;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return (isRunningAsAdmin = Environment.UserName is "root").Value;

            throw new InvalidOperationException("Неподдерживаемая платформа");
        }
    }

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowser<TSettings, TServer>, WebBrowserProcessAsyncEventArgs>? ProcessStarted;

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="WebBrowser{TSettings, TServer}"/>.
    /// </summary>
    /// <param name="settings">Настройки браузера.</param>
    protected WebBrowser(TSettings settings)
    {
        Settings = settings;
        process = new Process();
        Server = new TServer();
    }

    /// <summary>
    /// Запускает процесс браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    protected virtual async ValueTask OnProcessStarted(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        IsRunning = true;

        process.StartInfo = new ProcessStartInfo(Settings.GetNativeBinaryPath())
        {
            Arguments = Settings.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        await Server.StartAsync(cancellationToken).ConfigureAwait(false);
        process.Start();

        await ProcessStarted.On(this, new WebBrowserProcessAsyncEventArgs { CancellationToken = cancellationToken }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IWindow> OpenWindowAsync(TSettings settings, CancellationToken cancellationToken)
    {
        await OnProcessStarted(cancellationToken).ConfigureAwait(false);
        return new Window();
    }

    /// <inheritdoc/>
    public ValueTask<IWindow> OpenWindowAsync(TSettings settings) => OpenWindowAsync(settings, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<IWindow> OpenWindowAsync(CancellationToken cancellationToken) => OpenWindowAsync(Settings, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IWindow> OpenWindowAsync() => OpenWindowAsync(CancellationToken.None);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (IsRunning) process.Kill(true);
        process.Dispose();

        await Server.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}