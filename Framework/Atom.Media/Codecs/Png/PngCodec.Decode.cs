#pragma warning disable IDE0010, S109, MA0051, S3776

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Декодирование PNG.
/// </summary>
public sealed partial class PngCodec
{
    #region Decode

    /// <inheritdoc/>
    public CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var startTimestamp = Stopwatch.GetTimestamp();

        Logger?.LogPngDecodeStart(data.Length);

        if (isEncoder)
        {
            Logger?.LogPngError("Кодек инициализирован как encoder");
            return CodecResult.UnsupportedFormat;
        }

        var result = ValidatePngHeader(data, out var header, out var compressedData);
        if (result != CodecResult.Success)
        {
            Logger?.LogPngError("Ошибка валидации заголовка");
            return result;
        }

        Logger?.LogPngHeadersParsed(header.Width, header.Height, header.BitDepth, header.ColorType);

        result = DecompressAndDefilter(compressedData, frame.PackedData, header);

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (result == CodecResult.Success)
        {
            Logger?.LogPngDecodeComplete(header.Width, header.Height, elapsedMs);
        }

        return result;
    }

    /// <inheritdoc/>
    public ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> data,
        VideoFrameBuffer buffer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        var frame = buffer.AsFrame();
        var result = Decode(data.Span, ref frame);
        return new ValueTask<CodecResult>(result);
    }

    #endregion

    #region Header Parsing

    /// <summary>
    /// Информация из IHDR чанка.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct PngIhdr(
        int Width,
        int Height,
        byte BitDepth,
        byte ColorType,
        byte Compression,
        byte Filter,
        byte Interlace)
    {
        /// <summary>Количество байт на пиксель.</summary>
        public int BytesPerPixel
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ColorType switch
            {
                ColorTypeGrayscale => BitDepth <= 8 ? 1 : 2,
                ColorTypeRgb => BitDepth <= 8 ? 3 : 6,
                ColorTypeIndexed => 1,
                ColorTypeGrayscaleAlpha => BitDepth <= 8 ? 2 : 4,
                ColorTypeRgba => BitDepth <= 8 ? 4 : 8,
                _ => 0,
            };
        }

        /// <summary>Количество бит на пиксель (для sub-byte форматов).</summary>
        public int BitsPerPixel
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ColorType switch
            {
                ColorTypeGrayscale => BitDepth,
                ColorTypeRgb => BitDepth * 3,
                ColorTypeIndexed => BitDepth,
                ColorTypeGrayscaleAlpha => BitDepth * 2,
                ColorTypeRgba => BitDepth * 4,
                _ => 0,
            };
        }

        /// <summary>Байт для фильтра bpp (минимум 1).</summary>
        public int FilterBpp
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Math.Max(1, BitsPerPixel / 8);
        }
    }

    /// <summary>
    /// Валидирует заголовок PNG и извлекает сжатые данные.
    /// </summary>
    private static CodecResult ValidatePngHeader(ReadOnlySpan<byte> data, out PngIhdr header, out ReadOnlySpan<byte> compressedData)
    {
        header = default;
        compressedData = default;

        if (data.Length < SignatureSize + ChunkHeaderSize + IhdrDataSize)
        {
            return CodecResult.InvalidData;
        }

        if (!data[..SignatureSize].SequenceEqual(PngSignature))
        {
            return CodecResult.InvalidData;
        }

        var result = ParseIhdr(data[SignatureSize..], out header);
        if (result != CodecResult.Success)
        {
            return result;
        }

        compressedData = CollectIdatChunks(data[SignatureSize..]);
        return compressedData.Length == 0 ? CodecResult.InvalidData : CodecResult.Success;
    }

    /// <summary>
    /// Парсит IHDR чанк PNG.
    /// </summary>
    internal static CodecResult ParseIhdr(ReadOnlySpan<byte> data, out PngIhdr header)
    {
        header = default;

        if (data.Length < ChunkHeaderSize + IhdrDataSize + 4) // +4 для CRC
        {
            return CodecResult.InvalidData;
        }

        var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data);
        var chunkType = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);

        if (chunkType != ChunkIhdr || chunkLength != IhdrDataSize)
        {
            return CodecResult.InvalidData;
        }

        var ihdrData = data[ChunkHeaderSize..];
        var width = BinaryPrimitives.ReadInt32BigEndian(ihdrData);
        var height = BinaryPrimitives.ReadInt32BigEndian(ihdrData[4..]);
        var bitDepth = ihdrData[8];
        var colorType = ihdrData[9];
        var compression = ihdrData[10];
        var filter = ihdrData[11];
        var interlace = ihdrData[12];

        // Валидация bit depth для каждого color type
        var validBitDepth = colorType switch
        {
            ColorTypeGrayscale => bitDepth is 1 or 2 or 4 or 8 or 16,
            ColorTypeRgb => bitDepth is 8 or 16,
            ColorTypeIndexed => bitDepth is 1 or 2 or 4 or 8,
            ColorTypeGrayscaleAlpha => bitDepth is 8 or 16,
            ColorTypeRgba => bitDepth is 8 or 16,
            _ => false,
        };

        if (!validBitDepth)
        {
            return CodecResult.UnsupportedFormat;
        }

        if (compression != 0 || filter != 0)
        {
            return CodecResult.UnsupportedFormat;
        }

        // Интерлейс: 0 = none, 1 = Adam7
        if (interlace > 1)
        {
            return CodecResult.UnsupportedFormat;
        }

        header = new PngIhdr(width, height, bitDepth, colorType, compression, filter, interlace);
        return CodecResult.Success;
    }

    /// <summary>
    /// Собирает данные из всех IDAT чанков в единый буфер.
    /// </summary>
    private static ReadOnlySpan<byte> CollectIdatChunks(ReadOnlySpan<byte> data)
    {
        // Подсчёт размера всех IDAT
        var totalIdatSize = 0;
        var offset = 0;

        // Пропускаем IHDR
        var ihdrLength = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
        offset += ChunkHeaderSize + ihdrLength + 4; // +4 для CRC

        // Первый проход: считаем размер
        var scanOffset = offset;
        while (scanOffset + ChunkHeaderSize <= data.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data[scanOffset..]);
            var chunkType = BinaryPrimitives.ReadUInt32BigEndian(data[(scanOffset + 4)..]);

            if (chunkType == ChunkIdat)
            {
                totalIdatSize += chunkLength;
            }
            else if (chunkType == ChunkIend)
            {
                break;
            }

            scanOffset += ChunkHeaderSize + chunkLength + 4;
        }

        if (totalIdatSize == 0)
        {
            return [];
        }

        // Если только один IDAT — возвращаем slice напрямую
        var firstIdatOffset = -1;
        var idatCount = 0;
        scanOffset = offset;

        while (scanOffset + ChunkHeaderSize <= data.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data[scanOffset..]);
            var chunkType = BinaryPrimitives.ReadUInt32BigEndian(data[(scanOffset + 4)..]);

            if (chunkType == ChunkIdat)
            {
                if (firstIdatOffset < 0)
                {
                    firstIdatOffset = scanOffset;
                }

                idatCount++;
            }
            else if (chunkType == ChunkIend)
            {
                break;
            }

            scanOffset += ChunkHeaderSize + chunkLength + 4;
        }

        if (idatCount == 1)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(data[firstIdatOffset..]);
            return data.Slice(firstIdatOffset + ChunkHeaderSize, len);
        }

        // Несколько IDAT — собираем в буфер
        // Примечание: можно использовать ArrayPool для уменьшения аллокаций
        var buffer = new byte[totalIdatSize];
        var bufferOffset = 0;
        scanOffset = offset;

        while (scanOffset + ChunkHeaderSize <= data.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data[scanOffset..]);
            var chunkType = BinaryPrimitives.ReadUInt32BigEndian(data[(scanOffset + 4)..]);

            if (chunkType == ChunkIdat)
            {
                data.Slice(scanOffset + ChunkHeaderSize, chunkLength).CopyTo(buffer.AsSpan(bufferOffset));
                bufferOffset += chunkLength;
            }
            else if (chunkType == ChunkIend)
            {
                break;
            }

            scanOffset += ChunkHeaderSize + chunkLength + 4;
        }

        return buffer;
    }

    #endregion

    #region Decompression

    /// <summary>
    /// Декомпрессирует и дефильтрует PNG данные.
    /// </summary>
    private CodecResult DecompressAndDefilter(ReadOnlySpan<byte> compressedData, Plane<byte> destination, PngIhdr header)
    {
        try
        {
            // +1 для filter byte каждой строки
            var rowBytes = CalculateRowBytes(header.Width, header.BitsPerPixel);
            var rawSize = (rowBytes + 1) * header.Height;
            var rawData = ArrayPool<byte>.Shared.Rent(rawSize);

            try
            {
                var decompressedSize = DecompressZlib(compressedData, rawData.AsSpan(0, rawSize));
                if (decompressedSize != rawSize)
                {
                    return CodecResult.InvalidData;
                }

                // Проверяем, что ширина строки Plane достаточна для данных
                var outputRowBytes = header.Width * header.BytesPerPixel;
                if (destination.Width < outputRowBytes)
                {
                    return CodecResult.OutputBufferTooSmall;
                }

                // Выбор оптимизированного defilter
                DefilterPng(rawData.AsSpan(0, rawSize), destination, header);
                return CodecResult.Success;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rawData);
            }
        }
        catch (InvalidDataException)
        {
            return CodecResult.InvalidData;
        }
    }

    /// <summary>
    /// Вычисляет количество байт в строке (с учётом sub-byte форматов).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateRowBytes(int width, int bitsPerPixel) => ((width * bitsPerPixel) + 7) / 8;

    /// <summary>
    /// Декомпрессия ZLIB данных (2 байта заголовка + DEFLATE + 4 байта ADLER32).
    /// </summary>
    private static int DecompressZlib(ReadOnlySpan<byte> compressed, Span<byte> output)
    {
        // Пропускаем 2 байта ZLIB заголовка
        if (compressed.Length < 6)
        {
            return 0;
        }

        var deflateData = compressed[2..^4]; // Без заголовка ZLIB и ADLER32

        // Проверяем Store mode (первый байт DEFLATE: BFINAL(bit 0) + BTYPE(bits 1-2))
        // Store mode: BTYPE = 00, т.е. биты 1-2 = 0
        // 0x00 = non-final Store, 0x01 = final Store
        if (deflateData.Length > 0 && (deflateData[0] & 0x06) == 0x00)
        {
            // Попробуем быструю распаковку Store mode
            var storeResult = DecompressZlibStore(deflateData, output);
            if (storeResult >= 0)
            {
                return storeResult;
            }
            // Fallback к DeflateStream если Store не удалось
        }

        // Используем MemoryStream для чтения сжатых данных
        using var compressedStream = new MemoryStream(deflateData.ToArray());
        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);

        var totalRead = 0;
        var buffer = output;

        int bytesRead;
        while ((bytesRead = deflateStream.Read(buffer)) > 0)
        {
            totalRead += bytesRead;
            buffer = buffer[bytesRead..];
        }

        return totalRead;
    }

    /// <summary>
    /// Быстрая декомпрессия Store блоков DEFLATE (без сжатия).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int DecompressZlibStore(ReadOnlySpan<byte> deflateData, Span<byte> output)
    {
        var srcOffset = 0;
        var dstOffset = 0;
        var remaining = deflateData.Length;

        while (remaining > 0)
        {
            // Store block header: BFINAL(1) + BTYPE(2) = 1 byte, LEN(2), NLEN(2) = всего 5 байт
            if (remaining < 5)
            {
                break;
            }

            var header = deflateData[srcOffset];
            var isFinal = (header & 0x01) != 0;
            var btype = (header >> 1) & 0x03;

            // Проверяем что это Store block (BTYPE = 0)
            if (btype != 0)
            {
                // Не Store block — fallback на DeflateStream
                return -1;
            }

            srcOffset++;
            remaining--;

            // LEN (2 bytes, little-endian)
            var len = deflateData[srcOffset] | (deflateData[srcOffset + 1] << 8);
            srcOffset += 2;
            remaining -= 2;

            // NLEN (2 bytes, one's complement of LEN) — пропускаем
            srcOffset += 2;
            remaining -= 2;

            // Копируем данные
            if (remaining < len || output.Length - dstOffset < len)
            {
                break;
            }

            deflateData.Slice(srcOffset, len).CopyTo(output[dstOffset..]);
            srcOffset += len;
            dstOffset += len;
            remaining -= len;

            if (isFinal)
            {
                break;
            }
        }

        return dstOffset;
    }

    #endregion
}
