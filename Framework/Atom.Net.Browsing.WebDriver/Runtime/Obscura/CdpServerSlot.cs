using Atom.Net.Browsing.WebDriver.Protocol.Cdp;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Параметры, которые Obscura применяет ко всему процессу: поверхности с разными значениями
/// обслуживаются разными процессами.
/// </summary>
internal readonly record struct CdpServerKey(string? ProxyUrl, string? TimeZone, (double Latitude, double Longitude)? Geolocation);

/// <summary>
/// Процесс движка с одним CDP-соединением, через которое транспорт ведёт все его вкладки.
/// </summary>
/// <remarks>
/// У Obscura каждое WebSocket-соединение — самостоятельный набор контекстов, вкладок и cookie,
/// поэтому соединение на процесс одно: иначе вкладки одного окна оказались бы в разных хранилищах.
/// </remarks>
internal sealed class CdpServerSlot(int index, CdpServerKey key, CdpConnection connection, IAsyncDisposable? owner) : IAsyncDisposable
{
    private int disposeState;

    public int Index { get; } = index;

    public CdpServerKey Key { get; } = key;

    public CdpConnection Connection { get; } = connection ?? throw new ArgumentNullException(nameof(connection));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        try
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (owner is not null)
                await owner.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Число живых вкладок на этом процессе.</summary>
    public int TabCount => Volatile.Read(ref tabCount);

    private int tabCount;

    public void AddTab() => Interlocked.Increment(ref tabCount);

    /// <summary>Снимает вкладку и сообщает, остался ли процесс без вкладок.</summary>
    public bool RemoveTab() => Interlocked.Decrement(ref tabCount) <= 0;
}
