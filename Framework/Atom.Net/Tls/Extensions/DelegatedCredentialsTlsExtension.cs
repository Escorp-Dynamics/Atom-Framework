using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// delegated_credentials (0x0022).
/// </summary>
/// <remarks>
/// Клиент сообщает, что готов принять от сервера делегированные учётные данные с ограниченным
/// сроком, и перечисляет допустимые алгоритмы их подписи.
///
/// Расширение отправляет Firefox и НЕ отправляет Chromium — оно и есть одно из различий,
/// по которым эти движки различимы в первом пакете. Прежняя реализация была заглушкой с чужим
/// номером 0x0037 и пустым телом, то есть не соответствовала ни одному браузеру.
/// </remarks>
public sealed class DelegatedCredentialsTlsExtension : TlsExtension
{
    private SignatureAlgorithm[] algorithms = [];

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0022;

    /// <summary>
    /// Алгоритмы подписи, допустимые для делегированных учётных данных.
    /// </summary>
    public IEnumerable<SignatureAlgorithm> Algorithms
    {
        get => algorithms;
        set => algorithms = value is null ? [] : [.. value];
    }

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 2 + (algorithms.Length * 2);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(2 + (algorithms.Length * 2)));
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(algorithms.Length * 2));
        offset += 2;

        foreach (var algorithm in algorithms)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)algorithm);
            offset += 2;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Id = 0x0022;
        algorithms = [];
        base.Reset();
    }
}
