using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// TLS Extension: Extended Master Secret (RFC 7627).
/// Extension ID: 0x0017.
/// </summary>
public class ExtendedMasterSecretTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0017;

    /// <inheritdoc/>
    public override int Size => IsEnabled ? 2 + 2 : 0;

    /// <summary>
    /// Включено ли расширение.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        if (!IsEnabled) return;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 0);
        offset += 2;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        IsEnabled = default;
        base.Reset();
    }
}