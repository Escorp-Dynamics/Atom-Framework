using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение cookie (идентификатор 0x002C, RFC 8446 §4.2.2).
/// </summary>
/// <remarks>
/// Клиент никогда не придумывает его сам: он лишь возвращает без изменений то, что прислал сервер
/// в HelloRetryRequest. Сервер кладёт туда собственное состояние, чтобы не хранить его у себя
/// между двумя приветствиями, и проверяет при получении — изменённое или потерянное значение
/// означает отказ рукопожатия.
/// </remarks>
public class CookieTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x002C;

    /// <summary>Значение, присланное сервером.</summary>
    public ReadOnlyMemory<byte> Cookie { get; set; }

    /// <inheritdoc/>
    public override int Size => 2 + Cookie.Length;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(2 + Cookie.Length));
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)Cookie.Length);
        offset += 2;

        Cookie.Span.CopyTo(buffer[offset..]);
        offset += Cookie.Length;
    }
}
