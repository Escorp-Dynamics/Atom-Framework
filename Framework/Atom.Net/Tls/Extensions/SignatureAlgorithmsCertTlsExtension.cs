using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x0032): signature_algorithms_cert.
/// Указывает допустимые алгоритмы подписи для сертификатов.
/// </summary>
public class SignatureAlgorithmsCertTlsExtension : TlsExtension
{
    private ushort[] cache = [];

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0032;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 2 + (cache.Length * 2);  // 2 — ExtensionId, 2 — длина, 2 — vector length, n * 2 — алгоритмы

    /// <summary>
    /// Список алгоритмов подписи, допустимых в цепочке сертификатов.
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
        // [Extension ID]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Length]
        var bodyLength = 2 + (cache.Length * 2);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)bodyLength);
        offset += 2;

        // [Vector Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(cache.Length * 2));
        offset += 2;

        // [Algorithms]
        foreach (var algorithm in cache)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], algorithm);
            offset += 2;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Algorithms = [];
        base.Reset();
    }
}