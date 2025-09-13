using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS "ec_point_formats" (extension id = 0x000B).
/// Нужен только для TLS 1.2 fallback: большинство серверов ожидают,
/// что мы явно сообщим поддерживаемые форматы эллиптических точек.
/// Браузеры обычно указывают единственный формат: "uncompressed" (0).
/// Спецификация: RFC 4492 (устаревшая часть для ECDHE в TLS &lt; 1.3).
/// </summary>
public class EcPointFormatsTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x000B;

    /// <inheritdoc/>
    public override int Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var count = 0;
            foreach (var _ in Formats) count++;
            return 1 + count;
        }
    }

    /// <summary>
    /// Список форматов. По умолчанию — только Uncompressed (0x00).
    /// Публичная коллекция — IEnumerable&lt;byte&gt; по правилам проекта.
    /// </summary>
    public IEnumerable<byte> Formats { get; set; } = [0x00];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        var count = Formats.Count();
        var bodyLen = 1 + count;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id); offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)bodyLen); offset += 2;

        buffer[offset++] = (byte)count;
        foreach (var fmt in Formats) buffer[offset++] = fmt;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Formats = [0x00];
        base.Reset();
    }
}