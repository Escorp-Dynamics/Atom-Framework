using System.Buffers;

namespace Atom.Net.Tls;

internal readonly struct OwnerMemory(byte[] buffer, int length) : IDisposable
{
    public byte[] Buffer { get; } = buffer;
    public int Length { get; } = length;
    public ReadOnlySpan<byte> Span => Buffer.AsSpan(0, Length);
    public ReadOnlyMemory<byte> Memory => Buffer.AsMemory(0, Length);

    public void Dispose() => ArrayPool<byte>.Shared.Return(Buffer, clearArray: true);
}