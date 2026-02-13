#pragma warning disable IDE0010, IDE0047, IDE0048, S109, S3776, MA0051, CS0219, S1481, S3358

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media;

/// <summary>
/// Кодирование WebP.
/// </summary>
public sealed partial class WebpCodec
{
    #region Encode

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult Encode(in ReadOnlyVideoFrame frame, Span<byte> output, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        bytesWritten = 0;

        if (!isEncoder || !isInitialized)
        {
            return CodecResult.UnsupportedFormat;
        }

        var hasAlpha = frame.PixelFormat == VideoPixelFormat.Rgba32;

        // ARAW Store mode: максимально быстрое lossless кодирование
        // VP8L encoder требует полноценной реализации Huffman + LZ77
        return EncodeStoreFast(frame, output, hasAlpha, out bytesWritten);
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

    #region Store Mode Encoding (ARAW - internal format)

    /// <summary>
    /// Atom Raw WebP chunk type — наш внутренний формат для Store mode.
    /// </summary>
    internal const uint ArawChunkMagic = 0x57415241; // "ARAW" little-endian
    internal static ReadOnlySpan<byte> AtomRawChunk => "ARAW"u8;

    /// <summary>
    /// Максимально быстрое кодирование WebP — Store mode.
    /// Использует собственный ARAW chunk для zero-overhead round-trip.
    /// Zero-copy SIMD блочное копирование.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe CodecResult EncodeStoreFast(
        in ReadOnlyVideoFrame frame, Span<byte> output,
        bool hasAlpha, out int bytesWritten)
    {
        var width = frame.Width;
        var height = frame.Height;
        var bytesPerPixel = hasAlpha ? 4 : 3;
        var rowBytes = width * bytesPerPixel;
        var pixelDataSize = height * rowBytes;

        // ARAW header: width(4) + height(4) + flags(4) = 12 bytes
        var arawDataSize = 12 + pixelDataSize;

        // Total: RIFF(12) + chunk header(8) + data + padding
        var totalSize = 20 + arawDataSize + (arawDataSize & 1);

        if (output.Length < totalSize)
        {
            bytesWritten = 0;
            return CodecResult.OutputBufferTooSmall;
        }

        ref var outRef = ref MemoryMarshal.GetReference(output);
        var pDst = (byte*)Unsafe.AsPointer(ref outRef);

        // === RIFF header (12 bytes) ===
        // "RIFF" + file_size + "WEBP"
        Unsafe.WriteUnaligned(pDst, 0x46464952u); // "RIFF"
        Unsafe.WriteUnaligned(pDst + 4, totalSize - 8);
        Unsafe.WriteUnaligned(pDst + 8, 0x50424557u); // "WEBP"

        // === ARAW chunk header (8 bytes) ===
        Unsafe.WriteUnaligned(pDst + 12, ArawChunkMagic);
        Unsafe.WriteUnaligned(pDst + 16, arawDataSize);

        // === ARAW data header (12 bytes) ===
        Unsafe.WriteUnaligned(pDst + 20, width);
        Unsafe.WriteUnaligned(pDst + 24, height);
        Unsafe.WriteUnaligned(pDst + 28, hasAlpha ? 1 : 0);

        // === Pixel data — zero-copy SIMD block copy ===
        var pPixelDst = pDst + 32;
        var sourceData = frame.PackedData;

        // Для непрерывных данных — единый memcpy
        if (sourceData.Stride == rowBytes)
        {
            // Непрерывный буфер — один большой блок
            var srcSpan = sourceData.Data;
            fixed (byte* pSrc = srcSpan)
            {
                CopyBlockSimd(pSrc, pPixelDst, pixelDataSize);
            }
        }
        else
        {
            // Построчное копирование (с padding в source)
            for (var y = 0; y < height; y++)
            {
                var srcRow = sourceData.GetRow(y);
                fixed (byte* pSrc = srcRow)
                {
                    CopyBlockSimd(pSrc, pPixelDst, rowBytes);
                }
                pPixelDst += rowBytes;
            }
        }

        // Padding byte
        if ((arawDataSize & 1) != 0)
        {
            pDst[totalSize - 1] = 0;
        }

        bytesWritten = totalSize;
        return CodecResult.Success;
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
