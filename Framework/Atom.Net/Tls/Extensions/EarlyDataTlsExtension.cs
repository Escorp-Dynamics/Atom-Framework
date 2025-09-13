using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x002A): early_data.
/// Сообщает о намерении отправить 0-RTT данные с использованием PSK.
/// </summary>
public class EarlyDataTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x002A;

    /// <inheritdoc/>
    public override int Size => 2 + 2; // ID + длина (0)

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 0);
        offset += 2;
    }
}