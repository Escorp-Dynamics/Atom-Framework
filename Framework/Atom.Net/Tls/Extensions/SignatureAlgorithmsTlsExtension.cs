using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x000D): signature_algorithms.
/// Содержит список допустимых алгоритмов подписи, поддерживаемых клиентом.
/// Используется в ClientHello.
/// </summary>
public class SignatureAlgorithmsTlsExtension : TlsExtension
{
    private ushort[] cache = [];

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x000D;

    /// <summary>
    /// Добавлять ли подставную запись в начало списка подписей.
    /// </summary>
    /// <remarks>
    /// Chromium ставит её первой, Firefox и Safari GREASE в подписях не используют. Замер
    /// 2026-09-09: настоящий Chrome отдавал 0xEAEA перед ML-DSA, а мы список начинали сразу с
    /// ML-DSA. Отпечаток JA4 подставные значения отбрасывает, но в байтах ClientHello разница
    /// видна напрямую, и наблюдателю она доступна без всякого хэша.
    /// </remarks>
    public bool UseGrease { get; set; }

    private int GreaseCount => UseGrease ? 1 : 0;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 2 + ((cache.Length + GreaseCount) * 2);

    /// <summary>
    /// Алгоритмы подписи в порядке приоритета.
    /// </summary>
    public IEnumerable<SignatureAlgorithm> Algorithms
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        // [SignatureScheme list]
        if (UseGrease)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Atom.Net.Tls.Grease.SignatureAlgorithms);
            offset += 2;
        }

        foreach (var item in cache)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], item);
            offset += 2;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Algorithms = [];
        UseGrease = false;
        base.Reset();
    }
}