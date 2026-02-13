#pragma warning disable IDE0010, S109, S3776, CA1822, MA0038

using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Высокопроизводительный кодек для формата PNG (Portable Network Graphics).
/// </summary>
/// <remarks>
/// <para>
/// Поддерживает:
/// <list type="bullet">
///   <item>Grayscale (1, 2, 4, 8, 16 бит)</item>
///   <item>RGB24 (truecolor без альфа-канала)</item>
///   <item>RGBA32 (truecolor с альфа-каналом)</item>
///   <item>Indexed (palette-based, 1-8 бит)</item>
///   <item>Grayscale + Alpha</item>
/// </list>
/// </para>
/// <para>
/// Оптимизации:
/// <list type="bullet">
///   <item>SIMD фильтрация (AVX2/SSE4.1) для Sub, Up, Average, Paeth</item>
///   <item>Zero-allocation декодирование в предоставленный буфер</item>
///   <item>Параллельное сжатие для больших изображений</item>
///   <item>Интеграция с ColorSpaces для конвертации форматов</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class PngCodec : IImageCodec
{
    #region Constants

    /// <summary>Сигнатура PNG файла (8 байт).</summary>
    internal static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Размер сигнатуры PNG.</summary>
    internal const int SignatureSize = 8;

    /// <summary>Размер заголовка чанка (length + type).</summary>
    internal const int ChunkHeaderSize = 8;

    /// <summary>Размер IHDR данных (без заголовка и CRC).</summary>
    internal const int IhdrDataSize = 13;

    #endregion

    #region Chunk Types

    internal const uint ChunkIhdr = 0x49484452; // "IHDR"
    internal const uint ChunkPlte = 0x504C5445; // "PLTE"
    internal const uint ChunkIdat = 0x49444154; // "IDAT"
    internal const uint ChunkIend = 0x49454E44; // "IEND"
    internal const uint ChunkTrns = 0x74524E53; // "tRNS"
    internal const uint ChunkGama = 0x67414D41; // "gAMA"
    internal const uint ChunkSrgb = 0x73524742; // "sRGB"
    internal const uint ChunkChrm = 0x6348524D; // "cHRM"
    internal const uint ChunkIccp = 0x69434350; // "iCCP"
    internal const uint ChunkText = 0x74455874; // "tEXt"
    internal const uint ChunkZtxt = 0x7A545874; // "zTXt"
    internal const uint ChunkItxt = 0x69545874; // "iTXt"
    internal const uint ChunkBkgd = 0x624B4744; // "bKGD"
    internal const uint ChunkPhys = 0x70485973; // "pHYs"
    internal const uint ChunkTime = 0x74494D45; // "tIME"

    #endregion

    #region Color Types

    /// <summary>Grayscale (1, 2, 4, 8, 16 бит).</summary>
    internal const byte ColorTypeGrayscale = 0;

    /// <summary>RGB (8, 16 бит на канал).</summary>
    internal const byte ColorTypeRgb = 2;

    /// <summary>Indexed (palette, 1-8 бит).</summary>
    internal const byte ColorTypeIndexed = 3;

    /// <summary>Grayscale + Alpha (8, 16 бит на канал).</summary>
    internal const byte ColorTypeGrayscaleAlpha = 4;

    /// <summary>RGBA (8, 16 бит на канал).</summary>
    internal const byte ColorTypeRgba = 6;

    #endregion

    #region Filter Types

    internal const byte FilterNone = 0;
    internal const byte FilterSub = 1;
    internal const byte FilterUp = 2;
    internal const byte FilterAverage = 3;
    internal const byte FilterPaeth = 4;

    #endregion

    #region Fields

    private bool isEncoder;
    private bool isInitialized;
    private bool isDisposed;

    #endregion

    #region ICodec Properties

    /// <inheritdoc/>
    public MediaCodecId CodecId => MediaCodecId.Png;

    /// <inheritdoc/>
    public string Name => "PNG (Portable Network Graphics)";

    /// <inheritdoc/>
    public CodecCapabilities Capabilities => CodecCapabilities.Decode | CodecCapabilities.Encode;

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public IMeterFactory? MeterFactory { get; set; }

    /// <inheritdoc/>
    public HardwareAcceleration Acceleration { get; init; } = HardwareAcceleration.Auto;

    #endregion

    #region IImageCodec Properties

    /// <inheritdoc/>
    public ImageCodecParameters Parameters { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedExtensions { get; } = [".png"];

    /// <inheritdoc/>
    public string MimeType => "image/png";

    #endregion

    #region Initialization

    /// <inheritdoc/>
    public CodecResult InitializeDecoder(ImageCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        Parameters = parameters;
        isEncoder = false;
        isInitialized = true;

        Logger?.LogPngInitialized("Decoder", parameters.Width, parameters.Height);
        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public CodecResult InitializeEncoder(ImageCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.Width <= 0 || parameters.Height <= 0)
        {
            Logger?.LogPngError("Некорректные размеры");
            return CodecResult.InvalidData;
        }

        // Поддерживаем только RGB24 и RGBA32 packed форматы для encoding
        if (parameters.PixelFormat is not (VideoPixelFormat.Rgb24 or VideoPixelFormat.Rgba32))
        {
            Logger?.LogPngError("Неподдерживаемый формат пикселей для encoding");
            return CodecResult.UnsupportedFormat;
        }

        Parameters = parameters;
        isEncoder = true;
        isInitialized = true;

        Logger?.LogPngInitialized("Encoder", parameters.Width, parameters.Height);
        return CodecResult.Success;
    }

    #endregion

    #region Utility Methods

    /// <inheritdoc/>
    public bool CanDecode(ReadOnlySpan<byte> header)
    {
        if (header.Length < SignatureSize)
        {
            return false;
        }

        return header[..SignatureSize].SequenceEqual(PngSignature);
    }

    /// <inheritdoc/>
    public int EstimateEncodedSize(int width, int height, VideoPixelFormat format)
    {
        var bytesPerPixel = format == VideoPixelFormat.Rgba32 ? 4 : 3;
        var rawSize = ((width * bytesPerPixel) + 1) * height;

        // ZLIB overhead: 2 (header) + 5 bytes per 65535 block + 4 (adler32)
        // DEFLATE может увеличить размер для маленьких данных
        // Берём 120% от raw + минимум 1KB для overhead
        var compressedEstimate = Math.Max(rawSize * 12 / 10, rawSize + 1024);
        var ihdrChunk = ChunkHeaderSize + IhdrDataSize + 4;
        var idatChunk = ChunkHeaderSize + compressedEstimate + 4;
        var iendChunk = ChunkHeaderSize + 4;
        return SignatureSize + ihdrChunk + idatChunk + iendChunk;
    }

    /// <inheritdoc/>
    public void Reset() { }

    /// <summary>
    /// Возвращает информацию о PNG из заголовка без полного декодирования.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult GetInfo(ReadOnlySpan<byte> data, out PngInfo info)
    {
        info = default;

        if (data.Length < SignatureSize + ChunkHeaderSize + IhdrDataSize + 4)
        {
            return CodecResult.InvalidData;
        }

        if (!data[..SignatureSize].SequenceEqual(PngSignature))
        {
            return CodecResult.InvalidData;
        }

        var result = ParseIhdr(data[SignatureSize..], out var header);
        if (result != CodecResult.Success)
        {
            return result;
        }

        info = new PngInfo(
            header.Width,
            header.Height,
            header.BitDepth,
            header.ColorType,
            header.Interlace != 0);

        return CodecResult.Success;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        isInitialized = false;
    }

    #endregion
}

/// <summary>
/// Информация о PNG файле.
/// </summary>
/// <param name="Width">Ширина изображения.</param>
/// <param name="Height">Высота изображения.</param>
/// <param name="BitDepth">Битовая глубина (1, 2, 4, 8, 16).</param>
/// <param name="ColorType">Тип цвета (0=Grayscale, 2=RGB, 3=Indexed, 4=GrayAlpha, 6=RGBA).</param>
/// <param name="Interlaced">Использует ли Adam7 интерлейсинг.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PngInfo(int Width, int Height, byte BitDepth, byte ColorType, bool Interlaced)
{
    /// <summary>Количество байт на пиксель.</summary>
    public int BytesPerPixel => ColorType switch
    {
        PngCodec.ColorTypeGrayscale => BitDepth <= 8 ? 1 : 2,
        PngCodec.ColorTypeRgb => BitDepth <= 8 ? 3 : 6,
        PngCodec.ColorTypeIndexed => 1,
        PngCodec.ColorTypeGrayscaleAlpha => BitDepth <= 8 ? 2 : 4,
        PngCodec.ColorTypeRgba => BitDepth <= 8 ? 4 : 8,
        _ => 0,
    };

    /// <summary>Формат пикселей для VideoFrame.</summary>
    public VideoPixelFormat PixelFormat => ColorType switch
    {
        PngCodec.ColorTypeGrayscale => VideoPixelFormat.Gray8,
        PngCodec.ColorTypeRgb => VideoPixelFormat.Rgb24,
        PngCodec.ColorTypeIndexed => VideoPixelFormat.Rgb24, // Распаковывается в RGB
        PngCodec.ColorTypeGrayscaleAlpha => VideoPixelFormat.Rgba32, // Gray+A → RGBA
        PngCodec.ColorTypeRgba => VideoPixelFormat.Rgba32,
        _ => VideoPixelFormat.Unknown,
    };
}
