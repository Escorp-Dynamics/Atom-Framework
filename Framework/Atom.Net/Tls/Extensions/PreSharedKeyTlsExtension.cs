using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// 
/// </summary>
public class PreSharedKeyTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0029;

    /// <inheritdoc/>
    public override int Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var idSize = 2;
            var binderSize = 2;

            foreach (var id in Identities) idSize += 2 + id.Identity.Length + 4;
            foreach (var binder in Binders) binderSize += 1 + binder.Length;

            return 2 + 2 + idSize + binderSize;
        }
    }

    /// <summary>
    /// Список PSK-идентификаторов (идентификаторы + obfuscated_ticket_age).
    /// </summary>
    public IEnumerable<PskIdentity> Identities { get; set; } = [];

    /// <summary>
    /// Список binder'ов для каждого идентификатора (например, HMAC).
    /// </summary>
    public IEnumerable<ReadOnlyMemory<byte>> Binders { get; set; } = [];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        var totalLenOffset = offset;
        offset += 2;

        var start = offset;

        // identities
        var idLenOffset = offset;
        offset += 2;

        var idLength = 0;

        foreach (var id in Identities)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)id.Identity.Length);
            offset += 2;
            id.Identity.Span.CopyTo(buffer[offset..]);
            offset += id.Identity.Length;

            BinaryPrimitives.WriteUInt32BigEndian(buffer[offset..], id.ObfuscatedTicketAge);
            offset += 4;
            idLength += 2 + id.Identity.Length + 4;
        }

        BinaryPrimitives.WriteUInt16BigEndian(buffer[idLenOffset..], (ushort)idLength);

        // binders
        var binderLenOffset = offset;
        offset += 2;

        var binderLength = 0;

        foreach (var binder in Binders)
        {
            buffer[offset++] = (byte)binder.Length;
            binder.Span.CopyTo(buffer[offset..]);
            offset += binder.Length;

            binderLength += 1 + binder.Length;
        }

        BinaryPrimitives.WriteUInt16BigEndian(buffer[binderLenOffset..], (ushort)binderLength);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[totalLenOffset..], (ushort)(offset - start));
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Identities = [];
        Binders = [];
        base.Reset();
    }
}