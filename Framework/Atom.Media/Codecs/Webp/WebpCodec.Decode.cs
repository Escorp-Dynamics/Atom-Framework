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

        if (data.Length >= MinHeaderSize)
        {
            ref var dataRef = ref MemoryMarshal.GetReference(data);
            var riff = Unsafe.ReadUnaligned<uint>(ref dataRef);
            var webp = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref dataRef, 8));

            if (riff == 0x46464952 && webp == 0x50424557)
            {
                var containerResult = WebpContainerParser.Parse(data, out var containerInfo);
                if (containerResult != CodecResult.Success)
                {
                    return containerResult;
                }

                if (containerInfo.IsAnimated)
                {
                    return DecodeAnimated(data, containerInfo, ref frame);
                }

                var imageData = data.Slice(containerInfo.ImagePayloadOffset, containerInfo.ImagePayloadSize);
                if (containerInfo.IsLossless)
                {
                    using var vp8lDecoder = new Vp8LDecoder();
                    return vp8lDecoder.Decode(imageData, ref frame);
                }

                if (frame.PixelFormat == VideoPixelFormat.Rgba32)
                {
                    var vp8Decoder = new Vp8Decoder();
                    var vp8Result = vp8Decoder.Decode(imageData, ref frame);
                    if (vp8Result != CodecResult.Success)
                    {
                        return vp8Result;
                    }

                    if (!containerInfo.HasAlphaChunk)
                    {
                        return CodecResult.Success;
                    }

                    var alphaData = data.Slice(containerInfo.AlphaPayloadOffset, containerInfo.AlphaPayloadSize);
                    return TryDecodeAlphaChunk(alphaData, ref frame)
                        ? CodecResult.Success
                        : CodecResult.InvalidData;
                }

                return DecodeLossyChunk(data, ref frame);
            }
        }

        return CodecResult.InvalidData;
    }

    private static CodecResult DecodeAnimated(ReadOnlySpan<byte> data, in WebpContainerInfo containerInfo, ref VideoFrame frame)
    {
        if (containerInfo.AnimationFrames.Length == 0)
        {
            return CodecResult.InvalidData;
        }

        if (frame.Width != containerInfo.Width || frame.Height != containerInfo.Height)
        {
            return CodecResult.InvalidData;
        }

        if (frame.PixelFormat != VideoPixelFormat.Rgba32)
        {
            return CodecResult.UnsupportedFormat;
        }

        FillRgbaFrame(ref frame, containerInfo.AnimationBackgroundColor);

        AnimatedFrameInfo? pendingDisposeFrame = null;
        foreach (var animationFrame in containerInfo.AnimationFrames)
        {
            if (pendingDisposeFrame is { DisposeToBackground: true } previousFrame)
            {
                FillRgbaRectangle(ref frame,
                    previousFrame.FrameX,
                    previousFrame.FrameY,
                    previousFrame.FrameWidth,
                    previousFrame.FrameHeight,
                    containerInfo.AnimationBackgroundColor);
            }

            using var frameBuffer = new VideoFrameBuffer(animationFrame.FrameWidth, animationFrame.FrameHeight, VideoPixelFormat.Rgba32);
            var frameData = frameBuffer.AsFrame();
            var decodeResult = DecodeAnimatedFrame(data, animationFrame, ref frameData);
            if (decodeResult != CodecResult.Success)
            {
                return decodeResult;
            }

            BlitAnimatedFrame(frameBuffer.AsReadOnlyFrame(), animationFrame, ref frame);
            pendingDisposeFrame = animationFrame;
        }

        return CodecResult.Success;
    }

    private static CodecResult DecodeAnimatedFrame(ReadOnlySpan<byte> data, in AnimatedFrameInfo animationFrame, ref VideoFrame frame)
    {
        var imageData = data.Slice(animationFrame.ImagePayloadOffset, animationFrame.ImagePayloadSize);

        if (animationFrame.IsLossless)
        {
            using var vp8lDecoder = new Vp8LDecoder();
            return vp8lDecoder.Decode(imageData, ref frame);
        }

        var vp8Decoder = new Vp8Decoder();
        var vp8Result = vp8Decoder.Decode(imageData, ref frame);
        if (vp8Result != CodecResult.Success)
        {
            return vp8Result;
        }

        if (!animationFrame.HasAlphaChunk)
        {
            return CodecResult.Success;
        }

        var alphaData = data.Slice(animationFrame.AlphaPayloadOffset, animationFrame.AlphaPayloadSize);
        return TryDecodeAlphaChunk(alphaData, ref frame)
            ? CodecResult.Success
            : CodecResult.InvalidData;
    }

    private static void FillRgbaFrame(ref VideoFrame frame, uint backgroundColor)
    {
        var packedData = frame.PackedData;
        for (var y = 0; y < frame.Height; y++)
        {
            FillRgbaRow(packedData.GetRow(y), backgroundColor);
        }
    }

    private static void FillRgbaRectangle(ref VideoFrame frame, int x, int y, int width, int height, uint backgroundColor)
    {
        var packedData = frame.PackedData;
        var rowBytes = width * 4;
        for (var row = 0; row < height; row++)
        {
            FillRgbaRow(packedData.GetRow(y + row).Slice(x * 4, rowBytes), backgroundColor);
        }
    }

    private static void FillRgbaRow(Span<byte> row, uint backgroundColor)
    {
        var blue = (byte)(backgroundColor & 0xFF);
        var green = (byte)((backgroundColor >> 8) & 0xFF);
        var red = (byte)((backgroundColor >> 16) & 0xFF);
        var alpha = (byte)((backgroundColor >> 24) & 0xFF);

        for (var offset = 0; offset < row.Length; offset += 4)
        {
            row[offset] = red;
            row[offset + 1] = green;
            row[offset + 2] = blue;
            row[offset + 3] = alpha;
        }
    }

    private static void BlitAnimatedFrame(in ReadOnlyVideoFrame source, in AnimatedFrameInfo animationFrame, ref VideoFrame destination)
    {
        var srcData = source.PackedData;
        var dstData = destination.PackedData;

        for (var y = 0; y < source.Height; y++)
        {
            var srcRow = srcData.GetRow(y)[..(source.Width * 4)];
            var dstRow = dstData.GetRow(animationFrame.FrameY + y).Slice(animationFrame.FrameX * 4, source.Width * 4);

            if (animationFrame.DoNotBlend)
            {
                srcRow.CopyTo(dstRow);
                continue;
            }

            AlphaBlendRow(srcRow, dstRow);
        }
    }

    private static void AlphaBlendRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        for (var offset = 0; offset < source.Length; offset += 4)
        {
            var srcAlpha = source[offset + 3];
            if (srcAlpha == 255)
            {
                source.Slice(offset, 4).CopyTo(destination.Slice(offset, 4));
                continue;
            }

            if (srcAlpha == 0)
            {
                continue;
            }

            var dstAlpha = destination[offset + 3];
            var outAlpha = srcAlpha + ((dstAlpha * (255 - srcAlpha)) / 255);

            if (outAlpha == 0)
            {
                destination[offset] = 0;
                destination[offset + 1] = 0;
                destination[offset + 2] = 0;
                destination[offset + 3] = 0;
                continue;
            }

            destination[offset] = (byte)(((source[offset] * srcAlpha) + (destination[offset] * dstAlpha * (255 - srcAlpha) / 255)) / outAlpha);
            destination[offset + 1] = (byte)(((source[offset + 1] * srcAlpha) + (destination[offset + 1] * dstAlpha * (255 - srcAlpha) / 255)) / outAlpha);
            destination[offset + 2] = (byte)(((source[offset + 2] * srcAlpha) + (destination[offset + 2] * dstAlpha * (255 - srcAlpha) / 255)) / outAlpha);
            destination[offset + 3] = (byte)outAlpha;
        }
    }

    private static bool TryDecodeAlphaChunk(ReadOnlySpan<byte> alphaChunkData, ref VideoFrame frame)
    {
        if (alphaChunkData.Length < 1)
        {
            return false;
        }

        var header = alphaChunkData[0];
        var compression = header & 0x03;
        var filter = (header >> 2) & 0x03;
        var reserved = header >> 6;

        if (reserved != 0)
        {
            return false;
        }

        var width = frame.Width;
        var height = frame.Height;
        var alphaValues = new byte[width * height];

        if (compression == 0)
        {
            if (alphaChunkData.Length - 1 != alphaValues.Length)
            {
                return false;
            }

            alphaChunkData[1..].CopyTo(alphaValues);
        }
        else if (compression == 1)
        {
            using var decoder = new Vp8LDecoder();
            if (decoder.DecodeAlphaImageStream(alphaChunkData[1..], width, height, alphaValues) != CodecResult.Success)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        ApplyAlphaFilter(alphaValues, width, height, filter);

        var packedData = frame.PackedData;
        for (var y = 0; y < height; y++)
        {
            var row = packedData.GetRow(y);
            var srcOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                row[(x * 4) + 3] = alphaValues[srcOffset + x];
            }
        }

        return true;
    }

    private static void ApplyAlphaFilter(Span<byte> alphaValues, int width, int height, int filter)
    {
        if (filter == 0)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var predictor = filter switch
                {
                    1 => GetHorizontalAlphaPredictor(alphaValues, width, x, y),
                    2 => GetVerticalAlphaPredictor(alphaValues, width, x, y),
                    3 => PredictGradient(alphaValues, width, x, y),
                    _ => 0,
                };

                alphaValues[rowOffset + x] = (byte)((alphaValues[rowOffset + x] + predictor) & 0xFF);
            }
        }
    }

    private static byte GetHorizontalAlphaPredictor(ReadOnlySpan<byte> alphaValues, int width, int x, int y)
    {
        if (x == 0)
        {
            return y == 0 ? (byte)0 : alphaValues[(y - 1) * width];
        }

        return alphaValues[(y * width) + x - 1];
    }

    private static byte GetVerticalAlphaPredictor(ReadOnlySpan<byte> alphaValues, int width, int x, int y)
    {
        if (y == 0)
        {
            return x == 0 ? (byte)0 : alphaValues[x - 1];
        }

        return alphaValues[((y - 1) * width) + x];
    }

    private static byte PredictGradient(ReadOnlySpan<byte> alphaValues, int width, int x, int y)
    {
        if (x == 0 && y == 0)
        {
            return 0;
        }

        if (x == 0)
        {
            return alphaValues[((y - 1) * width) + x];
        }

        if (y == 0)
        {
            return alphaValues[(y * width) + x - 1];
        }

        var left = alphaValues[(y * width) + x - 1];
        var top = alphaValues[((y - 1) * width) + x];
        var topLeft = alphaValues[((y - 1) * width) + x - 1];
        return ClampToByte(left + top - topLeft);
    }

    private static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);

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

    #region Lossy Decoding

    private static CodecResult DecodeLossyChunk(ReadOnlySpan<byte> data, ref VideoFrame frame)
    {
        var pixelFormat = frame.PixelFormat;
        if (pixelFormat is not (VideoPixelFormat.Rgba32 or VideoPixelFormat.Rgb24))
        {
            return CodecResult.UnsupportedFormat;
        }

        using var decodedBuffer = new VideoFrameBuffer(frame.Width, frame.Height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();

        var vp8Decoder = new Vp8Decoder();
        var decodeResult = vp8Decoder.Decode(data, ref decodedFrame);
        if (decodeResult != CodecResult.Success)
        {
            return decodeResult;
        }

        var decoded = decodedBuffer.AsReadOnlyFrame();
        if (pixelFormat == VideoPixelFormat.Rgba32)
        {
            CopyRgbaFrame(decoded, ref frame);
            return CodecResult.Success;
        }

        CopyRgb24Frame(decoded, ref frame);
        return CodecResult.Success;
    }

    private static void CopyRgbaFrame(in ReadOnlyVideoFrame source, ref VideoFrame destination)
    {
        var rowBytes = source.Width * 4;
        for (var y = 0; y < source.Height; y++)
        {
            source.PackedData.GetRow(y)[..rowBytes].CopyTo(destination.PackedData.GetRow(y)[..rowBytes]);
        }
    }

    private static void CopyRgb24Frame(in ReadOnlyVideoFrame source, ref VideoFrame destination)
    {
        for (var y = 0; y < source.Height; y++)
        {
            var srcRow = source.PackedData.GetRow(y);
            var dstRow = destination.PackedData.GetRow(y);
            for (var x = 0; x < source.Width; x++)
            {
                var srcOffset = x * 4;
                var dstOffset = x * 3;
                dstRow[dstOffset] = srcRow[srcOffset];
                dstRow[dstOffset + 1] = srcRow[srcOffset + 1];
                dstRow[dstOffset + 2] = srcRow[srcOffset + 2];
            }
        }
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
