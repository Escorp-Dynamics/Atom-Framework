#pragma warning disable S109, S3776, MA0051, IDE0010, IDE0047, IDE0048, CA1822
#if DEBUG
#pragma warning disable S2223, S2696
#endif

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Atom.IO;
using Atom.IO.Compression.Huffman;

namespace Atom.Media;

/// <summary>
/// Декодер VP8L (WebP Lossless) битового потока.
/// </summary>
/// <remarks>
/// <para>
/// Реализация спецификации:
/// https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification
/// </para>
/// <para>
/// Архитектурные решения:
/// <list type="bullet">
///   <item>Zero-allocation для Huffman таблиц (managed буферы с GCHandle)</item>
///   <item>LSB-first BitReader (как в DEFLATE)</item>
///   <item>Переиспользование HuffmanDecoder/HuffmanTreeBuilder из Atom.IO.Compression</item>
///   <item>Декодирование в uint[] ARGB буфер, конвертация в VideoFrame на финальном шаге</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class Vp8LDecoder : IDisposable
{
    #region Constants

    /// <summary>Количество символов в алфавите длин кодов.</summary>
    private const int NumCodeLengthCodes = 19;

    /// <summary>Лог2 размера таблицы Хаффмана.</summary>
    private const int TableLog = 11;

    #endregion

#if DEBUG
    // Профилирование: замеры по этапам декодирования (в тиках Stopwatch)
    internal static long DecReadTransformsTicks;
    internal static long DecReadMetaPrefixTicks;
    internal static long DecDecodePixelsTicks;
    internal static long DecInverseTransformsTicks;
    internal static long DecWriteToFrameTicks;
    internal static long DecTotalTicks;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Mark() => Stopwatch.GetTimestamp();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Elapsed(long start) => Stopwatch.GetTimestamp() - start;
#endif

    #region Fields

    private int width;
    private int height;

    /// <summary>Стек применённых преобразований (в порядке чтения).</summary>
    private readonly List<TransformInfo> transforms = [];

    /// <summary>Буфер ARGB пикселей (width * height), арендованный из ArrayPool.</summary>
    private uint[]? pixelBuffer;

    /// <summary>Цветовой кэш (null если кэш не используется).</summary>
    private Vp8LColorCache? colorCache;

    /// <summary>Группы prefix кодов. Каждая группа = 5 таблиц.</summary>
    private PrefixCodeGroup[]? prefixGroups;

    /// <summary>Entropy image для meta prefix codes (null = одна группа).</summary>
    private uint[]? entropyImage;
    private int entropyXSize;
    private int entropyBits;

    private bool disposed;

    #endregion

    #region Public API

    /// <summary>
    /// Декодирует VP8L битовый поток в буфер ARGB пикселей.
    /// </summary>
    /// <param name="vp8lData">Данные VP8L (после chunk header, начиная с signature byte).</param>
    /// <param name="frame">VideoFrame для записи результата.</param>
    /// <returns>Результат декодирования.</returns>
    internal CodecResult Decode(ReadOnlySpan<byte> vp8lData, ref VideoFrame frame)
    {
        if (vp8lData.Length < Vp8LConstants.HeaderSize)
        {
            return CodecResult.InvalidData;
        }

        // Проверяем сигнатуру
        if (vp8lData[0] != Vp8LConstants.Signature)
        {
            return CodecResult.InvalidData;
        }

        // Парсим заголовок: signature(1) + packed(4)
        var packed = BitConverter.ToUInt32(vp8lData.Slice(1, 4));
        width = (int)(packed & 0x3FFF) + 1;
        height = (int)((packed >> 14) & 0x3FFF) + 1;
        var version = (int)((packed >> 29) & 0x7);

        if (version != Vp8LConstants.FormatVersion)
        {
            return CodecResult.InvalidData;
        }

        if (width > Vp8LConstants.MaxImageSize || height > Vp8LConstants.MaxImageSize)
        {
            return CodecResult.InvalidData;
        }

        // Проверяем frame dimensions
        if (frame.Width != width || frame.Height != height)
        {
            return CodecResult.InvalidData;
        }

        return DecodeImageStreamToFrame(vp8lData[Vp8LConstants.HeaderSize..], ref frame);
    }

    internal CodecResult DecodeAlphaImageStream(ReadOnlySpan<byte> imageStreamData, int imageWidth, int imageHeight, Span<byte> alphaOutput)
    {
        if (imageWidth < 1 || imageHeight < 1)
        {
            return CodecResult.InvalidData;
        }

        if (alphaOutput.Length < imageWidth * imageHeight)
        {
            return CodecResult.OutputBufferTooSmall;
        }

        width = imageWidth;
        height = imageHeight;

        var result = DecodeImageStream(imageStreamData);
        if (result != CodecResult.Success)
        {
            return result;
        }

        ExtractGreenToAlpha(alphaOutput);
        return CodecResult.Success;
    }

    private CodecResult DecodeImageStreamToFrame(ReadOnlySpan<byte> imageStreamData, ref VideoFrame frame)
    {
        var result = DecodeImageStream(imageStreamData);
        if (result != CodecResult.Success)
        {
            return result;
        }

        WriteToFrame(ref frame);
        return CodecResult.Success;
    }

    private CodecResult DecodeImageStream(ReadOnlySpan<byte> imageStreamData)
    {
        colorCache = null;
        entropyImage = null;
        entropyXSize = 0;
        entropyBits = 0;

        // Создаём BitReader для VP8L данных (после 5-byte header), LSB-first.
        // VP8L потоки неявно дополнены нулями: libwebp возвращает 0 при чтении за пределами буфера.
        // Добавляем zero-padding, чтобы BitReader не бросал EndOfData на хвостовых Huffman-таблицах.
        const int zeroPadding = 4096; // покрывает чтение до 5 полных Huffman-таблиц за пределами данных
        var paddedLen = imageStreamData.Length + zeroPadding;
        var padded = ArrayPool<byte>.Shared.Rent(paddedLen);
        imageStreamData.CopyTo(padded);
        padded.AsSpan(imageStreamData.Length, padded.Length - imageStreamData.Length).Clear(); // zero-padding
        var reader = new BitReader(padded, lsbFirst: true);

        // Аллоцируем выходной буфер из ArrayPool (снижаем GC давление при покадровом декодировании)
        if (pixelBuffer is not null)
            ArrayPool<uint>.Shared.Return(pixelBuffer);
        pixelBuffer = ArrayPool<uint>.Shared.Rent(width * height);

        try
        {
#if DEBUG
            var tTotal = Mark();
            var t0 = Mark();
#endif
            // 1. Читаем преобразования
            var result = ReadTransforms(ref reader);
            if (result != CodecResult.Success) return result;

            // Эффективные размеры (после ColorIndexing может быть меньше)
            var effectiveWidth = width;
            var effectiveHeight = height;

            // ColorIndexing может изменить ширину
            foreach (var t in transforms)
            {
                if (t.Type == Vp8LConstants.TransformColorIndexing && t.WidthBits > 0)
                {
                    effectiveWidth = Vp8LTransforms.DivRoundUp(effectiveWidth, 1 << t.WidthBits);
                }
            }

            // 2. Читаем color cache info
            var useColorCache = reader.ReadBit();
            var colorCacheBits = 0;

            if (useColorCache)
            {
                colorCacheBits = (int)reader.ReadBits(4);
                if (colorCacheBits is < Vp8LConstants.MinColorCacheBits or > Vp8LConstants.MaxColorCacheBits)
                {
                    return CodecResult.InvalidData;
                }
                colorCache = new Vp8LColorCache(colorCacheBits);
            }
#if DEBUG
            DecReadTransformsTicks = Elapsed(t0);
            t0 = Mark();
#endif

            // 3. Читаем meta prefix codes (entropy image)
            result = ReadMetaPrefixCodes(ref reader, effectiveWidth, effectiveHeight, colorCacheBits);
            if (result != CodecResult.Success) return result;
#if DEBUG
            DecReadMetaPrefixTicks = Elapsed(t0);
            t0 = Mark();
#endif

            // 4. Декодируем пиксели
            result = DecodePixels(ref reader, effectiveWidth, effectiveHeight);
            if (result != CodecResult.Success) return result;
#if DEBUG
            DecDecodePixelsTicks = Elapsed(t0);
            t0 = Mark();
#endif

            // 5. Применяем обратные преобразования (в обратном порядке)
            ApplyInverseTransforms();
#if DEBUG
            DecInverseTransformsTicks = Elapsed(t0);
#endif

#if DEBUG
            DecTotalTicks = Elapsed(tTotal);
#endif

            return CodecResult.Success;
        }
        finally
        {
            DisposeHuffmanTables();
            ArrayPool<byte>.Shared.Return(padded);
        }
    }

    private void ExtractGreenToAlpha(Span<byte> alphaOutput)
    {
        var pixels = pixelBuffer!.AsSpan(0, width * height);
        for (var i = 0; i < pixels.Length; i++)
        {
            alphaOutput[i] = (byte)((pixels[i] >> 8) & 0xFF);
        }
    }

    #endregion

    #region Transforms

    /// <summary>
    /// Читает и сохраняет преобразования из битового потока.
    /// </summary>
    private CodecResult ReadTransforms(ref BitReader reader)
    {
        transforms.Clear();

        while (reader.ReadBit()) // while transform_present
        {
            if (transforms.Count >= Vp8LConstants.MaxTransforms)
            {
                return CodecResult.InvalidData;
            }

            var type = (int)reader.ReadBits(2);

            // Каждый тип может встретиться один раз
            foreach (var existing in transforms)
            {
                if (existing.Type == type)
                    return CodecResult.InvalidData;
            }

            switch (type)
            {
                case Vp8LConstants.TransformPredictor:
                case Vp8LConstants.TransformColor:
                    var sizeBits = (int)reader.ReadBits(3) + 2;
                    var blockSize = 1 << sizeBits;
                    var tw = Vp8LTransforms.DivRoundUp(width, blockSize);
                    var th = Vp8LTransforms.DivRoundUp(height, blockSize);
                    var transformData = new uint[tw * th];
                    var transformResult = DecodeSubImage(ref reader, tw, th, transformData);
                    if (transformResult != CodecResult.Success) return transformResult;
                    transforms.Add(new TransformInfo(type, SizeBits: sizeBits, Data: transformData, Palette: null, WidthBits: 0));
                    break;

                case Vp8LConstants.TransformSubtractGreen:
                    // Нет дополнительных данных
                    transforms.Add(new TransformInfo(type, SizeBits: 0, Data: null, Palette: null, WidthBits: 0));
                    break;

                case Vp8LConstants.TransformColorIndexing:
                    var paletteSize = (int)reader.ReadBits(8) + 1;
                    var palette = new uint[paletteSize];

                    // Палитра кодируется как sub-image (paletteSize x 1)
                    var paletteResult = DecodeSubImage(ref reader, paletteSize, 1, palette);
                    if (paletteResult != CodecResult.Success) return paletteResult;

                    // Палитра хранится как дельта-кодирование
                    for (var i = 1; i < paletteSize; i++)
                    {
                        palette[i] = Vp8LTransforms.AddArgb(palette[i], palette[i - 1]);
                    }

                    // Определяем widthBits для упаковки пикселей
                    var widthBits = paletteSize switch
                    {
                        <= 2 => 3,  // 8 пикселей в одном green байте (1 bit each)
                        <= 4 => 2,  // 4 пикселя (2 bits each)
                        <= 16 => 1, // 2 пикселя (4 bits each)
                        _ => 0,     // Нет упаковки
                    };

                    transforms.Add(new TransformInfo(type, SizeBits: 0, Data: null, Palette: palette, WidthBits: widthBits));
                    break;

                default:
                    return CodecResult.InvalidData;
            }
        }

        return CodecResult.Success;
    }

    /// <summary>
    /// Применяет обратные преобразования в обратном порядке.
    /// </summary>
    private void ApplyInverseTransforms()
    {
        var pixels = pixelBuffer.AsSpan(0, width * height);

        for (var i = transforms.Count - 1; i >= 0; i--)
        {
            var t = transforms[i];

            switch (t.Type)
            {
                case Vp8LConstants.TransformSubtractGreen:
                    Vp8LTransforms.InverseSubtractGreen(pixels);
                    break;

                case Vp8LConstants.TransformPredictor:
                    Vp8LTransforms.InversePredictor(pixels, width, height, t.Data, t.SizeBits);
                    break;

                case Vp8LConstants.TransformColor:
                    Vp8LTransforms.InverseColorTransform(pixels, width, height, t.Data, t.SizeBits);
                    break;

                case Vp8LConstants.TransformColorIndexing:
                    Vp8LTransforms.InverseColorIndexing(pixels, width, height, t.Palette, t.WidthBits);
                    break;
            }
        }
    }

    #endregion

    #region Meta Prefix Codes

    /// <summary>
    /// Читает информацию о meta prefix codes (entropy image).
    /// </summary>
    private CodecResult ReadMetaPrefixCodes(
        ref BitReader reader,
        int effectiveWidth, int effectiveHeight,
        int colorCacheBits)
    {
        var useMeta = reader.ReadBit();

        if (useMeta)
        {
            // Читаем entropy image
            entropyBits = (int)reader.ReadBits(3) + 2;
            var blockSize = 1 << entropyBits;
            entropyXSize = Vp8LTransforms.DivRoundUp(effectiveWidth, blockSize);
            var entropyYSize = Vp8LTransforms.DivRoundUp(effectiveHeight, blockSize);

            entropyImage = new uint[entropyXSize * entropyYSize];
            var result = DecodeSubImage(ref reader, entropyXSize, entropyYSize, entropyImage);
            if (result != CodecResult.Success) return result;

            // Подсчитываем количество различных prefix groups
            var maxGroupIndex = 0;
            for (var i = 0; i < entropyImage.Length; i++)
            {
                var groupIndex = (int)((entropyImage[i] >> 8) & 0xFFFF);
                if (groupIndex > maxGroupIndex) maxGroupIndex = groupIndex;
            }

            var numGroups = maxGroupIndex + 1;
            return ReadPrefixCodeGroups(ref reader, numGroups, colorCacheBits);
        }

        // Одна prefix code группа
        return ReadPrefixCodeGroups(ref reader, 1, colorCacheBits);
    }

    /// <summary>
    /// Читает все группы prefix codes.
    /// </summary>
    private CodecResult ReadPrefixCodeGroups(
        ref BitReader reader,
        int numGroups,
        int colorCacheBits)
    {
        var colorCacheSize = colorCacheBits > 0 ? 1 << colorCacheBits : 0;
        var greenAlphabetSize = Vp8LConstants.NumLiteralSymbols + Vp8LConstants.NumLengthPrefixCodes + colorCacheSize;

        prefixGroups = new PrefixCodeGroup[numGroups];

        for (var g = 0; g < numGroups; g++)
        {
            var group = new PrefixCodeGroup();

            // Green (16-bit: до 2328 символов)
            var result = ReadHuffmanCode16(ref reader, greenAlphabetSize, out group.Green);
            if (result != CodecResult.Success) return result;

            // Red (8-bit: 256)
            result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeColor, out group.Red);
            if (result != CodecResult.Success) return result;

            // Blue (8-bit: 256)
            result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeColor, out group.Blue);
            if (result != CodecResult.Success) return result;

            // Alpha (8-bit: 256)
            result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeColor, out group.Alpha);
            if (result != CodecResult.Success) return result;

            // Distance (8-bit: 40)
            result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeDistance, out group.Distance);
            if (result != CodecResult.Success) return result;

            prefixGroups[g] = group;
        }

        return CodecResult.Success;
    }

    #endregion

    #region Huffman Code Reading

    /// <summary>
    /// Читает одну Huffman таблицу (8-bit символы) из битового потока.
    /// </summary>
    private static CodecResult ReadHuffmanCode(
        ref BitReader reader,
        int alphabetSize,
        out HuffmanTableBuffer? table)
    {
        var isSimple = reader.ReadBit();

        if (isSimple)
        {
            return ReadSimpleHuffmanCode(ref reader, alphabetSize, out table);
        }

        return ReadNormalHuffmanCode(ref reader, alphabetSize, out table);
    }

    /// <summary>
    /// Читает одну Huffman таблицу с 16-bit символами из битового потока.
    /// </summary>
    private static CodecResult ReadHuffmanCode16(
        ref BitReader reader,
        int alphabetSize,
        out HuffmanTableBuffer16? table)
    {
        var isSimple = reader.ReadBit();

        if (isSimple)
        {
            return ReadSimpleHuffmanCode16(ref reader, alphabetSize, out table);
        }

        return ReadNormalHuffmanCode16(ref reader, alphabetSize, out table);
    }

    /// <summary>
    /// Simple code length: 1-2 символа напрямую.
    /// </summary>
    private static CodecResult ReadSimpleHuffmanCode(
        ref BitReader reader,
        int alphabetSize,
        out HuffmanTableBuffer? table)
    {
        table = null;
        var numSymbols = (int)reader.ReadBits(1) + 1;
        var firstSymbolBits = reader.ReadBit() ? 8 : 1;
        var symbol0 = (int)reader.ReadBits(firstSymbolBits);

        if (numSymbols == 1)
        {
            // VP8L spec: единственный символ → code length 0, декодирование не потребляет бит.
            // libwebp допускает symbol0 >= alphabetSize для 1-символьного кода
            // (напр. Distance таблица с placeholder символом, который никогда не используется).
            table = new HuffmanTableBuffer(TableLog, alphabetSize);
            table.Symbols.Fill((byte)symbol0);
            table.Lengths.Clear();
            table.BuildPackedTable();
            return CodecResult.Success;
        }

        if (symbol0 >= alphabetSize) return CodecResult.InvalidData;

        var symbol1 = (int)reader.ReadBits(8);
        if (symbol1 >= alphabetSize) return CodecResult.InvalidData;

        Span<byte> codeLengths = stackalloc byte[alphabetSize];
        codeLengths.Clear();
        codeLengths[symbol0] = 1;
        codeLengths[symbol1] = 1;

        table = new HuffmanTableBuffer(TableLog, alphabetSize);
        HuffmanTreeBuilder.BuildDecodeTable(codeLengths, table.Symbols, table.Lengths, TableLog, lsbFirst: true);
        table.BuildPackedTable();
        return CodecResult.Success;
    }

    /// <summary>
    /// Simple code length 16-bit: 1-2 символа напрямую.
    /// </summary>
    private static CodecResult ReadSimpleHuffmanCode16(
        ref BitReader reader,
        int alphabetSize,
        out HuffmanTableBuffer16? table)
    {
        table = null;
        var numSymbols = (int)reader.ReadBits(1) + 1;
        var firstSymbolBits = reader.ReadBit() ? 8 : 1;
        var symbol0 = (int)reader.ReadBits(firstSymbolBits);

        if (numSymbols == 1)
        {
            // VP8L spec: единственный символ → code length 0, декодирование не потребляет бит.
            // libwebp допускает symbol0 >= alphabetSize для 1-символьного кода.
            table = new HuffmanTableBuffer16(TableLog, alphabetSize);
            table.Symbols.Fill((ushort)symbol0);
            table.Lengths.Clear();
            table.BuildPackedTable();
            return CodecResult.Success;
        }

        if (symbol0 >= alphabetSize) return CodecResult.InvalidData;

        var symbol1 = (int)reader.ReadBits(8);
        if (symbol1 >= alphabetSize) return CodecResult.InvalidData;

        Span<byte> codeLengths = stackalloc byte[alphabetSize];
        codeLengths.Clear();
        codeLengths[symbol0] = 1;
        codeLengths[symbol1] = 1;

        table = new HuffmanTableBuffer16(TableLog, alphabetSize);
        HuffmanTreeBuilder.BuildDecodeTable16(codeLengths, table.Symbols, table.Lengths, TableLog, lsbFirst: true);
        table.BuildPackedTable();
        return CodecResult.Success;
    }

    /// <summary>
    /// Normal code length: полная таблица длин через 19-символьную code length мета-таблицу.
    /// </summary>
    private static CodecResult ReadNormalHuffmanCode(
        ref BitReader reader,
        int alphabetSize,
        out HuffmanTableBuffer? table)
    {
        table = null;

        // Читаем code lengths для code length алфавита
        var numCodeLengthCodes = (int)reader.ReadBits(4) + 4;
        if (numCodeLengthCodes > NumCodeLengthCodes) return CodecResult.InvalidData;

        Span<byte> clCodeLengths = stackalloc byte[NumCodeLengthCodes];
        clCodeLengths.Clear();

        for (var i = 0; i < numCodeLengthCodes; i++)
        {
            clCodeLengths[Vp8LConstants.CodeLengthCodeOrder[i]] = (byte)reader.ReadBits(3);
        }

        // Строим мета-таблицу для чтения длин основных кодов
        using var clBuffer = new HuffmanTableBuffer(7, NumCodeLengthCodes);
        HuffmanTreeBuilder.BuildDecodeTable(clCodeLengths, clBuffer.Symbols, clBuffer.Lengths, 7, lsbFirst: true);
        var clTable = clBuffer.ToTable();

        // max_symbol
        var maxSymbol = alphabetSize;
        if (reader.ReadBit())
        {
            var lengthNbits = (int)(2 + 2 * reader.ReadBits(3));
            maxSymbol = (int)(2 + reader.ReadBits(lengthNbits));
            if (maxSymbol > alphabetSize) maxSymbol = alphabetSize;
        }

        // Читаем длины кодов основного алфавита
        var codeLengths = new byte[alphabetSize];

        var result = ReadCodeLengths(ref reader, clTable, codeLengths, maxSymbol);
        if (result != CodecResult.Success) return result;

        table = new HuffmanTableBuffer(TableLog, alphabetSize);
        HuffmanTreeBuilder.BuildDecodeTable(codeLengths, table.Symbols, table.Lengths, TableLog, lsbFirst: true);
        table.BuildPackedTable();
        return CodecResult.Success;
    }

    /// <summary>
    /// Normal code length 16-bit: полная таблица длин через code length мета-таблицу.
    /// </summary>
    private static CodecResult ReadNormalHuffmanCode16(
        ref BitReader reader,
        int alphabetSize,
        out HuffmanTableBuffer16? table)
    {
        table = null;

        var numCodeLengthCodes = (int)reader.ReadBits(4) + 4;
        if (numCodeLengthCodes > NumCodeLengthCodes) return CodecResult.InvalidData;

        Span<byte> clCodeLengths = stackalloc byte[NumCodeLengthCodes];
        clCodeLengths.Clear();

        for (var i = 0; i < numCodeLengthCodes; i++)
        {
            clCodeLengths[Vp8LConstants.CodeLengthCodeOrder[i]] = (byte)reader.ReadBits(3);
        }

        using var clBuffer = new HuffmanTableBuffer(7, NumCodeLengthCodes);
        HuffmanTreeBuilder.BuildDecodeTable(clCodeLengths, clBuffer.Symbols, clBuffer.Lengths, 7, lsbFirst: true);
        var clTable = clBuffer.ToTable();

        var maxSymbol = alphabetSize;
        if (reader.ReadBit())
        {
            var lengthNbits = (int)(2 + 2 * reader.ReadBits(3));
            maxSymbol = (int)(2 + reader.ReadBits(lengthNbits));
            if (maxSymbol > alphabetSize) maxSymbol = alphabetSize;
        }

        // VP8L alphabetSize может быть до 2328, heap-аллокация для ref safety
        var codeLengths = new byte[alphabetSize];

        var result = ReadCodeLengths(ref reader, clTable, codeLengths, maxSymbol);
        if (result != CodecResult.Success) return result;

        table = new HuffmanTableBuffer16(TableLog, alphabetSize);
        HuffmanTreeBuilder.BuildDecodeTable16(codeLengths, table.Symbols, table.Lengths, TableLog, lsbFirst: true);
        table.BuildPackedTable();
        return CodecResult.Success;
    }

    /// <summary>
    /// Декодирует длины кодов из битового потока через code length мета-таблицу.
    /// Коды 16/17/18 — повторение и нули (идентично DEFLATE).
    /// </summary>
    private static CodecResult ReadCodeLengths(
        ref BitReader reader,
        in HuffmanTable clTable,
        byte[] codeLengths,
        int maxSymbol)
    {
        var prevCodeLength = (byte)8;
        var i = 0;

        while (i < maxSymbol)
        {
            var symbol = HuffmanDecoder.Decode(ref reader, clTable);

            if (symbol < 16)
            {
                // Литеральная длина кода
                codeLengths[i++] = symbol;
                if (symbol != 0) prevCodeLength = symbol;
            }
            else if (symbol == 16)
            {
                // Повтор предыдущей длины 3-6 раз
                var repeat = (int)reader.ReadBits(2) + 3;

                // libwebp clamps repeat to max_symbol - symbol (не ошибка)
                if (i + repeat > maxSymbol) repeat = maxSymbol - i;

                for (var r = 0; r < repeat; r++)
                    codeLengths[i++] = prevCodeLength;
            }
            else if (symbol == 17)
            {
                // Повтор нуля 3-10 раз
                var repeat = (int)reader.ReadBits(3) + 3;

                // libwebp clamps repeat to max_symbol - symbol
                if (i + repeat > maxSymbol) repeat = maxSymbol - i;

                i += repeat; // codeLengths уже проинициализирован нулями
            }
            else if (symbol == 18)
            {
                // Повтор нуля 11-138 раз
                var repeat = (int)reader.ReadBits(7) + 11;

                // libwebp clamps repeat to max_symbol - symbol
                if (i + repeat > maxSymbol) repeat = maxSymbol - i;

                i += repeat;
            }
            else
            {
                return CodecResult.InvalidData;
            }
        }

        return CodecResult.Success;
    }

    #endregion

    #region Pixel Decoding

    /// <summary>
    /// Декодирует основное изображение (пиксели) из битового потока.
    /// Fused decode: DecodeLsb совмещает EnsureBits + PeekBits + table lookup + SkipBits.
    /// </summary>
    private unsafe CodecResult DecodePixels(ref BitReader reader, int effectiveWidth, int effectiveHeight)
    {
        var totalPixels = effectiveWidth * effectiveHeight;
        var pixels = pixelBuffer!;
        var pos = 0;

        // Single prefix group fast path (no entropy image) — избегаем GetPrefixGroup lookup
        var singleGroup = entropyImage is null;
        var group0 = prefixGroups![0];

        // Предкэшируем packed pointers для fused decode (все таблицы имеют одинаковый TableLog)
        var greenT = group0.Green!.ToTable();
        var tl = greenT.TableLog;
        var m = greenT.Mask;
        var greenP = greenT.PackedPtr;
        var redP = group0.Red!.ToTable().PackedPtr;
        var blueP = group0.Blue!.ToTable().PackedPtr;
        var alphaP = group0.Alpha!.ToTable().PackedPtr;
        var distP = group0.Distance!.ToTable().PackedPtr;
        var lastGroupIdx = 0;

        var cache = colorCache;
        var hasCache = cache is not null;

        // Pointer-based pixel access: исключаем bounds checking в hot loop
        fixed (uint* pixelBase = pixels)
        {
            while (pos < totalPixels)
            {
                // При наличии entropy image проверяем смену группы
                if (!singleGroup)
                {
                    var groupIdx = GetPrefixGroupIndex(pos, effectiveWidth);
                    if (groupIdx != lastGroupIdx)
                    {
                        lastGroupIdx = groupIdx;
                        var g = prefixGroups![groupIdx];
                        greenP = g.Green!.ToTable().PackedPtr;
                        redP = g.Red!.ToTable().PackedPtr;
                        blueP = g.Blue!.ToTable().PackedPtr;
                        alphaP = g.Alpha!.ToTable().PackedPtr;
                        distP = g.Distance!.ToTable().PackedPtr;
                    }
                }

                // Fused decode: 1 branch + 1 packed table read
                var greenSym = reader.DecodeLsb(tl, greenP, m);

                if (greenSym < Vp8LConstants.NumLiteralSymbols)
                {
                    // Batch literal loop: декодируем подряд идущие литералы без повторного branch
                    var batchStart = pos;

                    do
                    {
                        pixelBase[pos] = reader.DecodeLiteral4Lsb(greenSym, tl, m, redP, blueP, alphaP);
                        pos++;

                        if (pos >= totalPixels || !singleGroup)
                            break;

                        greenSym = reader.DecodeLsb(tl, greenP, m);
                    }
                    while (greenSym < Vp8LConstants.NumLiteralSymbols);

                    // Batch cache insert для всех литералов серии
                    if (hasCache)
                        cache!.InsertBatch(pixelBase + batchStart, pos - batchStart);

                    // Если последний greenSym был literal или данные кончились — возврат к основному циклу
                    if (greenSym < Vp8LConstants.NumLiteralSymbols || pos >= totalPixels)
                        continue;

                    // Иначе fall-through: greenSym — backref или cache, обрабатываем ниже
                }

                if (greenSym < Vp8LConstants.NumLiteralSymbols + Vp8LConstants.NumLengthPrefixCodes)
                {
                    // LZ77 back-reference
                    var lengthCode = greenSym - Vp8LConstants.NumLiteralSymbols;
                    var extraBitsCount = Vp8LConstants.PrefixCodeExtraBits(lengthCode);
                    var extraBitsValue = extraBitsCount > 0 ? reader.ReadBits(extraBitsCount) : 0u;
                    var length = Vp8LConstants.PrefixCodeToValue(lengthCode, extraBitsValue);

                    // Декодируем distance
                    var distCode = reader.DecodeLsb(tl, distP, m);
                    var distExtraBits = Vp8LConstants.PrefixCodeExtraBits(distCode);
                    var distExtraValue = distExtraBits > 0 ? reader.ReadBits(distExtraBits) : 0u;
                    var distanceCode = Vp8LConstants.PrefixCodeToValue(distCode, distExtraValue);

                    // Применяем distance mapping
                    var distance = DistanceToPixelOffset(distanceCode, effectiveWidth);
                    if (distance <= 0 || pos - distance < 0)
                    {
                        return CodecResult.InvalidData;
                    }

                    // Копируем length пикселей
                    var srcOffset = pos - distance;
                    var copyLen = Math.Min(length, totalPixels - pos);

                    if (hasCache)
                    {
                        // Bulk copy + batch cache update (лучше locality, чем interleaved)
                        if (distance >= copyLen)
                        {
                            Buffer.MemoryCopy(pixelBase + srcOffset, pixelBase + pos, (uint)copyLen * 4, (uint)copyLen * 4);
                        }
                        else
                        {
                            for (var j = 0; j < copyLen; j++)
                                pixelBase[pos + j] = pixelBase[srcOffset + j];
                        }

                        cache!.InsertBatch(pixelBase + pos, copyLen);
                        pos += copyLen;
                    }
                    else
                    {
                        // Без color cache: bulk copy (если distance > length, нет перекрытия)
                        if (distance >= copyLen)
                        {
                            Buffer.MemoryCopy(pixelBase + srcOffset, pixelBase + pos, (uint)copyLen * 4, (uint)copyLen * 4);
                        }
                        else
                        {
                            for (var j = 0; j < copyLen; j++)
                                pixelBase[pos + j] = pixelBase[srcOffset + j];
                        }
                        pos += copyLen;
                    }
                }
                else
                {
                    // Color cache lookup
                    var cacheIndex = greenSym - Vp8LConstants.NumLiteralSymbols - Vp8LConstants.NumLengthPrefixCodes;

                    if (!hasCache)
                    {
                        return CodecResult.InvalidData;
                    }

                    var argb = cache!.Lookup(cacheIndex);
                    pixelBase[pos] = argb;
                    cache.Insert(argb);
                    pos++;
                }
            }
        }

        return CodecResult.Success;
    }

    /// <summary>
    /// Декодирует sub-image (для transform data и entropy image).
    /// Использует тот же алгоритм, но без transform и meta prefix codes.
    /// </summary>
    private static unsafe CodecResult DecodeSubImage(ref BitReader reader, int subWidth, int subHeight, Span<uint> output)
    {
        // Color cache для sub-image
        Vp8LColorCache? subColorCache = null;
        var useColorCache = reader.ReadBit();
        var colorCacheBits = 0;

        if (useColorCache)
        {
            colorCacheBits = (int)reader.ReadBits(4);
            if (colorCacheBits is < Vp8LConstants.MinColorCacheBits or > Vp8LConstants.MaxColorCacheBits)
            {
                return CodecResult.InvalidData;
            }
            subColorCache = new Vp8LColorCache(colorCacheBits);
        }

        // Для sub-image нет meta prefix codes
        var colorCacheSize = colorCacheBits > 0 ? 1 << colorCacheBits : 0;
        var greenAlphabetSize = Vp8LConstants.NumLiteralSymbols + Vp8LConstants.NumLengthPrefixCodes + colorCacheSize;

        // Читаем одну группу prefix кодов
        var group = new PrefixCodeGroup();

        var result = ReadHuffmanCode16(ref reader, greenAlphabetSize, out group.Green);
        if (result != CodecResult.Success) return result;

        result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeColor, out group.Red);
        if (result != CodecResult.Success) return result;

        result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeColor, out group.Blue);
        if (result != CodecResult.Success) return result;

        result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeColor, out group.Alpha);
        if (result != CodecResult.Success) return result;

        result = ReadHuffmanCode(ref reader, Vp8LConstants.AlphabetSizeDistance, out group.Distance);
        if (result != CodecResult.Success) return result;

        try
        {
            // Предкэшируем packed pointers для fused decode
            var greenT = group.Green!.ToTable();
            var tl = greenT.TableLog;
            var m = greenT.Mask;
            var greenP = greenT.PackedPtr;
            var redP = group.Red!.ToTable().PackedPtr;
            var blueP = group.Blue!.ToTable().PackedPtr;
            var alphaP = group.Alpha!.ToTable().PackedPtr;
            var distP = group.Distance!.ToTable().PackedPtr;

            // Декодируем пиксели sub-image
            var totalPixels = subWidth * subHeight;
            var pos = 0;

            while (pos < totalPixels)
            {
                var greenSym = reader.DecodeLsb(tl, greenP, m);

                if (greenSym < Vp8LConstants.NumLiteralSymbols)
                {
                    var red = reader.DecodeLsb(tl, redP, m);
                    var blue = reader.DecodeLsb(tl, blueP, m);
                    var alpha = reader.DecodeLsb(tl, alphaP, m);

                    var argb = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)greenSym << 8) | (uint)blue;
                    output[pos] = argb;
                    subColorCache?.Insert(argb);
                    pos++;
                }
                else if (greenSym < Vp8LConstants.NumLiteralSymbols + Vp8LConstants.NumLengthPrefixCodes)
                {
                    var lengthCode = greenSym - Vp8LConstants.NumLiteralSymbols;
                    var extraBitsCount = Vp8LConstants.PrefixCodeExtraBits(lengthCode);
                    var extraBitsValue = extraBitsCount > 0 ? reader.ReadBits(extraBitsCount) : 0u;
                    var length = Vp8LConstants.PrefixCodeToValue(lengthCode, extraBitsValue);

                    var distCode = reader.DecodeLsb(tl, distP, m);
                    var distExtraBits = Vp8LConstants.PrefixCodeExtraBits(distCode);
                    var distExtraValue = distExtraBits > 0 ? reader.ReadBits(distExtraBits) : 0u;
                    var distanceCode = Vp8LConstants.PrefixCodeToValue(distCode, distExtraValue);

                    var distance = DistanceToPixelOffset(distanceCode, subWidth);
                    if (distance <= 0 || pos - distance < 0)
                    {
                        return CodecResult.InvalidData;
                    }

                    var srcOffset = pos - distance;
                    for (var j = 0; j < length && pos < totalPixels; j++)
                    {
                        var pixel = output[srcOffset + j];
                        output[pos] = pixel;
                        subColorCache?.Insert(pixel);
                        pos++;
                    }
                }
                else
                {
                    // Color cache lookup
                    var cacheIndex = greenSym - Vp8LConstants.NumLiteralSymbols - Vp8LConstants.NumLengthPrefixCodes;

                    if (subColorCache is null)
                    {
                        return CodecResult.InvalidData;
                    }

                    var argb = subColorCache.Lookup(cacheIndex);
                    output[pos] = argb;
                    subColorCache.Insert(argb);
                    pos++;
                }
            }

            return CodecResult.Success;
        }
        finally
        {
            group.Dispose();
        }
    }

    #endregion

    #region Distance Mapping

    /// <summary>
    /// Преобразует VP8L distance code в смещение в пикселях.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DistanceToPixelOffset(int distanceCode, int imageWidth)
    {
        if (distanceCode > Vp8LConstants.NumDistanceMapEntries)
        {
            // Прямое расстояние в scanline порядке
            return distanceCode - Vp8LConstants.NumDistanceMapEntries;
        }

        // 2D distance mapping
        var mapIndex = (distanceCode - 1) * 2;
        var distMap = Vp8LConstants.DistanceMap;
        var dx = (int)distMap[mapIndex];
        var dy = (int)distMap[mapIndex + 1];
        var distance = dx + dy * imageWidth;

        // Расстояние должно быть >= 1
        return distance < 1 ? 1 : distance;
    }

    #endregion

    #region Prefix Group Lookup

    /// <summary>
    /// Возвращает группу prefix codes для данной позиции пикселя.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PrefixCodeGroup GetPrefixGroup(int pixelPos, int effectiveWidth)
    {
        if (entropyImage is null || prefixGroups is null)
        {
            return prefixGroups![0];
        }

        return prefixGroups[GetPrefixGroupIndex(pixelPos, effectiveWidth)];
    }

    /// <summary>
    /// Возвращает индекс группы prefix codes для данной позиции пикселя.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetPrefixGroupIndex(int pixelPos, int effectiveWidth)
    {
        var x = pixelPos % effectiveWidth;
        var y = pixelPos / effectiveWidth;
        var tileX = x >> entropyBits;
        var tileY = y >> entropyBits;
        var tileIndex = tileY * entropyXSize + tileX;

        return (int)((entropyImage![tileIndex] >> 8) & 0xFFFF);
    }

    #endregion

    #region Output Conversion

    /// <summary>
    /// Конвертирует внутренний ARGB буфер в VideoFrame (RGB24 или RGBA32).
    /// </summary>
    private void WriteToFrame(ref VideoFrame frame)
    {
        var pixels = pixelBuffer!;
        var destData = frame.PackedData;

        if (frame.PixelFormat == VideoPixelFormat.Rgba32)
        {
            // ARGB → RGBA: byte shuffle [A,R,G,B] → [R,G,B,A]
            // В ARGB (uint, little-endian): byte[0]=B, byte[1]=G, byte[2]=R, byte[3]=A
            // В RGBA32 (byte layout): byte[0]=R, byte[1]=G, byte[2]=B, byte[3]=A
            // Shuffle: [B,G,R,A] → [R,G,B,A] = indices [2,1,0,3]
            ref var pixelRef = ref MemoryMarshal.GetArrayDataReference(pixels);

            for (var y = 0; y < height; y++)
            {
                var dstRow = destData.GetRow(y);
                var srcOffset = (uint)(y * width);
                ref var srcRef = ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref pixelRef, srcOffset));
                ref var dstRef = ref MemoryMarshal.GetReference(dstRow);
                var x = 0;

                if (Vector256.IsHardwareAccelerated && width >= 8)
                {
                    // 8 pixels per iteration: ARGB (BGRA in memory) → RGBA
                    var shuffleMask = Vector256.Create(
                        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15,
                        18, 17, 16, 19, 22, 21, 20, 23, 26, 25, 24, 27, 30, 29, 28, 31);

                    for (; x + 8 <= width; x += 8)
                    {
                        var src = Vector256.LoadUnsafe(ref srcRef, (nuint)(x * 4));
                        var shuffled = Vector256.Shuffle(src, shuffleMask);
                        shuffled.StoreUnsafe(ref dstRef, (nuint)(x * 4));
                    }
                }
                else if (Vector128.IsHardwareAccelerated && width >= 4)
                {
                    var shuffleMask = Vector128.Create(
                        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);

                    for (; x + 4 <= width; x += 4)
                    {
                        var src = Vector128.LoadUnsafe(ref srcRef, (nuint)(x * 4));
                        var shuffled = Vector128.Shuffle(src, shuffleMask);
                        shuffled.StoreUnsafe(ref dstRef, (nuint)(x * 4));
                    }
                }

                // Scalar tail
                for (; x < width; x++)
                {
                    var argb = pixels[srcOffset + (uint)x];
                    var dstIdx = x * 4;
                    Unsafe.Add(ref dstRef, dstIdx) = (byte)((argb >> 16) & 0xFF);
                    Unsafe.Add(ref dstRef, dstIdx + 1) = (byte)((argb >> 8) & 0xFF);
                    Unsafe.Add(ref dstRef, dstIdx + 2) = (byte)(argb & 0xFF);
                    Unsafe.Add(ref dstRef, dstIdx + 3) = (byte)((argb >> 24) & 0xFF);
                }
            }
        }
        else
        {
            // RGB24
            for (var y = 0; y < height; y++)
            {
                var dstRow = destData.GetRow(y);
                var srcOffset = y * width;

                for (var x = 0; x < width; x++)
                {
                    var argb = pixels[srcOffset + x];
                    var dstIdx = x * 3;

                    dstRow[dstIdx] = (byte)((argb >> 16) & 0xFF);     // R
                    dstRow[dstIdx + 1] = (byte)((argb >> 8) & 0xFF);  // G
                    dstRow[dstIdx + 2] = (byte)(argb & 0xFF);         // B
                }
            }
        }
    }

    #endregion

    #region Resource Management

    /// <summary>
    /// Освобождает все HuffmanTableBuffer ресурсы.
    /// </summary>
    private void DisposeHuffmanTables()
    {
        if (prefixGroups is not null)
        {
            for (var i = 0; i < prefixGroups.Length; i++)
            {
                prefixGroups[i].Dispose();
            }
            prefixGroups = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        DisposeHuffmanTables();

        if (pixelBuffer is not null)
        {
            ArrayPool<uint>.Shared.Return(pixelBuffer);
            pixelBuffer = null;
        }
        colorCache = null;
        entropyImage = null;
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Информация о применённом преобразовании.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TransformInfo(
        int Type,
        int SizeBits,
        uint[]? Data,
        uint[]? Palette,
        int WidthBits);

    /// <summary>
    /// Группа из 5 prefix кодов (Green, Red, Blue, Alpha, Distance).
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct PrefixCodeGroup : IDisposable
    {
        internal HuffmanTableBuffer16? Green;
        internal HuffmanTableBuffer? Red;
        internal HuffmanTableBuffer? Blue;
        internal HuffmanTableBuffer? Alpha;
        internal HuffmanTableBuffer? Distance;

        public void Dispose()
        {
            Green?.Dispose();
            Red?.Dispose();
            Blue?.Dispose();
            Alpha?.Dispose();
            Distance?.Dispose();

            Green = null;
            Red = null;
            Blue = null;
            Alpha = null;
            Distance = null;
        }
    }

    #endregion
}