using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS: GREASE.
/// Используется для маскировки fingerprint'а. Не несёт смысла, но помогает в антидетекте.
/// </summary>
public class GreaseTlsExtension : TlsExtension
{
    /// <summary>
    /// Идентификатор расширения (должен быть из GREASE-диапазона).
    /// </summary>
    /// <summary>
    /// Тип расширения.
    /// </summary>
    /// <remarks>
    /// Нулевое значение означает «выбрать при записи»: значения GREASE выводятся из random
    /// конкретного ClientHello, а профиль строится задолго до него и переживает много соединений.
    /// Зафиксировав значение в профиле, мы отправляли бы одно и то же GREASE во всех соединениях —
    /// то есть превратили бы средство против запоминания в устойчивый признак.
    /// </remarks>
    public override ushort Id { get; set; }

    /// <summary>
    /// Место расширения в списке: определяет, какое из значений GREASE будет выбрано.
    /// </summary>
    public int Slot { get; set; }

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Data.Length;

    /// <summary>
    /// Произвольное тело расширения (может быть пустым).
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        // Значение выбирается здесь, потому что здесь уже известен random текущего ClientHello.
        var id = Id is 0 ? Tls.Grease.ExtensionAt(Slot) : Id;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)Data.Length);
        offset += 2;

        Data.Span.CopyTo(buffer[offset..]);
        offset += Data.Length;
    }

    /// <summary>
    /// Создаёт расширение GREASE для указанного места в списке.
    /// </summary>
    /// <param name="slot">Место расширения: 0 — первое, 1 — второе.</param>
    /// <param name="payloadLength">Длина тела; браузер оставляет первое пустым, а последнее — в один нулевой байт.</param>
    /// <returns>Готовое расширение.</returns>
    /// <remarks>
    /// Значение типа НЕ выбирается здесь: оно выводится из random конкретного ClientHello и
    /// назначается при записи. См. пояснение у свойства <see cref="Id"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GreaseTlsExtension Create(int slot = 0, int payloadLength = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadLength);

        return new GreaseTlsExtension
        {
            Slot = slot,
            Data = payloadLength is 0 ? ReadOnlyMemory<byte>.Empty : new byte[payloadLength],
        };
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Id = default;
        Slot = 0;
        Data = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}