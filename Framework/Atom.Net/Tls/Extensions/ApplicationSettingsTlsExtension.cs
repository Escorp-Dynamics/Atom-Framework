using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Нестандартное расширение TLS (0x00FF): application_settings.
/// Используется Chrome в связке с HTTP/3/QUIC.
/// </summary>
public class ApplicationSettingsTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x00FF;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Data.Length;

    /// <summary>
    /// Данные расширения (opaque).
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        // [Extension ID]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)Data.Length);
        offset += 2;

        // [Data]
        Data.Span.CopyTo(buffer[offset..]);
        offset += Data.Length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Data = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}