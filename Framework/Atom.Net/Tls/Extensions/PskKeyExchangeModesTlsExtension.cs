using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// TLS Extension: PSK Key Exchange Modes (RFC 8446, §4.2.9).
/// Extension ID: 0x002d.
/// </summary>
public class PskKeyExchangeModesTlsExtension : TlsExtension
{
    private int modesLength;

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x002d;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 1 + modesLength;

    /// <summary>
    /// Поддерживаемые режимы.
    /// </summary>
    public IEnumerable<PskKeyExchangeMode> Modes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            field = value;
            modesLength = value.Count();
        }
    } = [PskKeyExchangeMode.PskDheKe];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(1 + modesLength));
        offset += 2;

        buffer[offset++] = (byte)modesLength;

        foreach (var mode in Modes) buffer[offset++] = (byte)mode;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Modes = [];
        base.Reset();
    }
}