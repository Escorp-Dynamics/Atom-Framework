using System.Runtime.CompilerServices;
using Stream = Atom.IO.Stream;

namespace Atom.Net.Quic;

/// <summary>
/// Представляет логический поток данных внутри <see cref="QuicConnection"/>.
/// Управляет ID, flow-control окнами и маппингом в пакеты QUIC.
/// </summary>
public sealed class QuicStream : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => default;

    /// <inheritdoc/>
    public override bool CanWrite => default;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer) => throw new NotImplementedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotImplementedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}