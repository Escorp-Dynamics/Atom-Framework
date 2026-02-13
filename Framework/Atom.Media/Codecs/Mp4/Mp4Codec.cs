#pragma warning disable CA1822, IDE0010, IDE0032, MA0041, MA0051, S109, S1144, S2325, S3776

using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Высокооптимизированный видеокодек MP4 (H.264/AVC).
/// </summary>
/// <remarks>
/// <para>
/// Поддерживает Store mode для zero-overhead round-trip с использованием
/// собственного AFRM (Atom Frame) chunk внутри ISO Base Media контейнера.
/// </para>
/// <para>
/// Форматы пикселей: YUV420P, YUV422P, YUV444P, RGB24, RGBA32, BGR24, BGRA32.
/// </para>
/// <para>
/// Оптимизации:
/// <list type="bullet">
/// <item>AVX2: 256 байт за итерацию (8x unroll)</item>
/// <item>SSE2: 64 байт за итерацию (4x unroll)</item>
/// <item>Zero-copy для непрерывных буферов</item>
/// <item>Построчное копирование при наличии stride padding</item>
/// <item>Без bounds checks в hot path</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class Mp4Codec : IVideoCodec
{
    #region Constants

    /// <summary>Минимальный размер заголовка ftyp box.</summary>
    private const int MinHeaderSize = 8;

    /// <summary>Максимальный размер кадра (16K).</summary>
    private const int MaxFrameSize = 16384;

    /// <summary>AFRM magic для Store mode.</summary>
    internal const uint AfrmMagic = 0x4D524641; // "AFRM" little-endian

    /// <summary>ftyp box type.</summary>
    private static ReadOnlySpan<byte> FtypBoxType => "ftyp"u8;

    #endregion

    #region Fields

    private VideoCodecParameters parameters;
    private bool isInitialized;
    private bool isEncoder;
    private bool isDisposed;
    private long frameIndex;

    #endregion

    #region Properties

    /// <inheritdoc/>
    public MediaCodecId CodecId { get; private set; } = MediaCodecId.H264;

    /// <inheritdoc/>
    public string Name => "MP4 H.264/AVC (Atom Store Mode)";

    /// <inheritdoc/>
    public string MimeType => "video/mp4";

    /// <inheritdoc/>
    public CodecCapabilities Capabilities =>
        CodecCapabilities.Decode | CodecCapabilities.Encode;

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public IMeterFactory? MeterFactory { get; set; }

    /// <inheritdoc/>
    public HardwareAcceleration Acceleration { get; init; } = HardwareAcceleration.Auto;

    /// <inheritdoc/>
    public VideoCodecParameters Parameters => parameters;

    #endregion

    #region Initialization

    /// <inheritdoc/>
    public CodecResult InitializeDecoder(in VideoCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.Width <= 0 || parameters.Height <= 0)
            return CodecResult.InvalidData;

        if (parameters.Width > MaxFrameSize || parameters.Height > MaxFrameSize)
            return CodecResult.InvalidData;

        if (!IsSupportedPixelFormat(parameters.PixelFormat))
            return CodecResult.UnsupportedFormat;

        this.parameters = parameters;
        isEncoder = false;
        isInitialized = true;
        frameIndex = 0;

        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public CodecResult InitializeEncoder(in VideoCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.Width <= 0 || parameters.Height <= 0)
            return CodecResult.InvalidData;

        if (parameters.Width > MaxFrameSize || parameters.Height > MaxFrameSize)
            return CodecResult.InvalidData;

        if (!IsSupportedPixelFormat(parameters.PixelFormat))
            return CodecResult.UnsupportedFormat;

        this.parameters = parameters;
        isEncoder = true;
        isInitialized = true;
        frameIndex = 0;

        return CodecResult.Success;
    }

    /// <summary>
    /// Проверяет поддержку формата пикселей.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSupportedPixelFormat(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Yuv420P => true,
        VideoPixelFormat.Yuv422P => true,
        VideoPixelFormat.Yuv444P => true,
        VideoPixelFormat.Rgb24 => true,
        VideoPixelFormat.Rgba32 => true,
        VideoPixelFormat.Bgr24 => true,
        VideoPixelFormat.Bgra32 => true,
        _ => false,
    };

    #endregion

    #region Frame Size Calculation

    /// <summary>
    /// Вычисляет размер закодированного кадра.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EstimateEncodedSize(int width, int height, VideoPixelFormat format)
    {
        var frameSize = CalculateFrameSize(width, height, format);

        // AFRM header: magic(4) + width(4) + height(4) + format(4) + frameIndex(8) + flags(4) + planeSizes(12) = 40 bytes
        // + MP4 box overhead (~128 bytes max)
        return frameSize + 256;
    }

    /// <summary>
    /// Вычисляет размер сырых данных кадра.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateFrameSize(int width, int height, VideoPixelFormat format)
    {
        return format switch
        {
            // Planar YUV
            VideoPixelFormat.Yuv420P => (width * height) + (width / 2 * (height / 2) * 2),
            VideoPixelFormat.Yuv422P => (width * height) + (width / 2 * height * 2),
            VideoPixelFormat.Yuv444P => width * height * 3,

            // Packed RGB
            VideoPixelFormat.Rgb24 or VideoPixelFormat.Bgr24 => width * height * 3,
            VideoPixelFormat.Rgba32 or VideoPixelFormat.Bgra32 => width * height * 4,

            _ => width * height * 4, // Default to RGBA
        };
    }

    #endregion

    #region Header Detection

    /// <summary>
    /// Проверяет, является ли данные MP4 потоком (ftyp box).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanDecodeStream(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinHeaderSize)
            return false;

        // ftyp box: size(4) + "ftyp"(4)
        return data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p';
    }

    /// <summary>
    /// Проверяет наличие AFRM (Atom Frame) chunk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasAfrmChunk(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32)
            return false;

        // Ищем AFRM magic
        ref var dataRef = ref MemoryMarshal.GetReference(data);
        for (var i = 0; i <= data.Length - 4; i++)
        {
            if (Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, i)) == AfrmMagic)
                return true;
        }

        return false;
    }

    #endregion

    #region Flush & Reset

    /// <inheritdoc/>
    public CodecResult Flush(ref VideoFrame frame) =>
        // Store mode кодек не буферизует данные
        CodecResult.EndOfStream;

    /// <inheritdoc/>
    public void Reset() => frameIndex = 0;

    #endregion

    #region Dispose

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        isInitialized = false;
    }

    #endregion
}
