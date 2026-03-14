#pragma warning disable S109, S2325, CA1822, MA0051, MA0008

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// H.264 NAL Unit заголовок.
/// </summary>
internal readonly record struct NalHeader
{
    /// <summary>forbidden_zero_bit (1 bit).</summary>
    public required byte ForbiddenBit { get; init; }

    /// <summary>nal_ref_idc (2 bits).</summary>
    public required byte RefIdc { get; init; }

    /// <summary>nal_unit_type (5 bits).</summary>
    public required byte UnitType { get; init; }
}

/// <summary>
/// Информация о найденном NAL unit.
/// </summary>
internal readonly record struct NalUnit
{
    /// <summary>Заголовок NAL unit.</summary>
    public required NalHeader Header { get; init; }

    /// <summary>Данные RBSP (без start code и emulation prevention bytes).</summary>
    public required int Offset { get; init; }

    /// <summary>Длина данных RBSP.</summary>
    public required int Length { get; init; }
}

/// <summary>
/// H.264 NAL unit parser (ITU-T H.264 Section 7.3.1).
/// </summary>
/// <remarks>
/// Обрабатывает:
/// - Поиск start codes (3-byte: 0x00 0x00 0x01, 4-byte: 0x00 0x00 0x00 0x01)
/// - Удаление emulation prevention bytes (0x03)
/// - Парсинг NAL header
/// </remarks>
internal static class H264Nal
{
    /// <summary>
    /// Парсит NAL header из первого байта.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static NalHeader ParseHeader(byte firstByte) => new()
    {
        ForbiddenBit = (byte)((firstByte >> 7) & 1),
        RefIdc = (byte)((firstByte >> 5) & 3),
        UnitType = (byte)(firstByte & 0x1F),
    };

    /// <summary>
    /// Ищет следующий start code в данных.
    /// </summary>
    /// <returns>Смещение start code или -1.</returns>
    public static int FindStartCode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
            return -1;

        ref var dataRef = ref MemoryMarshal.GetReference(data);
        var end = data.Length - 2;

        for (var i = 0; i < end; i++)
        {
            if (Unsafe.Add(ref dataRef, i) == 0 &&
                Unsafe.Add(ref dataRef, i + 1) == 0)
            {
                // 3-byte start code: 0x00 0x00 0x01
                if (Unsafe.Add(ref dataRef, i + 2) == 1)
                {
                    return i;
                }

                // 4-byte start code: 0x00 0x00 0x00 0x01
                if (i + 3 < data.Length &&
                    Unsafe.Add(ref dataRef, i + 2) == 0 &&
                    Unsafe.Add(ref dataRef, i + 3) == 1)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Определяет длину start code (3 или 4 байта).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StartCodeLength(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 3 < data.Length &&
            data[offset] == 0 && data[offset + 1] == 0 &&
            data[offset + 2] == 0 && data[offset + 3] == 1)
        {
            return 4;
        }

        return 3;
    }

    /// <summary>
    /// Удаляет emulation prevention bytes (0x00 0x00 0x03 → 0x00 0x00) из RBSP.
    /// </summary>
    /// <returns>Количество записанных байт.</returns>
    public static int RemoveEmulationPrevention(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.Length < 3)
        {
            input.CopyTo(output);
            return input.Length;
        }

        ref var srcRef = ref MemoryMarshal.GetReference(input);
        ref var dstRef = ref MemoryMarshal.GetReference(output);
        var written = 0;
        var i = 0;

        while (i < input.Length)
        {
            // Detect 0x00 0x00 0x03 pattern
            if (i + 2 < input.Length &&
                Unsafe.Add(ref srcRef, i) == 0 &&
                Unsafe.Add(ref srcRef, i + 1) == 0 &&
                Unsafe.Add(ref srcRef, i + 2) == 3)
            {
                // Write 0x00 0x00, skip 0x03
                Unsafe.Add(ref dstRef, written++) = 0;
                Unsafe.Add(ref dstRef, written++) = 0;
                i += 3;
            }
            else
            {
                Unsafe.Add(ref dstRef, written++) = Unsafe.Add(ref srcRef, i);
                i++;
            }
        }

        return written;
    }

    /// <summary>
    /// Разбивает Annex B bitstream на NAL units.
    /// </summary>
    /// <returns>Количество найденных NAL units.</returns>
    public static int ParseAnnexB(ReadOnlySpan<byte> data, Span<NalUnit> units)
    {
        var count = 0;
        var pos = 0;

        while (pos < data.Length && count < units.Length)
        {
            var startCodePos = FindStartCode(data[pos..]);
            if (startCodePos < 0)
                break;

            var absoluteStart = pos + startCodePos;
            var scLen = StartCodeLength(data, absoluteStart);
            var nalStart = absoluteStart + scLen;

            if (nalStart >= data.Length)
                break;

            // Find next start code or end of data
            var nextStart = nalStart + 1;
            var nalEnd = data.Length;

            if (nextStart < data.Length - 2)
            {
                var next = FindStartCode(data[nextStart..]);
                if (next >= 0)
                {
                    nalEnd = nextStart + next;
                }
            }

            // Remove trailing zero bytes
            while (nalEnd > nalStart && data[nalEnd - 1] == 0)
                nalEnd--;

            var header = ParseHeader(data[nalStart]);

            units[count++] = new NalUnit
            {
                Header = header,
                Offset = nalStart + 1, // skip NAL header byte
                Length = nalEnd - nalStart - 1,
            };

            pos = nalEnd;
        }

        return count;
    }

    /// <summary>
    /// Разбивает AVCC/avcC format (length-prefixed) на NAL units.
    /// </summary>
    /// <returns>Количество найденных NAL units.</returns>
    public static int ParseAvcc(ReadOnlySpan<byte> data, int nalLengthSize, Span<NalUnit> units)
    {
        var count = 0;
        var pos = 0;

        while (pos + nalLengthSize <= data.Length && count < units.Length)
        {
            var nalLen = 0;

            for (var i = 0; i < nalLengthSize; i++)
                nalLen = (nalLen << 8) | data[pos + i];

            pos += nalLengthSize;

            if (nalLen <= 0 || pos + nalLen > data.Length)
                break;

            var header = ParseHeader(data[pos]);

            units[count++] = new NalUnit
            {
                Header = header,
                Offset = pos + 1,
                Length = nalLen - 1,
            };

            pos += nalLen;
        }

        return count;
    }
}
