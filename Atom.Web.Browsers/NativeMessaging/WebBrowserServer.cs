using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Atom.Web.Browsers.NativeMessaging;

/// <summary>
/// Предоставляет доступ к веб-браузерам через нативное сообщение.
/// </summary>
public abstract class WebBrowserServer : IWebBrowserServer
{
    private readonly ClientWebSocket webSocket = new();

    /// <inheritdoc/>
    public Manifest Manifest { get; protected set; }

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserServer>? Started;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserServer>? Stopped;

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="WebBrowserServer"/>.
    /// </summary>
    /// <param name="manifest">Манифест сервера.</param>
    protected WebBrowserServer(Manifest manifest)
    {
        Manifest = manifest;
    }

    /// <summary>
    /// Происходит в момент запуска браузерного сервера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    protected virtual async ValueTask OnStarted(CancellationToken cancellationToken)
    {
        var appPath = Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var exePath = appPath + ".exe";
            if (!File.Exists(exePath)) exePath = appPath + ".dll";
            appPath = exePath;
        }

        if (!File.Exists(appPath))
        {
            appPath += ".dll";
            if (!File.Exists(appPath)) throw new FileNotFoundException("Не удалось определить путь к текущему исполняемому файлу");
        }

        Manifest.Path = appPath;

        var json = JsonSerializer.Serialize(Manifest, JsonManifestContext.Default.Manifest);
        var path = GetManifestPath();

        var dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
        
        await File.WriteAllTextAsync(Path.GetFullPath(path), json, cancellationToken).ConfigureAwait(false);

        await Started.On(this).ConfigureAwait(false);
    }

    /// <summary>
    /// Происходит в момент остановки браузерного сервера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    protected virtual ValueTask OnStopped(CancellationToken cancellationToken) => Stopped.On(this);

    /// <summary>
    /// Возвращает путь к файлу манифеста.
    /// </summary>
    protected abstract string GetManifestPath();

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        await OnStarted(cancellationToken).ConfigureAwait(false);

        // TODO: реализовать
    }

    /// <inheritdoc/>
    public ValueTask StartAsync() => StartAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        var path = GetManifestPath();
        if (File.Exists(path)) File.Delete(path);

        await OnStopped(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask StopAsync() => StartAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        webSocket.Dispose();
        GC.SuppressFinalize(this);
    }
}