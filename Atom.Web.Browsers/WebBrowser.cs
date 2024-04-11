using System.Diagnostics;
using Atom.Threading;
using Atom.Web.Browsers.BOM;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет браузер.
/// </summary>
/// <typeparam name="TSettings">Тип настроек браузера.</typeparam>
public abstract class WebBrowser<TSettings> : IWebBrowser<TSettings>
    where TSettings : IWebBrowserSettings, new()
{
    private readonly Process process;

    /// <inheritdoc/>
    public TSettings Settings { get; init; }

    /// <inheritdoc/>
    public bool IsRunning { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="WebBrowser{TSettings}"/>.
    /// </summary>
    /// <param name="settings">Настройки браузера.</param>
    protected WebBrowser(TSettings settings)
    {
        Settings = settings;
        process = new Process();
    }

    private ValueTask StartProcessAsync(CancellationToken cancellationToken)
    {
        IsRunning = true;

        process.StartInfo = new ProcessStartInfo(Settings.BinaryPath)
        {
            UseShellExecute = true,
            CreateNoWindow = true,
        };

        process.Start();
        return ValueTask.CompletedTask;
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