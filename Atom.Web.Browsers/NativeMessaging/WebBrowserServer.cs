using System.Net.WebSockets;

namespace Atom.Web.Browsers.NativeMessaging;

/// <summary>
/// Предоставляет доступ к веб-браузерам через нативное сообщение.
/// </summary>
public class WebBrowserServer : IWebBrowserServer
{
    private readonly ClientWebSocket webSocket = new();

    /// <inheritdoc/>
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public ValueTask StartAsync() => StartAsync(CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
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