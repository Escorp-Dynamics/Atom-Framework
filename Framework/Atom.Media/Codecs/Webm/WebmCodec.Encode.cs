#pragma warning disable IDE0010, IDE0047, IDE0048, IDE0078, S109, S1871, S3776, MA0051

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media;

/// <summary>
/// Кодирование WebM (Store mode).
/// </summary>
public sealed partial class WebmCodec
{
    #region Encode

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        bytesWritten = 0;

        if (!isEncoder || !isInitialized)
            return CodecResult.UnsupportedFormat;

        if (!IsSupportedPixelFormat(frame.PixelFormat))
            return CodecResult.UnsupportedFormat;

        return EncodeAfrmFast(frame, output, out bytesWritten);
    }

    /// <inheritdoc/>
    public ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        VideoFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        var roFrame = frame.AsReadOnlyFrame();
        var result = Encode(roFrame, output.Span, out var written);
        return new ValueTask<(CodecResult, int)>((result, written));
    }

    #endregion

    #region AFRM Encoding

    /// <summary>
    /// AFRM (Atom Frame) формат:
    /// - magic (4 bytes): "AFRM"
    /// - width (4 bytes): ширина кадра
    /// - height (4 bytes): высота кадра
    /// - format (4 bytes): VideoPixelFormat
    /// - frameIndex (8 bytes): индекс кадра
    /// - flags (4 bytes): bit 0 = isKeyframe
    /// - plane0Size (4 bytes): размер плоскости 0
    /// - plane1Size (4 bytes): размер плоскости 1 (0 для packed)
    /// - plane2Size (4 bytes): размер плоскости 2 (0 для packed)
    /// - data: пиксельные данные всех плоскостей
    /// </summary>
    private const int AfrmHeaderSize = 44;

    /// <summary>
    /// Максимально быстрое кодирование — AFRM Store mode.
    /// Zero-copy SIMD блочное копирование.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private unsafe CodecResult EncodeAfrmFast(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten)
    {
        var width = frame.Width;
        var height = frame.Height;
        var format = frame.PixelFormat;

        // Вычисляем размеры плоскостей
        GetPlaneSizes(width, height, format, out var plane0Size, out var plane1Size, out var plane2Size);
        var totalDataSize = plane0Size + plane1Size + plane2Size;
        var totalSize = AfrmHeaderSize + totalDataSize;

        if (output.Length < totalSize)
        {
            bytesWritten = 0;
            return CodecResult.OutputBufferTooSmall;
        }

        ref var outRef = ref MemoryMarshal.GetReference(output);
        var pDst = (byte*)Unsafe.AsPointer(ref outRef);

        // === AFRM Header (44 bytes) ===
        Unsafe.WriteUnaligned(pDst, AfrmMagic);
        Unsafe.WriteUnaligned(pDst + 4, width);
        Unsafe.WriteUnaligned(pDst + 8, height);
        Unsafe.WriteUnaligned(pDst + 12, (int)format);
        Unsafe.WriteUnaligned(pDst + 16, frameIndex);
        Unsafe.WriteUnaligned(pDst + 24, 1); // flags: keyframe=1 (Store mode = all keyframes)
        Unsafe.WriteUnaligned(pDst + 28, plane0Size);
        Unsafe.WriteUnaligned(pDst + 32, plane1Size);
        Unsafe.WriteUnaligned(pDst + 36, plane2Size);
        // Reserved: pDst + 40..43

        var pPixelDst = pDst + AfrmHeaderSize;

        // === Копирование плоскостей ===
        var planeCount = format.GetPlaneCount();

        if (planeCount == 1)
        {
            // Packed формат — одна плоскость
            CopyPlaneData(frame.PackedData, pPixelDst, plane0Size);
        }
        else
        {
            // Planar формат — несколько плоскостей (YUV)
            CopyPlaneData(frame.GetPlaneY(), pPixelDst, plane0Size);

            if (plane1Size > 0)
            {
                CopyPlaneData(frame.GetPlaneU(), pPixelDst + plane0Size, plane1Size);
            }

            if (plane2Size > 0)
            {
                CopyPlaneData(frame.GetPlaneV(), pPixelDst + plane0Size + plane1Size, plane2Size);
            }
        }

        frameIndex++;
        bytesWritten = totalSize;
        return CodecResult.Success;
    }

    /// <summary>
    /// Вычисляет размеры плоскостей для формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetPlaneSizes(int width, int height, VideoPixelFormat format,
        out int plane0Size, out int plane1Size, out int plane2Size)
    {
        switch (format)
        {
            case VideoPixelFormat.Yuv420P:
                plane0Size = width * height;
                plane1Size = (width / 2) * (height / 2);
                plane2Size = (width / 2) * (height / 2);
                break;

            case VideoPixelFormat.Yuv422P:
                plane0Size = width * height;
                plane1Size = (width / 2) * height;
                plane2Size = (width / 2) * height;
                break;

            case VideoPixelFormat.Yuv444P:
                plane0Size = width * height;
                plane1Size = width * height;
                plane2Size = width * height;
                break;

            case VideoPixelFormat.Rgb24:
            case VideoPixelFormat.Bgr24:
                plane0Size = width * height * 3;
                plane1Size = 0;
                plane2Size = 0;
                break;

            case VideoPixelFormat.Rgba32:
            case VideoPixelFormat.Bgra32:
                plane0Size = width * height * 4;
                plane1Size = 0;
                plane2Size = 0;
                break;

            default:
                plane0Size = width * height * 4;
                plane1Size = 0;
                plane2Size = 0;
                break;
        }
    }

    /// <summary>
    /// Копирует данные плоскости с SIMD оптимизацией.
    /// Учитывает stride — копирует построчно.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void CopyPlaneData(ReadOnlyPlane<byte> plane, byte* dst, int expectedSize)
    {
        var width = plane.Width;
        var height = plane.Height;
        var stride = plane.Stride;

        // Если stride == width, можно копировать блоком (нет padding)
        if (stride == width)
        {
            var srcSpan = plane.Data;
            if (srcSpan.Length >= expectedSize)
            {
                fixed (byte* pSrc = srcSpan)
                {
                    CopyBlockSimd(pSrc, dst, expectedSize);
                }
            }
        }
        else
        {
            // Построчное копирование (stride > width)
            fixed (byte* pSrc = plane.Data)
            {
                for (var y = 0; y < height; y++)
                {
                    CopyBlockSimd(pSrc + (y * stride), dst + (y * width), width);
                }
            }
        }
    }

    /// <summary>
    /// SIMD блочное копирование без проверок границ.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void CopyBlockSimd(byte* src, byte* dst, int length)
    {
        var i = 0;

        // AVX2: 256 байт за итерацию (8x unroll)
        if (Avx2.IsSupported && length >= 256)
        {
            var end256 = length - 255;
            for (; i < end256; i += 256)
            {
                var v0 = Avx.LoadVector256(src + i);
                var v1 = Avx.LoadVector256(src + i + 32);
                var v2 = Avx.LoadVector256(src + i + 64);
                var v3 = Avx.LoadVector256(src + i + 96);
                var v4 = Avx.LoadVector256(src + i + 128);
                var v5 = Avx.LoadVector256(src + i + 160);
                var v6 = Avx.LoadVector256(src + i + 192);
                var v7 = Avx.LoadVector256(src + i + 224);

                Avx.Store(dst + i, v0);
                Avx.Store(dst + i + 32, v1);
                Avx.Store(dst + i + 64, v2);
                Avx.Store(dst + i + 96, v3);
                Avx.Store(dst + i + 128, v4);
                Avx.Store(dst + i + 160, v5);
                Avx.Store(dst + i + 192, v6);
                Avx.Store(dst + i + 224, v7);
            }

            // 32-byte blocks
            var end32 = length - 31;
            for (; i < end32; i += 32)
            {
                Avx.Store(dst + i, Avx.LoadVector256(src + i));
            }
        }
        else if (Sse2.IsSupported && length >= 64)
        {
            // SSE2: 64 байт за итерацию (4x unroll)
            var end64 = length - 63;
            for (; i < end64; i += 64)
            {
                var v0 = Sse2.LoadVector128(src + i);
                var v1 = Sse2.LoadVector128(src + i + 16);
                var v2 = Sse2.LoadVector128(src + i + 32);
                var v3 = Sse2.LoadVector128(src + i + 48);

                Sse2.Store(dst + i, v0);
                Sse2.Store(dst + i + 16, v1);
                Sse2.Store(dst + i + 32, v2);
                Sse2.Store(dst + i + 48, v3);
            }

            // 16-byte blocks
            var end16 = length - 15;
            for (; i < end16; i += 16)
            {
                Sse2.Store(dst + i, Sse2.LoadVector128(src + i));
            }
        }

        // 8-byte blocks
        var end8 = length - 7;
        for (; i < end8; i += 8)
        {
            *(ulong*)(dst + i) = *(ulong*)(src + i);
        }

        // Scalar tail
        for (; i < length; i++)
        {
            dst[i] = src[i];
        }
    }

    #endregion
}
