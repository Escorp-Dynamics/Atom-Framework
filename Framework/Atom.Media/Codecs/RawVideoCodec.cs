#pragma warning disable CA1822, IDE0032, MA0051, S3776

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Кодек для raw (несжатых) видео форматов.
/// Поддерживает RGB24, BGRA32, YUV420P и другие форматы без компрессии.
/// </summary>
/// <remarks>
/// Этот кодек просто копирует данные между буферами без какого-либо
/// сжатия или преобразования. Полезен для тестирования пайплайна
/// и работы с уже декодированными данными.
/// </remarks>
public sealed class RawVideoCodec : IVideoCodec
{
    private VideoCodecParameters parameters;
    private bool isInitialized;
    private bool isEncoder;
    private bool isDisposed;

    /// <inheritdoc/>
    public MediaCodecId CodecId { get; private set; } = MediaCodecId.Unknown;

    /// <inheritdoc/>
    public string Name => CodecId switch
    {
        MediaCodecId.RawRgb24 => "Raw RGB24",
        MediaCodecId.RawRgba32 => "Raw RGBA32",
        MediaCodecId.RawBgr24 => "Raw BGR24",
        MediaCodecId.RawBgra32 => "Raw BGRA32",
        MediaCodecId.RawYuv420P => "Raw YUV420P",
        MediaCodecId.RawYuv422P => "Raw YUV422P",
        MediaCodecId.RawYuv444P => "Raw YUV444P",
        MediaCodecId.RawNv12 => "Raw NV12",
        MediaCodecId.RawNv21 => "Raw NV21",
        _ => "Raw Video",
    };

    /// <inheritdoc/>
    public CodecCapabilities Capabilities => CodecCapabilities.Decode | CodecCapabilities.Encode;

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public IMeterFactory? MeterFactory { get; set; }

    /// <inheritdoc/>
    public HardwareAcceleration Acceleration { get; init; } = HardwareAcceleration.Auto;

    /// <inheritdoc/>
    public VideoCodecParameters Parameters => parameters;

    #region Initialization

    /// <inheritdoc/>
    public CodecResult InitializeDecoder(in VideoCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.Width <= 0 || parameters.Height <= 0)
            return CodecResult.InvalidData;

        this.parameters = parameters;
        CodecId = MapPixelFormatToCodecId(parameters.PixelFormat);
        isEncoder = false;
        isInitialized = true;

        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public CodecResult InitializeEncoder(in VideoCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.Width <= 0 || parameters.Height <= 0)
            return CodecResult.InvalidData;

        this.parameters = parameters;
        CodecId = MapPixelFormatToCodecId(parameters.PixelFormat);
        isEncoder = true;
        isInitialized = true;

        return CodecResult.Success;
    }

    #endregion

    #region Decode

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult Decode(ReadOnlySpan<byte> packet, ref VideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (isEncoder)
            return CodecResult.UnsupportedFormat;

        // Вычисляем ожидаемый размер данных
        var expectedSize = CalculateFrameSize(parameters.Width, parameters.Height, parameters.PixelFormat);

        if (packet.Length < expectedSize)
            return CodecResult.InvalidData;

        // Для raw — данные и есть кадр
        // Копируем в frame.plane0 (или несколько плоскостей для planar)
        var planeCount = parameters.PixelFormat.GetPlaneCount();

        if (planeCount == 1)
        {
            // Packed формат: все данные в одной плоскости
            var destination = frame.PackedData.Data;
            if (destination.Length < expectedSize)
                return CodecResult.OutputBufferTooSmall;

            packet[..expectedSize].CopyTo(destination);
        }
        else
        {
            // Planar формат (YUV420P, YUV422P и т.д.)
            CopyPlanarData(packet, ref frame, parameters.Width, parameters.Height, parameters.PixelFormat);
        }

        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public async ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> packet,
        [NotNull] VideoFrameBuffer buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (isEncoder)
            return CodecResult.UnsupportedFormat;

        cancellationToken.ThrowIfCancellationRequested();

        var expectedSize = CalculateFrameSize(parameters.Width, parameters.Height, parameters.PixelFormat);

        if (packet.Length < expectedSize)
            return CodecResult.InvalidData;

        // Убеждаемся, что буфер выделен
        if (!buffer.IsAllocated)
            buffer.Allocate(parameters.Width, parameters.Height, parameters.PixelFormat);

        // Копируем данные асинхронно через Task.Run для больших буферов
        if (expectedSize > 1024 * 1024) // > 1MB
        {
            await Task.Run(() =>
            {
                var span = packet.Span;
                var frame = buffer.AsFrame();
                CopyToFrame(span, ref frame, parameters.PixelFormat, expectedSize);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var frame = buffer.AsFrame();
            CopyToFrame(packet.Span, ref frame, parameters.PixelFormat, expectedSize);
        }

        return CodecResult.Success;
    }

    #endregion

    #region Encode

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten)
    {
        bytesWritten = 0;

        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (!isEncoder)
            return CodecResult.UnsupportedFormat;

        var expectedSize = CalculateFrameSize(frame.Width, frame.Height, frame.PixelFormat);

        if (output.Length < expectedSize)
            return CodecResult.OutputBufferTooSmall;

        // Для packed формата — просто копируем
        if (frame.PixelFormat.GetPlaneCount() == 1)
        {
            frame.PackedData.Data.CopyTo(output);
            bytesWritten = expectedSize;
        }
        else
        {
            // Planar: копируем плоскости последовательно
            bytesWritten = CopyPlanarToOutput(frame, output);
        }

        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public async ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        [NotNull] VideoFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return (CodecResult.NotInitialized, 0);

        if (!isEncoder)
            return (CodecResult.UnsupportedFormat, 0);

        cancellationToken.ThrowIfCancellationRequested();

        var expectedSize = CalculateFrameSize(frame.Width, frame.Height, frame.PixelFormat);

        if (output.Length < expectedSize)
            return (CodecResult.OutputBufferTooSmall, 0);

        var isPacked = frame.PixelFormat.GetPlaneCount() == 1;

        // Для больших буферов — асинхронная копия
        if (expectedSize > 1024 * 1024)
        {
            var bytesWritten = await Task.Run(() =>
            {
                var roFrame = frame.AsReadOnlyFrame();

                if (isPacked)
                {
                    roFrame.PackedData.Data.CopyTo(output.Span);
                    return expectedSize;
                }

                return CopyPlanarToOutput(roFrame, output.Span);
            }, cancellationToken).ConfigureAwait(false);

            return (CodecResult.Success, bytesWritten);
        }
        else
        {
            var roFrame = frame.AsReadOnlyFrame();

            if (isPacked)
            {
                roFrame.PackedData.Data.CopyTo(output.Span);
                return (CodecResult.Success, expectedSize);
            }

            var bytesWritten = CopyPlanarToOutput(roFrame, output.Span);
            return (CodecResult.Success, bytesWritten);
        }
    }

    #endregion

    #region Flush & Reset

    /// <inheritdoc/>
    public CodecResult Flush(ref VideoFrame frame) =>
        // Raw кодек не буферизует данные — flush всегда возвращает EndOfStream
        CodecResult.EndOfStream;

    /// <inheritdoc/>
    public void Reset()
    {
        // Raw кодек без состояния — ничего не делаем
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Вычисляет размер кадра в байтах.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateFrameSize(int width, int height, VideoPixelFormat format)
    {
        var planeCount = format.GetPlaneCount();

        if (planeCount == 1)
        {
            // Packed формат
            return width * height * format.GetBytesPerPixel();
        }

        // Planar форматы
        return format switch
        {
            // YUV 4:2:0 — Y полный, U и V по 1/4
            VideoPixelFormat.Yuv420P => (width * height) + (width * height / 2),
            VideoPixelFormat.Nv12 or VideoPixelFormat.Nv21 => (width * height) + (width * height / 2),

            // YUV 4:2:2 — Y полный, U и V по 1/2
            VideoPixelFormat.Yuv422P => width * height * 2,

            // YUV 4:4:4 — все плоскости полного размера
            VideoPixelFormat.Yuv444P => width * height * 3,

            // 10-bit варианты (2 байта на семпл)
            VideoPixelFormat.Yuv420P10Le => ((width * height) + (width * height / 2)) * 2,
            VideoPixelFormat.Yuv422P10Le => width * height * 4,
            VideoPixelFormat.Yuv444P10Le => width * height * 6,
            VideoPixelFormat.P010Le => ((width * height) + (width * height / 2)) * 2,

            _ => width * height * 3, // Fallback
        };
    }

    /// <summary>
    /// Маппинг формата пикселей на ID кодека.
    /// </summary>
    private static MediaCodecId MapPixelFormatToCodecId(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Rgb24 => MediaCodecId.RawRgb24,
        VideoPixelFormat.Rgba32 => MediaCodecId.RawRgba32,
        VideoPixelFormat.Bgr24 => MediaCodecId.RawBgr24,
        VideoPixelFormat.Bgra32 => MediaCodecId.RawBgra32,
        VideoPixelFormat.Yuv420P => MediaCodecId.RawYuv420P,
        VideoPixelFormat.Yuv422P => MediaCodecId.RawYuv422P,
        VideoPixelFormat.Yuv444P => MediaCodecId.RawYuv444P,
        VideoPixelFormat.Nv12 => MediaCodecId.RawNv12,
        VideoPixelFormat.Nv21 => MediaCodecId.RawNv21,
        _ => MediaCodecId.Unknown,
    };

    /// <summary>
    /// Копирует данные в frame с учётом формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyToFrame(ReadOnlySpan<byte> source, ref VideoFrame frame, VideoPixelFormat format, int size)
    {
        if (format.GetPlaneCount() == 1)
        {
            source[..size].CopyTo(frame.PackedData.Data);
        }
        else
        {
            CopyPlanarData(source, ref frame, frame.Width, frame.Height, format);
        }
    }

    /// <summary>
    /// Копирует planar данные из пакета в frame.
    /// </summary>
    private static void CopyPlanarData(ReadOnlySpan<byte> source, ref VideoFrame frame, int width, int height, VideoPixelFormat format)
    {
        var offset = 0;

        // Y плоскость — копируем построчно с учётом stride
        var planeY = frame.GetPlaneY();
        offset = CopyToPlaneRowByRow(source, offset, planeY, width, height);

        // UV плоскости — зависят от формата
        switch (format)
        {
            case VideoPixelFormat.Yuv420P:
            case VideoPixelFormat.Yuv420P10Le:
                {
                    var chromaW = width / 2;
                    var chromaH = height / 2;
                    var planeU = frame.GetPlaneU();
                    var planeV = frame.GetPlaneV();
                    offset = CopyToPlaneRowByRow(source, offset, planeU, chromaW, chromaH);
                    CopyToPlaneRowByRow(source, offset, planeV, chromaW, chromaH);
                    break;
                }

            case VideoPixelFormat.Nv12:
            case VideoPixelFormat.Nv21:
                {
                    // NV12/NV21: interleaved UV, ширина = width, высота = height/2
                    var planeUV = frame.GetPlaneUV();
                    CopyToPlaneRowByRow(source, offset, planeUV, width, height / 2);
                    break;
                }

            case VideoPixelFormat.Yuv422P:
            case VideoPixelFormat.Yuv422P10Le:
                {
                    var chromaW = width / 2;
                    var chromaH = height;
                    var planeU = frame.GetPlaneU();
                    var planeV = frame.GetPlaneV();
                    offset = CopyToPlaneRowByRow(source, offset, planeU, chromaW, chromaH);
                    CopyToPlaneRowByRow(source, offset, planeV, chromaW, chromaH);
                    break;
                }

            case VideoPixelFormat.Yuv444P:
            case VideoPixelFormat.Yuv444P10Le:
                {
                    var planeU = frame.GetPlaneU();
                    var planeV = frame.GetPlaneV();
                    offset = CopyToPlaneRowByRow(source, offset, planeU, width, height);
                    CopyToPlaneRowByRow(source, offset, planeV, width, height);
                    break;
                }
        }
    }

    /// <summary>
    /// Копирует packed данные из source в plane построчно с учётом stride.
    /// </summary>
    /// <returns>Новый offset в source после копирования.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CopyToPlaneRowByRow(ReadOnlySpan<byte> source, int offset, Plane<byte> plane, int rowWidth, int rowCount)
    {
        // Если stride == width, можно копировать одним блоком
        if (plane.Stride == rowWidth)
        {
            var totalSize = rowWidth * rowCount;
            source.Slice(offset, totalSize).CopyTo(plane.Data);
            return offset + totalSize;
        }

        // Иначе — построчно
        for (var y = 0; y < rowCount; y++)
        {
            var srcRow = source.Slice(offset, rowWidth);
            var dstRow = plane.GetRow(y);
            srcRow.CopyTo(dstRow);
            offset += rowWidth;
        }

        return offset;
    }

    /// <summary>
    /// Копирует planar frame в линейный output буфер.
    /// </summary>
    private static int CopyPlanarToOutput(in ReadOnlyVideoFrame frame, Span<byte> output)
    {
        var offset = 0;
        var width = frame.Width;
        var height = frame.Height;

        // Y плоскость — всегда full size
        var ySize = width * height;
        var yPlane = frame.GetPlaneY();

        // Копируем построчно если stride != width
        if (yPlane.Stride == width)
        {
            yPlane.Data[..ySize].CopyTo(output[offset..]);
        }
        else
        {
            for (var y = 0; y < height; y++)
                yPlane.GetRow(y).CopyTo(output.Slice(offset + (y * width), width));
        }
        offset += ySize;

        // Для packed форматов — только одна плоскость
        if (frame.PixelFormat.GetPlaneCount() == 1)
            return offset;

        // UV плоскости — зависят от формата
        switch (frame.PixelFormat)
        {
            case VideoPixelFormat.Nv12:
            case VideoPixelFormat.Nv21:
                {
                    // Interleaved UV — размер width * (height / 2)
                    var uvSize = width * (height / 2);
                    var uvPlane = frame.GetPlaneUV();

                    if (uvPlane.Stride == width)
                    {
                        uvPlane.Data[..uvSize].CopyTo(output[offset..]);
                    }
                    else
                    {
                        for (var y = 0; y < height / 2; y++)
                            uvPlane.GetRow(y).CopyTo(output.Slice(offset + (y * width), width));
                    }
                    offset += uvSize;
                    break;
                }

            case VideoPixelFormat.Yuv420P:
                {
                    // U и V — каждый (width/2) * (height/2)
                    var chromaWidth = width / 2;
                    var chromaHeight = height / 2;
                    var chromaSize = chromaWidth * chromaHeight;

                    var uPlane = frame.GetPlaneU();
                    if (uPlane.Stride == chromaWidth)
                    {
                        uPlane.Data[..chromaSize].CopyTo(output[offset..]);
                    }
                    else
                    {
                        for (var y = 0; y < chromaHeight; y++)
                            uPlane.GetRow(y).CopyTo(output.Slice(offset + (y * chromaWidth), chromaWidth));
                    }
                    offset += chromaSize;

                    var vPlane = frame.GetPlaneV();
                    if (vPlane.Stride == chromaWidth)
                    {
                        vPlane.Data[..chromaSize].CopyTo(output[offset..]);
                    }
                    else
                    {
                        for (var y = 0; y < chromaHeight; y++)
                            vPlane.GetRow(y).CopyTo(output.Slice(offset + (y * chromaWidth), chromaWidth));
                    }
                    offset += chromaSize;
                    break;
                }

            case VideoPixelFormat.Yuv422P:
                {
                    // U и V — каждый (width/2) * height
                    var chromaWidth = width / 2;
                    var chromaSize = chromaWidth * height;

                    var uPlane = frame.GetPlaneU();
                    if (uPlane.Stride == chromaWidth)
                    {
                        uPlane.Data[..chromaSize].CopyTo(output[offset..]);
                    }
                    else
                    {
                        for (var y = 0; y < height; y++)
                            uPlane.GetRow(y).CopyTo(output.Slice(offset + (y * chromaWidth), chromaWidth));
                    }
                    offset += chromaSize;

                    var vPlane = frame.GetPlaneV();
                    if (vPlane.Stride == chromaWidth)
                    {
                        vPlane.Data[..chromaSize].CopyTo(output[offset..]);
                    }
                    else
                    {
                        for (var y = 0; y < height; y++)
                            vPlane.GetRow(y).CopyTo(output.Slice(offset + (y * chromaWidth), chromaWidth));
                    }
                    offset += chromaSize;
                    break;
                }

            case VideoPixelFormat.Yuv444P:
                {
                    // U и V — каждый width * height
                    var chromaSize = width * height;

                    var uPlane = frame.GetPlaneU();
                    if (uPlane.Stride == width)
                    {
                        uPlane.Data[..chromaSize].CopyTo(output[offset..]);
                    }
                    else
                    {
                        for (var y = 0; y < height; y++)
                            uPlane.GetRow(y).CopyTo(output.Slice(offset + (y * width), width));
                    }
                    offset += chromaSize;

                    var vPlane = frame.GetPlaneV();
                    if (vPlane.Stride == width)
                    {
                        vPlane.Data[..chromaSize].CopyTo(output[offset..]);
                    }
                    else
                    {
                        for (var y = 0; y < height; y++)
                            vPlane.GetRow(y).CopyTo(output.Slice(offset + (y * width), width));
                    }
                    offset += chromaSize;
                    break;
                }

            default:
                {
                    // Fallback для других форматов
                    var uPlane = frame.GetPlaneU();
                    var vPlane = frame.GetPlaneV();
                    uPlane.Data.CopyTo(output[offset..]);
                    offset += uPlane.Data.Length;
                    vPlane.Data.CopyTo(output[offset..]);
                    offset += vPlane.Data.Length;
                    break;
                }
        }

        return offset;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        isInitialized = false;
    }

    #endregion
}
