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
/// Два режима работы:
/// <list type="bullet">
/// <item>Store mode: zero-overhead round-trip с AFRM (Atom Frame) chunk.</item>
/// <item>H.264 decode: Annex B и AVCC (MP4/MKV контейнер) → RGBA32.</item>
/// </list>
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

    // H.264 decode state
    private H264Decoder? h264Decoder;
    private H264Sps? h264Sps;
    private H264Pps? h264Pps;
    private int nalLengthSize;

    #endregion

    #region Properties

    /// <inheritdoc/>
    public MediaCodecId CodecId { get; private set; } = MediaCodecId.H264;

    /// <inheritdoc/>
    public string Name => "MP4 H.264/AVC";

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

        // Parse AVCC configuration from ExtraData (SPS/PPS for MP4 containers)
        if (!parameters.ExtraData.IsEmpty)
        {
            ParseAvccConfig(parameters.ExtraData.Span);
        }

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
        CodecResult.EndOfStream;

    /// <inheritdoc/>
    public void Reset()
    {
        frameIndex = 0;
        h264Decoder = null;
        h264Sps = null;
        h264Pps = null;
        nalLengthSize = 0;
    }

    #endregion

    #region Dispose

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        isInitialized = false;
        h264Decoder = null;
        h264Sps = null;
        h264Pps = null;
    }

    #endregion

    #region AVCC Config Parsing

    /// <summary>
    /// Parses AVCDecoderConfigurationRecord from ExtraData to extract SPS/PPS.
    /// </summary>
    private void ParseAvccConfig(ReadOnlySpan<byte> extraData)
    {
        // AVCDecoderConfigurationRecord (ISO 14496-15)
        // byte[0]: configurationVersion = 1
        // byte[1]: AVCProfileIndication
        // byte[2]: profile_compatibility
        // byte[3]: AVCLevelIndication
        // byte[4]: xxxxxx ll → nalLengthSize = (ll & 0x03) + 1
        // byte[5]: xxx nnnnn → numSPS = nnnnn & 0x1F
        if (extraData.Length < 7)
            return;

        nalLengthSize = (extraData[4] & 0x03) + 1;

        var offset = 5;
        var numSps = extraData[offset] & 0x1F;
        offset++;

        for (var i = 0; i < numSps && offset + 2 <= extraData.Length; i++)
        {
            var spsLen = (extraData[offset] << 8) | extraData[offset + 1];
            offset += 2;

            if (offset + spsLen > extraData.Length || spsLen < 2)
                break;

            // Skip NAL header byte (1 byte)
            var rbsp = new byte[spsLen];
            var rbspLen = H264Nal.RemoveEmulationPrevention(extraData.Slice(offset + 1, spsLen - 1), rbsp);
            h264Sps = H264Sps.Parse(rbsp.AsSpan(0, rbspLen));
            offset += spsLen;
        }

        if (offset >= extraData.Length)
            return;

        var numPps = extraData[offset];
        offset++;

        for (var i = 0; i < numPps && offset + 2 <= extraData.Length; i++)
        {
            var ppsLen = (extraData[offset] << 8) | extraData[offset + 1];
            offset += 2;

            if (offset + ppsLen > extraData.Length || ppsLen < 2)
                break;

            // Skip NAL header byte (1 byte)
            var rbsp = new byte[ppsLen];
            var rbspLen = H264Nal.RemoveEmulationPrevention(extraData.Slice(offset + 1, ppsLen - 1), rbsp);
            h264Pps = H264Pps.Parse(rbsp.AsSpan(0, rbspLen));
            offset += ppsLen;
        }

        h264Decoder = new H264Decoder();
    }

    #endregion
}
