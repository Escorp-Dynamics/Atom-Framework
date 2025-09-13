using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// TLS Extension: SessionTicket (RFC 5077 / RFC 8446).
/// Extension ID: 0x0023.
/// </summary>
public class SessionTicketExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0023;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Ticket.Length;

    /// <summary>
    /// Бинарные данные сессионного тикета (может быть пустым).
    /// </summary>
    public ReadOnlyMemory<byte> Ticket { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)Ticket.Length);
        offset += 2;

        Ticket.Span.CopyTo(buffer[offset..]);
        offset += Ticket.Length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Ticket = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}