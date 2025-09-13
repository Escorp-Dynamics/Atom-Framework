using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// TLS Extension: Record Size Limit (RFC 8449).
/// Extension ID: 0x001C.
/// </summary>
public class RecordSizeLimitTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x001C;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 2;

    /// <summary>
    /// Максимально допустимый размер TLS-записи (обычно: 16384).
    /// </summary>
    public ushort Limit { get; set; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 2);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Limit);
        offset += 2;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Limit = default;
        base.Reset();
    }
}