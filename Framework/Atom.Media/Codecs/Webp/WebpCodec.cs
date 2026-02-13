#pragma warning disable IDE0010, S109, S3776, CA1822, MA0038, MA0051, MA0003

using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Высокопроизводительный кодек для формата WebP.
/// </summary>
/// <remarks>
/// <para>
/// Поддерживает:
/// <list type="bullet">
///   <item>WebP Lossless (VP8L)</item>
///   <item>RGB24 и RGBA32 форматы</item>
///   <item>Store mode для максимальной скорости</item>
/// </list>
/// </para>
/// <para>
/// Оптимизации:
/// <list type="bullet">
///   <item>SIMD копирование данных (AVX2/SSE2)</item>
///   <item>Zero-allocation декодирование в предоставленный буфер</item>
///   <item>CRC32 sliced-by-8 для контрольных сумм</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class WebpCodec : IImageCodec
{
    #region Constants

    /// <summary>RIFF signature.</summary>
    internal static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;

    /// <summary>WEBP signature.</summary>
    internal static ReadOnlySpan<byte> WebpSignature => "WEBP"u8;

    /// <summary>VP8L chunk type (lossless).</summary>
    internal static ReadOnlySpan<byte> Vp8LChunk => "VP8L"u8;

    /// <summary>VP8 chunk type (lossy).</summary>
    internal static ReadOnlySpan<byte> Vp8Chunk => "VP8 "u8;

    /// <summary>VP8X chunk type (extended).</summary>
    internal static ReadOnlySpan<byte> Vp8XChunk => "VP8X"u8;

    /// <summary>ALPH chunk type (alpha channel).</summary>
    internal static ReadOnlySpan<byte> AlphChunk => "ALPH"u8;

    /// <summary>Минимальный размер заголовка RIFF + WEBP.</summary>
    internal const int MinHeaderSize = 12;

    /// <summary>VP8L signature byte (0x2F = '/').</summary>
    internal const byte Vp8LSignature = 0x2F;

    /// <summary>Размер заголовка VP8L (signature + width/height + flags).</summary>
    internal const int Vp8LHeaderSize = 5;

    #endregion

    #region Fields

    private bool isEncoder;
    private bool isInitialized;
    private bool isDisposed;

    #endregion

    #region ICodec Properties

    /// <inheritdoc/>
    public MediaCodecId CodecId => MediaCodecId.WebP;

    /// <inheritdoc/>
    public string Name => "WebP (Google WebP)";

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
    public IReadOnlyList<string> SupportedExtensions { get; } = [".webp"];

    /// <inheritdoc/>
    public string MimeType => "image/webp";

    #endregion

    #region Initialization

    /// <inheritdoc/>
    public CodecResult InitializeDecoder(ImageCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        Parameters = parameters;
        isEncoder = false;
        isInitialized = true;

        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public CodecResult InitializeEncoder(ImageCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.Width <= 0 || parameters.Height <= 0)
        {
            return CodecResult.InvalidData;
        }

        // Поддерживаем только RGB24 и RGBA32 packed форматы для encoding
        if (parameters.PixelFormat is not (VideoPixelFormat.Rgb24 or VideoPixelFormat.Rgba32))
        {
            return CodecResult.UnsupportedFormat;
        }

        // WebP имеет ограничение 16383x16383
        if (parameters.Width > 16383 || parameters.Height > 16383)
        {
            return CodecResult.InvalidData;
        }

        Parameters = parameters;
        isEncoder = true;
        isInitialized = true;

        return CodecResult.Success;
    }

    #endregion

    #region Utility Methods

    /// <inheritdoc/>
    public bool CanDecode(ReadOnlySpan<byte> header)
    {
        if (header.Length < MinHeaderSize)
        {
            return false;
        }

        // RIFF + size + WEBP
        return header[..4].SequenceEqual(RiffSignature) &&
               header[8..12].SequenceEqual(WebpSignature);
    }

    /// <inheritdoc/>
    public int EstimateEncodedSize(int width, int height, VideoPixelFormat format)
    {
        var bytesPerPixel = format == VideoPixelFormat.Rgba32 ? 4 : 3;
        var rawSize = width * height * bytesPerPixel;

        // RIFF header (12) + ARAW chunk header (8) + ARAW data header (12) + raw data + padding
        return 12 + 8 + 12 + rawSize + 16;
    }

    /// <inheritdoc/>
    public void Reset() { }

    /// <summary>
    /// Возвращает информацию о WebP из заголовка без полного декодирования.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult GetInfo(ReadOnlySpan<byte> data, out WebpInfo info)
    {
        info = default;

        if (data.Length < MinHeaderSize + 8 + Vp8LHeaderSize)
        {
            return CodecResult.InvalidData;
        }

        if (!CanDecode(data))
        {
            return CodecResult.InvalidData;
        }

        // Пропускаем RIFF header и ищем VP8L/VP8X/VP8 chunk
        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            var chunkType = data.Slice(offset, 4);
            var chunkSize = BitConverter.ToInt32(data.Slice(offset + 4, 4));

            if (chunkType.SequenceEqual(Vp8LChunk))
            {
                // VP8L header: signature(1) + packed width/height/alpha(4)
                if (offset + 8 + Vp8LHeaderSize > data.Length)
                {
                    return CodecResult.InvalidData;
                }

                var vp8lData = data.Slice(offset + 8, Vp8LHeaderSize);
                if (vp8lData[0] != Vp8LSignature)
                {
                    return CodecResult.InvalidData;
                }

                // Packed: width-1 (14 bits) + height-1 (14 bits) + alpha (1 bit) + version (3 bits)
                var packed = BitConverter.ToUInt32(vp8lData.Slice(1, 4));
                var width = (int)(packed & 0x3FFF) + 1;
                var height = (int)((packed >> 14) & 0x3FFF) + 1;
                var hasAlpha = ((packed >> 28) & 1) == 1;

                info = new WebpInfo(width, height, hasAlpha, true);
                return CodecResult.Success;
            }

            if (chunkType.SequenceEqual(Vp8Chunk))
            {
                // VP8 (lossy) — parse frame header
                if (offset + 8 + 10 > data.Length)
                {
                    return CodecResult.InvalidData;
                }

                var vp8Data = data.Slice(offset + 8, 10);
                // VP8 frame: 3 bytes frame tag + 3 bytes start code + 2 bytes width + 2 bytes height
                var width = (vp8Data[6] | (vp8Data[7] << 8)) & 0x3FFF;
                var height = (vp8Data[8] | (vp8Data[9] << 8)) & 0x3FFF;

                info = new WebpInfo(width, height, false, false);
                return CodecResult.Success;
            }

            if (chunkType.SequenceEqual(Vp8XChunk))
            {
                // VP8X extended header
                if (offset + 8 + 10 > data.Length)
                {
                    return CodecResult.InvalidData;
                }

                var vp8xData = data.Slice(offset + 8, 10);
                var flags = vp8xData[0];
                var hasAlpha = (flags & 0x10) != 0;

                // Width and height are 24-bit, stored as (value - 1)
                var width = (vp8xData[4] | (vp8xData[5] << 8) | (vp8xData[6] << 16)) + 1;
                var height = (vp8xData[7] | (vp8xData[8] << 8) | (vp8xData[9] << 16)) + 1;

                info = new WebpInfo(width, height, hasAlpha, false);
                // Продолжаем искать VP8L/VP8 chunk для определения lossless
                offset += 8 + ((chunkSize + 1) & ~1);
                continue;
            }

            // Переходим к следующему chunk (с выравниванием на 2 байта)
            offset += 8 + ((chunkSize + 1) & ~1);
        }

        return info.Width > 0 ? CodecResult.Success : CodecResult.InvalidData;
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
/// Информация о WebP файле.
/// </summary>
/// <param name="Width">Ширина изображения.</param>
/// <param name="Height">Высота изображения.</param>
/// <param name="HasAlpha">Содержит ли альфа-канал.</param>
/// <param name="IsLossless">Использует ли lossless сжатие (VP8L).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct WebpInfo(int Width, int Height, bool HasAlpha, bool IsLossless)
{
    /// <summary>Формат пикселей для VideoFrame.</summary>
    public VideoPixelFormat PixelFormat => HasAlpha ? VideoPixelFormat.Rgba32 : VideoPixelFormat.Rgb24;
}
