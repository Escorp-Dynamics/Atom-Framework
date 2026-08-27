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

    /// <summary>
    /// Добавлять ли подставную группу в начало списка.
    /// </summary>
    /// <remarks>
    /// Движки Chromium и Safari ставят её первой; Firefox GREASE не использует вовсе. Значение
    /// обязано СОВПАДАТЬ с подставной группой в долях ключа — так его выбирает библиотека
    /// браузера, и расхождение между двумя списками заметно.
    /// </remarks>
    public bool UseGrease { get; set; }

    private int GreaseCount => UseGrease ? 1 : 0;

    /// <summary>Размер расширения в байтах.</summary>
    public override int Size => 2 + 2 + 2 + ((cache.Length + GreaseCount) * 2);    // 2 байта — ExtensionId, 2 байта — Length, 2 байта — vector length, n * 2 байта — группы

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
        var bodyLength = 2 + ((cache.Length + GreaseCount) * 2);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)bodyLength);
        offset += 2;

        // [Vector Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)((cache.Length + GreaseCount) * 2));
        offset += 2;

        // [NamedGroup list]
        if (UseGrease)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Atom.Net.Tls.Grease.Groups);
            offset += 2;
        }

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