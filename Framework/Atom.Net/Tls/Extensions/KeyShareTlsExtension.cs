using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x0033): key_share.
/// Содержит открытые ключи клиента для каждой группы (предварительно сгенерированные).
/// </summary>
public class KeyShareTlsExtension : TlsExtension
{
    private KeyShare[] cache = [];

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0033;

    /// <inheritdoc/>
    public override int Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var total = 0;
            foreach (var entry in cache) total += 2 + 2 + entry.PublicKey.Length; // group + length + key
            return 2 + 2 + 2 + total;
            // 2 — ExtensionId
            // 2 — Length
            // 2 — vector length
            // N — список KeyShareEntry
        }
    }

    /// <summary>
    /// Список ключей клиента по каждой группе.
    /// </summary>
    public IEnumerable<KeyShare> Entries
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            field = value;
            cache = [.. value];
        }
    } = [];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        // [Extension ID]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Length]
        var bodyLength = 2 + cache.Sum(e => 4 + e.PublicKey.Length);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)bodyLength);
        offset += 2;

        // [Vector Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(bodyLength - 2));
        offset += 2;

        // [KeyShareEntry list]
        foreach (var entry in cache)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)entry.Group);
            offset += 2;

            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)entry.PublicKey.Length);
            offset += 2;

            entry.PublicKey.Span.CopyTo(buffer[offset..]);
            offset += entry.PublicKey.Length;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Entries = [];
        base.Reset();
    }
}