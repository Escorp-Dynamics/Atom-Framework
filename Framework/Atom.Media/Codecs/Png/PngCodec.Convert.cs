#pragma warning disable IDE0010, MA0051, S109, S3776

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.Media.ColorSpaces;

namespace Atom.Media;

/// <summary>
/// Конвертация форматов для PNG кодека с использованием оптимизированных ColorSpaces.
/// </summary>
public sealed partial class PngCodec
{
    #region Format Conversion

    /// <summary>
    /// Декодирует PNG с автоматической конвертацией в целевой формат.
    /// </summary>
    /// <param name="data">Закодированные PNG данные.</param>
    /// <param name="frame">Целевой кадр (может иметь отличный от PNG формат).</param>
    /// <returns>Результат декодирования.</returns>
    /// <remarks>
    /// Поддерживаемые конвертации:
    /// <list type="bullet">
    ///   <item>PNG RGBA → BGRA32 (swap R↔B с использованием SIMD)</item>
    ///   <item>PNG RGB → BGR24 (swap R↔B с использованием SIMD)</item>
    ///   <item>PNG RGBA → RGB24 (удаление альфа-канала)</item>
    ///   <item>PNG RGB → RGBA32 (добавление альфа=255)</item>
    ///   <item>PNG Gray → RGB24/RGBA32 (расширение каналов)</item>
    /// </list>
    /// </remarks>
    public CodecResult DecodeWithConversion(ReadOnlySpan<byte> data, ref VideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        // Парсим заголовок для определения исходного формата
        var result = ValidatePngHeader(data, out var header, out var compressedData);
        if (result != CodecResult.Success)
        {
            return result;
        }

        var sourceFormat = GetPixelFormat(header.ColorType, header.BitDepth);
        var targetFormat = frame.PixelFormat;

        // Если форматы совпадают — прямое декодирование
        if (sourceFormat == targetFormat)
        {
            return DecompressAndDefilter(compressedData, frame.PackedData, header);
        }

        // Декодируем во временный буфер и конвертируем
        return DecodeAndConvert(compressedData, frame, header, sourceFormat, targetFormat);
    }

    /// <summary>
    /// Кодирует кадр в PNG с автоматической конвертацией из исходного формата.
    /// </summary>
    /// <param name="frame">Входной кадр (может быть BGRA32, BGR24 и т.д.).</param>
    /// <param name="output">Буфер для PNG данных.</param>
    /// <param name="bytesWritten">Количество записанных байт.</param>
    /// <returns>Результат кодирования.</returns>
    public CodecResult EncodeWithConversion(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var sourceFormat = frame.PixelFormat;

        // PNG поддерживает RGB24 и RGBA32 напрямую
        if (sourceFormat is VideoPixelFormat.Rgb24 or VideoPixelFormat.Rgba32)
        {
            return Encode(frame, output, out bytesWritten);
        }

        // Конвертируем в подходящий формат
        return ConvertAndEncode(frame, output, sourceFormat, out bytesWritten);
    }

    #endregion

    #region Internal Conversion

    /// <summary>
    /// Декодирует PNG и конвертирует в целевой формат.
    /// </summary>
    private CodecResult DecodeAndConvert(
        ReadOnlySpan<byte> compressedData,
        VideoFrame targetFrame,
        PngIhdr header,
        VideoPixelFormat sourceFormat,
        VideoPixelFormat targetFormat)
    {
        var width = header.Width;
        var height = header.Height;
        var sourceBpp = GetBytesPerPixel(sourceFormat);
        var sourceRowBytes = width * sourceBpp;
        var sourceSize = sourceRowBytes * height;

        // Аллоцируем временный буфер для декодированных данных
        var tempBuffer = ArrayPool<byte>.Shared.Rent(sourceSize);
        try
        {
            // Создаём временную Plane для декодирования
            var tempPlane = new Plane<byte>(tempBuffer.AsSpan(0, sourceSize), sourceRowBytes, width, height);

            var result = DecompressAndDefilter(compressedData, tempPlane, header);
            if (result != CodecResult.Success)
            {
                return result;
            }

            // Конвертируем в целевой формат
            ConvertPixels(
                tempBuffer.AsSpan(0, sourceSize),
                targetFrame.PackedData.Data,
                width * height,
                sourceFormat,
                targetFormat);

            return CodecResult.Success;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    /// <summary>
    /// Конвертирует кадр в RGB/RGBA и кодирует в PNG.
    /// </summary>
    private CodecResult ConvertAndEncode(
        in ReadOnlyVideoFrame sourceFrame,
        Span<byte> output,
        VideoPixelFormat sourceFormat,
        out int bytesWritten)
    {
        bytesWritten = 0;

        var width = sourceFrame.Width;
        var height = sourceFrame.Height;
        var pixelCount = width * height;

        // Определяем целевой формат для PNG
        var targetFormat = HasAlpha(sourceFormat) ? VideoPixelFormat.Rgba32 : VideoPixelFormat.Rgb24;
        var targetBpp = targetFormat == VideoPixelFormat.Rgba32 ? 4 : 3;
        var targetSize = width * height * targetBpp;

        var tempBuffer = ArrayPool<byte>.Shared.Rent(targetSize);
        try
        {
            // Конвертируем
            ConvertPixels(
                sourceFrame.PackedData.Data,
                tempBuffer.AsSpan(0, targetSize),
                pixelCount,
                sourceFormat,
                targetFormat);

            // Создаём временный кадр и кодируем
            var targetRowBytes = width * targetBpp;
            var tempPlane = new ReadOnlyPlane<byte>(tempBuffer.AsSpan(0, targetSize), targetRowBytes, width, height);

            var colorType = targetBpp == 4 ? ColorTypeRgba : ColorTypeRgb;
            return EncodeInternal(tempPlane, output, width, height, targetBpp, colorType, Parameters.CompressionLevel, Parameters.FastFiltering, out bytesWritten);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    /// <summary>
    /// Конвертирует пиксели между форматами с использованием оптимизированных ColorSpaces.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ConvertPixels(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int pixelCount,
        VideoPixelFormat sourceFormat,
        VideoPixelFormat targetFormat)
    {
        // BGRA32 → RGBA32 (swap R↔B)
        if (sourceFormat == VideoPixelFormat.Bgra32 && targetFormat == VideoPixelFormat.Rgba32)
        {
            var src = MemoryMarshal.Cast<byte, Bgra32>(source);
            var dst = MemoryMarshal.Cast<byte, Rgba32>(destination);
            Rgba32.FromBgra32(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // RGBA32 → BGRA32 (swap R↔B)
        if (sourceFormat == VideoPixelFormat.Rgba32 && targetFormat == VideoPixelFormat.Bgra32)
        {
            var src = MemoryMarshal.Cast<byte, Rgba32>(source);
            var dst = MemoryMarshal.Cast<byte, Bgra32>(destination);
            Bgra32.FromRgba32(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // BGR24 → RGB24 (swap R↔B)
        if (sourceFormat == VideoPixelFormat.Bgr24 && targetFormat == VideoPixelFormat.Rgb24)
        {
            var src = MemoryMarshal.Cast<byte, Bgr24>(source);
            var dst = MemoryMarshal.Cast<byte, Rgb24>(destination);
            Rgb24.FromBgr24(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // RGB24 → BGR24 (swap R↔B)
        if (sourceFormat == VideoPixelFormat.Rgb24 && targetFormat == VideoPixelFormat.Bgr24)
        {
            var src = MemoryMarshal.Cast<byte, Rgb24>(source);
            var dst = MemoryMarshal.Cast<byte, Bgr24>(destination);
            Bgr24.FromRgb24(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // Gray8 → RGB24
        if (sourceFormat == VideoPixelFormat.Gray8 && targetFormat == VideoPixelFormat.Rgb24)
        {
            var src = MemoryMarshal.Cast<byte, Gray8>(source);
            var dst = MemoryMarshal.Cast<byte, Rgb24>(destination);
            Rgb24.FromGray8(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // Gray8 → RGBA32
        if (sourceFormat == VideoPixelFormat.Gray8 && targetFormat == VideoPixelFormat.Rgba32)
        {
            var src = MemoryMarshal.Cast<byte, Gray8>(source);
            var dst = MemoryMarshal.Cast<byte, Rgba32>(destination);
            Rgba32.FromGray8(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // RGB24 → RGBA32 (добавляем альфа=255)
        if (sourceFormat == VideoPixelFormat.Rgb24 && targetFormat == VideoPixelFormat.Rgba32)
        {
            var src = MemoryMarshal.Cast<byte, Rgb24>(source);
            var dst = MemoryMarshal.Cast<byte, Rgba32>(destination);
            Rgba32.FromRgb24(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // RGBA32 → RGB24 (отбрасываем альфа)
        if (sourceFormat == VideoPixelFormat.Rgba32 && targetFormat == VideoPixelFormat.Rgb24)
        {
            var src = MemoryMarshal.Cast<byte, Rgba32>(source);
            var dst = MemoryMarshal.Cast<byte, Rgb24>(destination);
            Rgb24.FromRgba32(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // BGR24 → RGBA32
        if (sourceFormat == VideoPixelFormat.Bgr24 && targetFormat == VideoPixelFormat.Rgba32)
        {
            var src = MemoryMarshal.Cast<byte, Bgr24>(source);
            var dst = MemoryMarshal.Cast<byte, Rgba32>(destination);
            Rgba32.FromBgr24(src[..pixelCount], dst[..pixelCount]);
            return;
        }

        // BGRA32 → RGB24
        if (sourceFormat == VideoPixelFormat.Bgra32 && targetFormat == VideoPixelFormat.Rgb24)
        {
            var src = MemoryMarshal.Cast<byte, Bgra32>(source);
            var dst = MemoryMarshal.Cast<byte, Rgb24>(destination);
            Rgb24.FromBgra32(src[..pixelCount], dst[..pixelCount], Acceleration);
            return;
        }

        // Fallback: форматы не поддерживаются
        Logger?.LogPngError($"Конвертация {sourceFormat} → {targetFormat} не поддерживается");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Определяет VideoPixelFormat из PNG ColorType и BitDepth.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VideoPixelFormat GetPixelFormat(byte colorType, byte bitDepth) => colorType switch
    {
        ColorTypeGrayscale when bitDepth <= 8 => VideoPixelFormat.Gray8,
        ColorTypeGrayscale => VideoPixelFormat.Gray16Le,
        ColorTypeRgb when bitDepth == 8 => VideoPixelFormat.Rgb24,
        ColorTypeRgba when bitDepth == 8 => VideoPixelFormat.Rgba32,
        ColorTypeIndexed => VideoPixelFormat.Rgb24, // После распаковки палитры
        ColorTypeGrayscaleAlpha when bitDepth == 8 => VideoPixelFormat.Rgba32, // Расширяется до RGBA
        _ => VideoPixelFormat.Unknown,
    };

    /// <summary>
    /// Возвращает количество байт на пиксель для формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBytesPerPixel(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Gray8 => 1,
        VideoPixelFormat.Gray16Le => 2,
        VideoPixelFormat.Rgb24 or VideoPixelFormat.Bgr24 => 3,
        VideoPixelFormat.Rgba32 or VideoPixelFormat.Bgra32 => 4,
        _ => 0,
    };

    /// <summary>
    /// Определяет, содержит ли формат альфа-канал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasAlpha(VideoPixelFormat format) =>
        format is VideoPixelFormat.Rgba32 or VideoPixelFormat.Bgra32;

    #endregion
}
