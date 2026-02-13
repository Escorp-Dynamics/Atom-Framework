#pragma warning disable IDE0010, IDE0047, IDE0048, IDE0078, S109, S3776, MA0051

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media;

/// <summary>
/// Декодирование WebP.
/// </summary>
public sealed partial class WebpCodec
{
    #region Decode

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult Decode(ReadOnlySpan<byte> data, ref VideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (data.Length < MinHeaderSize)
        {
            return CodecResult.InvalidData;
        }

        // Fast signature check без CanDecode
        ref var dataRef = ref MemoryMarshal.GetReference(data);
        var riff = Unsafe.ReadUnaligned<uint>(ref dataRef);
        var webp = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, 8));

        if (riff != 0x46464952 || webp != 0x50424557) // "RIFF" / "WEBP"
        {
            return CodecResult.InvalidData;
        }

        // Ищем chunk для декодирования
        var offset = 12; // После RIFF header
        var length = data.Length;

        while (offset + 8 <= length)
        {
            var chunkType = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, offset));
            var chunkSize = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref dataRef, offset + 4));

            // Наш внутренний ARAW chunk
            if (chunkType == ArawChunkMagic)
            {
                return DecodeArawChunkFast(ref Unsafe.Add(ref dataRef, offset + 8), chunkSize, ref frame);
            }

            // VP8L lossless / VP8 lossy — не поддерживаются (требуют полную реализацию кодека)
            if (chunkType is 0x4C385056 or 0x20385056) // "VP8L" / "VP8 "
            {
                return CodecResult.UnsupportedFormat;
            }

            // Следующий chunk (с выравниванием)
            offset += 8 + ((chunkSize + 1) & ~1);
        }

        return CodecResult.InvalidData;
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

    #region ARAW Decoding

    /// <summary>
    /// Декодирует ARAW chunk с zero-copy SIMD.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe CodecResult DecodeArawChunkFast(ref byte chunkStart, int chunkSize, ref VideoFrame frame)
    {
        if (chunkSize < 12)
        {
            return CodecResult.InvalidData;
        }

        // Читаем header без bounds checks
        var width = Unsafe.ReadUnaligned<int>(ref chunkStart);
        var height = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref chunkStart, 4));
        var flags = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref chunkStart, 8));
        var hasAlpha = (flags & 1) != 0;

        if ((uint)(width - 1) >= 16383 || (uint)(height - 1) >= 16383)
        {
            return CodecResult.InvalidData;
        }

        var bytesPerPixel = hasAlpha ? 4 : 3;
        var rowBytes = width * bytesPerPixel;
        var expectedDataSize = height * rowBytes;

        if (chunkSize < 12 + expectedDataSize)
        {
            return CodecResult.InvalidData;
        }

        // Проверяем frame
        if (frame.Width != width || frame.Height != height)
        {
            return CodecResult.InvalidData;
        }

        var expectedFormat = hasAlpha ? VideoPixelFormat.Rgba32 : VideoPixelFormat.Rgb24;
        if (frame.PixelFormat != expectedFormat)
        {
            return CodecResult.UnsupportedFormat;
        }

        // Указатель на начало пиксельных данных
        var pSrc = (byte*)Unsafe.AsPointer(ref Unsafe.Add(ref chunkStart, 12));
        var destData = frame.PackedData;

        // Для непрерывных данных — единый memcpy
        if (destData.Stride == rowBytes)
        {
            var dstSpan = destData.Data;
            fixed (byte* pDst = dstSpan)
            {
                CopyBlockSimdDecode(pSrc, pDst, expectedDataSize);
            }
        }
        else
        {
            // Построчное копирование (destination имеет padding)
            for (var y = 0; y < height; y++)
            {
                var dstRow = destData.GetRow(y);
                fixed (byte* pDst = dstRow)
                {
                    CopyBlockSimdDecode(pSrc, pDst, rowBytes);
                }
                pSrc += rowBytes;
            }
        }

        return CodecResult.Success;
    }

    /// <summary>
    /// SIMD блочное копирование для декодирования.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void CopyBlockSimdDecode(byte* src, byte* dst, int length)
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
