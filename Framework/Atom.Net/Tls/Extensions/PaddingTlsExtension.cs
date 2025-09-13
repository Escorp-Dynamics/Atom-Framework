using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// TLS Extension: Padding (RFC 7685).
/// Extension ID: 0x0015.
/// </summary>
public class PaddingTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0015;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Length;

    /// <summary>
    /// Количество байт-заполнителей внутри расширения (0–65535).
    /// </summary>
    public int Length { get; set; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)Length);
        offset += 2;

        buffer.Slice(offset, Length).Clear(); // заполняем нулями
        offset += Length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Length = default;
        base.Reset();
    }
}