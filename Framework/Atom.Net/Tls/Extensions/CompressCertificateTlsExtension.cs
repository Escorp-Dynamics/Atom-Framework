using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x001B): compress_certificate.
/// Указывает поддерживаемые алгоритмы сжатия сертификатов.
/// </summary>
public class CompressCertificateTlsExtension : TlsExtension
{
    private ushort[] cache = [];

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x001B;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 1 + (cache.Length * 2);

    /// <summary>
    /// Алгоритмы сжатия, поддерживаемые клиентом (в порядке приоритета).
    /// </summary>
    public IEnumerable<CertificateCompressionAlgorithm> Algorithms
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

        // [Length = 1 + 2*n]
        var length = 1 + (cache.Length * 2);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)length);
        offset += 2;

        // [Vector Length = 2*n]
        buffer[offset++] = (byte)(cache.Length * 2);

        // [Algorithms]
        foreach (var algo in cache)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], algo);
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