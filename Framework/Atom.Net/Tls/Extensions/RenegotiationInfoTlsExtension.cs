using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// TLS Extension: Renegotiation Info (RFC 5746).
/// Extension ID: 0xff01.
/// </summary>
public class RenegotiationInfoTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0xff01;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 1 + RenegotiatedConnection.Length;

    /// <summary>
    /// Значение "renegotiated_connection". Для первого ClientHello всегда пустое (0x00).
    /// </summary>
    public ReadOnlyMemory<byte> RenegotiatedConnection { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        var extDataLength = 1 + RenegotiatedConnection.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)extDataLength);
        offset += 2;

        buffer[offset++] = (byte)RenegotiatedConnection.Length;
        RenegotiatedConnection.Span.CopyTo(buffer[offset..]);
        offset += RenegotiatedConnection.Length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        RenegotiatedConnection = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}