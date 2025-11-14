using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Представляет HTTP/1.1 соединение.
/// </summary>
internal class Https11Connection : HttpsConnection
{
    /// <inheritdoc/>
    public override Version Version => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool IsConnected => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool IsSecure => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool IsMultiplexing => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int ActiveStreams => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int MaxConcurrentStreams => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool IsDraining => throw new NotSupportedException();

    /// <inheritdoc/>
    public override IPEndPoint? LocalEndPoint => throw new NotSupportedException();

    /// <inheritdoc/>
    public override IPEndPoint? RemoteEndPoint => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long LastActivityTimestamp => throw new NotSupportedException();

    /// <inheritdoc/>
    public override Traffic Traffic => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool HasCapacity => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Abort([AllowNull] Exception ex) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask CloseAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Dispose() { }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask DisposeAsync() => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool MatchesTarget(string host, int port, bool isHttps) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask OpenAsync(HttpsConnectionOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<bool> PingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void StartDrain() => throw new NotSupportedException();
}