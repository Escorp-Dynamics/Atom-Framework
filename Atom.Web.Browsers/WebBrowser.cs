using System.Diagnostics;
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

    /// <inheritdoc/>
    public TServer Server { get; set; }

    /// <inheritdoc/>
    public TSettings Settings { get; init; }

    /// <inheritdoc/>
    public bool IsRunning { get; protected set; }

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
    protected virtual async ValueTask StartProcessAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        IsRunning = true;

        process.StartInfo = new ProcessStartInfo(Settings.BinaryPath)
        {
            Arguments = Settings.ToString(),
            UseShellExecute = true,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        await Server.StartAsync(cancellationToken).ConfigureAwait(false);
        process.Start();
    }

    /// <inheritdoc/>
    public async ValueTask<IWindow> OpenWindowAsync(TSettings settings, CancellationToken cancellationToken)
    {
        await StartProcessAsync(cancellationToken).ConfigureAwait(false);
        throw new NotImplementedException();
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
    public ValueTask DisposeAsync()
    {
        process.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}