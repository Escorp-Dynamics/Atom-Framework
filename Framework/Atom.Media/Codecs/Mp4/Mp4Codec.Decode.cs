#pragma warning disable IDE0010, IDE0047, IDE0048, IDE0078, S109, S3776, MA0051

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media;

/// <summary>
/// Декодирование MP4 (Store mode + H.264).
/// </summary>
public sealed partial class Mp4Codec
{
    #region Decode

    /// <inheritdoc/>
    public CodecResult Decode(ReadOnlySpan<byte> packet, ref VideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (isEncoder)
            return CodecResult.UnsupportedFormat;

        if (packet.Length < 4)
            return CodecResult.InvalidData;

        // Check data format and route accordingly
        ref var dataRef = ref MemoryMarshal.GetReference(packet);
        var magic = Unsafe.ReadUnaligned<uint>(ref dataRef);

        // 1. AFRM Store mode (raw pixel data)
        if (magic == AfrmMagic && packet.Length >= AfrmHeaderSize)
        {
            return DecodeAfrmFast(ref dataRef, packet.Length, ref frame);
        }

        // 2. H.264 Annex B (start code 0x00000001 or 0x000001)
        if (IsAnnexBStartCode(packet))
        {
            return DecodeH264AnnexB(packet, ref frame);
        }

        // 3. H.264 AVCC (length-prefixed NALs from MP4/MKV demuxer)
        if (h264Decoder is not null && h264Sps is not null && h264Pps is not null)
        {
            return DecodeH264Avcc(packet, ref frame);
        }

        return CodecResult.InvalidData;
    }

    /// <inheritdoc/>
    public ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> packet,
        VideoFrameBuffer buffer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        var frame = buffer.AsFrame();
        var result = Decode(packet.Span, ref frame);
        return new ValueTask<CodecResult>(result);
    }

    #endregion

    #region AFRM Decoding

    /// <summary>
    /// Декодирует AFRM chunk с zero-copy SIMD.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe CodecResult DecodeAfrmFast(ref byte dataRef, int dataLength, ref VideoFrame frame)
    {
        // Читаем AFRM header
        var width = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref dataRef, 4));
        var height = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref dataRef, 8));
        var format = (VideoPixelFormat)Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref dataRef, 12));
        // frameIndex at offset 16 (8 bytes) - skip
        // flags at offset 24 (4 bytes) - skip
        var plane0Size = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref dataRef, 28));
        var plane1Size = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref dataRef, 32));
        var plane2Size = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref dataRef, 36));

        // Валидация
        if ((uint)(width - 1) >= MaxFrameSize || (uint)(height - 1) >= MaxFrameSize)
            return CodecResult.InvalidData;

        var totalDataSize = plane0Size + plane1Size + plane2Size;
        if (dataLength < AfrmHeaderSize + totalDataSize)
            return CodecResult.InvalidData;

        // Проверяем frame
        if (frame.Width != width || frame.Height != height)
            return CodecResult.InvalidData;

        if (frame.PixelFormat != format)
            return CodecResult.UnsupportedFormat;

        // Указатель на пиксельные данные
        var pSrc = (byte*)Unsafe.AsPointer(ref Unsafe.Add(ref dataRef, AfrmHeaderSize));

        var planeCount = format.GetPlaneCount();

        if (planeCount == 1)
        {
            // Packed формат
            CopyToPlane(pSrc, frame.PackedData, plane0Size);
        }
        else
        {
            // Planar формат (YUV)
            CopyToPlane(pSrc, frame.GetPlaneY(), plane0Size);

            if (plane1Size > 0)
            {
                CopyToPlane(pSrc + plane0Size, frame.GetPlaneU(), plane1Size);
            }

            if (plane2Size > 0)
            {
                CopyToPlane(pSrc + plane0Size + plane1Size, frame.GetPlaneV(), plane2Size);
            }
        }

        return CodecResult.Success;
    }

    /// <summary>
    /// Копирует данные в плоскость с SIMD оптимизацией.
    /// Учитывает stride — копирует построчно.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void CopyToPlane(byte* src, Plane<byte> plane, int expectedSize)
    {
        var width = plane.Width;
        var height = plane.Height;
        var stride = plane.Stride;

        // Если stride == width, можно копировать блоком (нет padding)
        if (stride == width)
        {
            var dstSpan = plane.Data;
            if (dstSpan.Length >= expectedSize)
            {
                fixed (byte* pDst = dstSpan)
                {
                    CopyBlockSimdDecode(src, pDst, expectedSize);
                }
            }
        }
        else
        {
            // Построчное копирование (stride > width)
            fixed (byte* pDst = plane.Data)
            {
                for (var y = 0; y < height; y++)
                {
                    CopyBlockSimdDecode(src + (y * width), pDst + (y * stride), width);
                }
            }
        }
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

    #region H.264 Decoding

    /// <summary>
    /// Detects Annex B start code (0x00000001 or 0x000001).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAnnexBStartCode(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0 && data[1] == 0 &&
        ((data[2] == 0 && data[3] == 1) || data[2] == 1);

    /// <summary>
    /// Decodes H.264 Annex B bitstream (start code delimited NAL units).
    /// </summary>
    private CodecResult DecodeH264AnnexB(ReadOnlySpan<byte> packet, ref VideoFrame frame)
    {
        h264Decoder ??= new H264Decoder();
        return h264Decoder.Decode(packet, ref frame);
    }

    /// <summary>
    /// Decodes H.264 AVCC bitstream (length-prefixed NAL units from MP4/MKV).
    /// </summary>
    private CodecResult DecodeH264Avcc(ReadOnlySpan<byte> packet, ref VideoFrame frame) =>
        h264Decoder!.DecodeAvcc(packet, nalLengthSize, h264Sps!, h264Pps!, ref frame);

    #endregion
}
