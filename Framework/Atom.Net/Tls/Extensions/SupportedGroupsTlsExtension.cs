using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x000A): supported_groups.
/// Список поддерживаемых групп (кривых) для Key Share и ECDHE.
/// </summary>
public class SupportedGroupsTlsExtension : TlsExtension
{
    private ushort[] cache = [];

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x000A;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 2 + (cache.Length * 2);    // 2 байта — ExtensionId, 2 байта — Length, 2 байта — vector length, n * 2 байта — группы

    /// <summary>
    /// Поддерживаемые группы в порядке приоритета.
    /// </summary>
    public IEnumerable<NamedGroup> Groups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            field = value;
            cache = [.. value.Select(static a => (ushort)a)];
        }
    } = [];

    /// <inheritdoc/>
    public override void Write(Span<byte> buffer, ref int offset)
    {
        // [Extension Id]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Length]
        var bodyLength = 2 + (cache.Length * 2);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)bodyLength);
        offset += 2;

        // [Vector Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(cache.Length * 2));
        offset += 2;

        // [NamedGroup list]
        foreach (var group in cache)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], group);
            offset += 2;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Groups = [];
        base.Reset();
    }
}