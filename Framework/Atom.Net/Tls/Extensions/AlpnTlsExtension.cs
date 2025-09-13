using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS "application_layer_protocol_negotiation" (ALPN, код 0x0010).
/// Позволяет клиенту предложить список протоколов верхнего уровня (например, "h2", "http/1.1").
/// </summary>
public class AlpnTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0010;

    /// <inheritdoc/>
    public override int Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var namesLength = 0;

            foreach (var proto in Protocols)
            {
                var len = proto.Length;
                if ((uint)len is 0 || len > byte.MaxValue) throw new InvalidOperationException("ALPN: длина имени протокола должна быть в диапазоне 1..255 байт");

                namesLength += 1 + len;
            }

            return 2 + namesLength;
        }
    }

    /// <summary>
    /// Список ALPN-протоколов в порядке приоритета.
    /// Требование RFC: каждый элемент 1..255 байт.
    /// </summary>
    public IEnumerable<ReadOnlyMemory<byte>> Protocols { get; set; } = [];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        var payloadLength = Size;

        // [Extension ID]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Extension Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)payloadLength);
        offset += 2;

        // [ProtocolNameList Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(payloadLength - 2));
        offset += 2;

        // [ProtocolName]* : по одному — длина (1) + байты имени
        foreach (var proto in Protocols)
        {
            var span = proto.Span;
            buffer[offset] = (byte)span.Length;
            offset += 1;

            span.CopyTo(buffer[offset..]);
            offset += span.Length;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Protocols = [];
        base.Reset();
    }

    /// <summary>
    /// 
    /// </summary>
    public static ReadOnlyMemory<byte> H2 { get; } = "h2"u8.ToArray();

    /// <summary>
    /// 
    /// </summary>
    public static ReadOnlyMemory<byte> Http11 { get; } = "http/1.1"u8.ToArray();
}