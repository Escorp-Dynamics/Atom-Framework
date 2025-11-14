using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Представляет базовую реализацию соединения HTTPS.
/// </summary>
internal abstract class HttpsConnection : IHttpsConnection
{
    /// <inheritdoc/>
    public abstract Version Version { get; }

    /// <inheritdoc/>
    public abstract bool IsConnected { get; }

    /// <inheritdoc/>
    public abstract bool IsSecure { get; }

    /// <inheritdoc/>
    public abstract bool IsMultiplexing { get; }

    /// <inheritdoc/>
    public abstract int ActiveStreams { get; }

    /// <inheritdoc/>
    public abstract int MaxConcurrentStreams { get; }

    /// <inheritdoc/>
    public abstract bool IsDraining { get; }

    /// <inheritdoc/>
    public abstract IPEndPoint? LocalEndPoint { get; }

    /// <inheritdoc/>
    public abstract IPEndPoint? RemoteEndPoint { get; }

    /// <inheritdoc/>
    public abstract long LastActivityTimestamp { get; }

    /// <inheritdoc/>
    public abstract Traffic Traffic { get; }

    /// <inheritdoc/>
    public abstract bool HasCapacity { get; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void Abort([AllowNull] Exception ex);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void Dispose();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract ValueTask DisposeAsync();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract bool MatchesTarget(string host, int port, bool isHttps);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract ValueTask OpenAsync(HttpsConnectionOptions options, CancellationToken cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask OpenAsync(HttpsConnectionOptions options) => OpenAsync(options, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract ValueTask<bool> PingAsync(CancellationToken cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> PingAsync() => PingAsync(CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request, CancellationToken cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request) => SendAsync(request, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void StartDrain();
}