#pragma warning disable MA0003, MA0051, S3776, IDE0075, S1125

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Atom.Media;

internal static class WebpContainerParser
{
    private const uint Vp8ChunkMagic = 0x20385056;
    private const uint Vp8LChunkMagic = 0x4C385056;
    private const uint Vp8XChunkMagic = 0x58385056;
    private const uint AlphChunkMagic = 0x48504C41;
    private const uint IccpChunkMagic = 0x50434349;
    private const uint ExifChunkMagic = 0x46495845;
    private const uint XmpChunkMagic = 0x20504D58;
    private const uint AnimChunkMagic = 0x4D494E41;
    private const uint AnmfChunkMagic = 0x464D4E41;

    private const byte Vp8xIccpFlag = 0x20;
    private const byte Vp8xAlphaFlag = 0x10;
    private const byte Vp8xExifFlag = 0x08;
    private const byte Vp8xXmpFlag = 0x04;
    private const byte Vp8xAnimationFlag = 0x02;

    internal static CodecResult Parse(ReadOnlySpan<byte> data, out WebpContainerInfo info)
    {
        info = default;

        if (data.Length < WebpCodec.MinHeaderSize)
        {
            return CodecResult.InvalidData;
        }

        if (!data[..4].SequenceEqual(WebpCodec.RiffSignature) || !data[8..12].SequenceEqual(WebpCodec.WebpSignature))
        {
            return CodecResult.InvalidData;
        }

        var riffPayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (riffPayloadSize < 4)
        {
            return CodecResult.InvalidData;
        }

        var riffEnd = 8L + riffPayloadSize;
        if (riffEnd > data.Length)
        {
            return CodecResult.InvalidData;
        }

        var offset = 12;
        var hasVp8X = false;
        var hasIccp = false;
        var hasAlphaChunk = false;
        var hasAnimationChunk = false;
        var hasAnimationFrame = false;
        var hasImageChunk = false;
        var vp8xHasAlpha = false;
        var vp8xHasAnimation = false;
        var vp8xHasIccp = false;
        var vp8xHasExif = false;
        var vp8xHasXmp = false;

        var width = 0;
        var height = 0;
        var hasAlpha = false;
        var isLossless = false;
        var imagePayloadOffset = 0;
        var imagePayloadSize = 0;
        var alphaPayloadOffset = 0;
        var alphaPayloadSize = 0;
        var animationBackgroundColor = 0U;
        List<AnimatedFrameInfo>? animationFrames = null;

        while (offset + 8 <= riffEnd)
        {
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var paddedChunkSize = (long)chunkSize + (chunkSize & 1U);
            var nextOffset = offset + 8L + paddedChunkSize;

            if (nextOffset > riffEnd)
            {
                return CodecResult.InvalidData;
            }

            if ((chunkSize & 1U) != 0)
            {
                var paddingOffset = offset + 8 + (int)chunkSize;
                if (data[paddingOffset] != 0)
                {
                    return CodecResult.InvalidData;
                }
            }

            var payloadOffset = offset + 8;
            var payloadLength = (int)chunkSize;
            var payload = data.Slice(payloadOffset, payloadLength);

            switch (chunkType)
            {
                case Vp8XChunkMagic:
                    if (hasVp8X || hasIccp || hasAlphaChunk || hasAnimationChunk || hasAnimationFrame || hasImageChunk || offset != 12)
                    {
                        return CodecResult.InvalidData;
                    }

                    if (payloadLength != 10)
                    {
                        return CodecResult.InvalidData;
                    }

                    hasVp8X = true;
                    vp8xHasIccp = (payload[0] & Vp8xIccpFlag) != 0;
                    vp8xHasAlpha = (payload[0] & Vp8xAlphaFlag) != 0;
                    vp8xHasExif = (payload[0] & Vp8xExifFlag) != 0;
                    vp8xHasXmp = (payload[0] & Vp8xXmpFlag) != 0;
                    vp8xHasAnimation = (payload[0] & Vp8xAnimationFlag) != 0;

                    width = ReadUint24(payload.Slice(4, 3)) + 1;
                    height = ReadUint24(payload.Slice(7, 3)) + 1;

                    if (width <= 0 || height <= 0 || (ulong)width * (ulong)height > uint.MaxValue)
                    {
                        return CodecResult.InvalidData;
                    }

                    hasAlpha = vp8xHasAlpha;
                    break;

                case IccpChunkMagic:
                    if (!hasVp8X || hasIccp || hasAlphaChunk || hasAnimationChunk || hasAnimationFrame || hasImageChunk)
                    {
                        return CodecResult.InvalidData;
                    }

                    hasIccp = true;
                    break;

                case AlphChunkMagic:
                    if (!hasVp8X || vp8xHasAnimation || hasAlphaChunk || hasAnimationChunk || hasAnimationFrame || hasImageChunk)
                    {
                        return CodecResult.InvalidData;
                    }

                    if (payloadLength < 1)
                    {
                        return CodecResult.InvalidData;
                    }

                    hasAlphaChunk = true;
                    alphaPayloadOffset = payloadOffset;
                    alphaPayloadSize = payloadLength;
                    break;

                case AnimChunkMagic:
                    if (!hasVp8X || !vp8xHasAnimation || hasAlphaChunk || hasAnimationChunk || hasAnimationFrame || hasImageChunk)
                    {
                        return CodecResult.InvalidData;
                    }

                    if (payloadLength != 6)
                    {
                        return CodecResult.InvalidData;
                    }

                    hasAnimationChunk = true;
                    animationBackgroundColor = BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);
                    break;

                case AnmfChunkMagic:
                    if (!hasVp8X || !vp8xHasAnimation || !hasAnimationChunk || hasAlphaChunk || hasImageChunk)
                    {
                        return CodecResult.InvalidData;
                    }

                    if (payloadLength < 16)
                    {
                        return CodecResult.InvalidData;
                    }

                    hasAnimationFrame = true;

                    if (!TryParseAnimationFrame(payload,
                        width,
                        height,
                        offset + 8,
                        out var frameInfo))
                    {
                        return CodecResult.InvalidData;
                    }

                    animationFrames ??= [];
                    animationFrames.Add(frameInfo);
                    break;

                case Vp8ChunkMagic:
                    if (hasImageChunk || hasAnimationFrame || (hasVp8X && vp8xHasAnimation))
                    {
                        return CodecResult.InvalidData;
                    }

                    if (!TryReadVp8Info(payload, out var vp8Width, out var vp8Height))
                    {
                        return CodecResult.InvalidData;
                    }

                    if (hasVp8X)
                    {
                        if (vp8Width != width || vp8Height != height)
                        {
                            return CodecResult.InvalidData;
                        }

                        if (vp8xHasAlpha && !hasAlphaChunk)
                        {
                            return CodecResult.InvalidData;
                        }
                    }

                    hasImageChunk = true;
                    isLossless = false;
                    hasAlpha = hasVp8X && vp8xHasAlpha;
                    imagePayloadOffset = payloadOffset;
                    imagePayloadSize = payloadLength;
                    width = vp8Width;
                    height = vp8Height;
                    break;

                case Vp8LChunkMagic:
                    if (hasImageChunk || hasAnimationFrame || (hasVp8X && vp8xHasAnimation) || hasAlphaChunk)
                    {
                        return CodecResult.InvalidData;
                    }

                    if (!TryReadVp8LInfo(payload, out var vp8LWidth, out var vp8LHeight, out var vp8LAlphaHint))
                    {
                        return CodecResult.InvalidData;
                    }

                    if (hasVp8X && (vp8LWidth != width || vp8LHeight != height))
                    {
                        return CodecResult.InvalidData;
                    }

                    hasImageChunk = true;
                    isLossless = true;
                    hasAlpha = hasVp8X ? (vp8xHasAlpha || vp8LAlphaHint) : vp8LAlphaHint;
                    imagePayloadOffset = payloadOffset;
                    imagePayloadSize = payloadLength;
                    width = vp8LWidth;
                    height = vp8LHeight;
                    break;

                case ExifChunkMagic:
                case XmpChunkMagic:
                    break;

                default:
                    break;
            }

            offset = (int)nextOffset;
        }

        if (offset != riffEnd)
        {
            return CodecResult.InvalidData;
        }

        if (hasVp8X)
        {
            if (vp8xHasIccp != hasIccp)
            {
                return CodecResult.InvalidData;
            }

            if (vp8xHasAnimation)
            {
                if (!hasAnimationChunk || !hasAnimationFrame)
                {
                    return CodecResult.InvalidData;
                }

                var animationHasAlpha = false;
                if (animationFrames is not null)
                {
                    foreach (var animationFrame in animationFrames)
                    {
                        if (!animationFrame.HasAlpha)
                        {
                            continue;
                        }

                        animationHasAlpha = true;
                        break;
                    }
                }

                if (animationHasAlpha && !vp8xHasAlpha)
                {
                    return CodecResult.InvalidData;
                }

                info = new WebpContainerInfo(width, height, vp8xHasAlpha, false, true, 0, 0, false, 0, 0,
                    animationBackgroundColor, animationFrames?.ToArray() ?? []);
                return CodecResult.Success;
            }

            if (!hasImageChunk)
            {
                return CodecResult.InvalidData;
            }

            if (vp8xHasExif != ContainsChunk(data, riffEnd, ExifChunkMagic) || vp8xHasXmp != ContainsChunk(data, riffEnd, XmpChunkMagic))
            {
                return CodecResult.InvalidData;
            }
        }
        else if (!hasImageChunk)
        {
            return CodecResult.InvalidData;
        }

        info = new WebpContainerInfo(width, height, hasAlpha, isLossless, false, imagePayloadOffset, imagePayloadSize, hasAlphaChunk, alphaPayloadOffset, alphaPayloadSize,
            0, []);
        return CodecResult.Success;
    }

    private static bool TryParseAnimationFrame(ReadOnlySpan<byte> payload,
        int canvasWidth,
        int canvasHeight,
        int framePayloadAbsoluteOffset,
        out AnimatedFrameInfo frameInfo)
    {
        frameInfo = default;

        if (payload.Length < 16)
        {
            return false;
        }

        var frameX = ReadUint24(payload[..3]) * 2;
        var frameY = ReadUint24(payload.Slice(3, 3)) * 2;
        var frameWidth = ReadUint24(payload.Slice(6, 3)) + 1;
        var frameHeight = ReadUint24(payload.Slice(9, 3)) + 1;
        var duration = ReadUint24(payload.Slice(12, 3));
        var flags = payload[15];

        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return false;
        }

        if (frameX + frameWidth > canvasWidth || frameY + frameHeight > canvasHeight)
        {
            return false;
        }

        var subOffset = 16;
        var hasAlphaChunk = false;
        var hasImageChunk = false;
        var alphaPayloadOffset = 0;
        var alphaPayloadSize = 0;
        var imagePayloadOffset = 0;
        var imagePayloadSize = 0;
        var isLossless = false;
        var hasAlpha = false;
        var hasUnknownChunk = false;

        while (subOffset + 8 <= payload.Length)
        {
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(subOffset, 4));
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(subOffset + 4, 4));
            var paddedChunkSize = (int)chunkSize + (int)(chunkSize & 1U);
            var nextOffset = subOffset + 8 + paddedChunkSize;
            if (nextOffset > payload.Length)
            {
                return false;
            }

            if ((chunkSize & 1U) != 0 && payload[subOffset + 8 + (int)chunkSize] != 0)
            {
                return false;
            }

            var absolutePayloadOffset = framePayloadAbsoluteOffset + subOffset + 8;
            var subPayload = payload.Slice(subOffset + 8, (int)chunkSize);

            if (chunkType == AlphChunkMagic)
            {
                if (hasUnknownChunk || hasAlphaChunk || hasImageChunk || subPayload.Length < 1)
                {
                    return false;
                }

                hasAlphaChunk = true;
                hasAlpha = true;
                alphaPayloadOffset = absolutePayloadOffset;
                alphaPayloadSize = subPayload.Length;
            }
            else if (chunkType == Vp8ChunkMagic)
            {
                if (hasUnknownChunk || hasImageChunk || !TryReadVp8Info(subPayload, out var bitstreamWidth, out var bitstreamHeight))
                {
                    return false;
                }

                if (bitstreamWidth != frameWidth || bitstreamHeight != frameHeight)
                {
                    return false;
                }

                hasImageChunk = true;
                imagePayloadOffset = absolutePayloadOffset;
                imagePayloadSize = subPayload.Length;
                isLossless = false;
            }
            else if (chunkType == Vp8LChunkMagic)
            {
                if (hasUnknownChunk || hasImageChunk || hasAlphaChunk || !TryReadVp8LInfo(subPayload, out var bitstreamWidth, out var bitstreamHeight, out var bitstreamAlphaHint))
                {
                    return false;
                }

                if (bitstreamWidth != frameWidth || bitstreamHeight != frameHeight)
                {
                    return false;
                }

                hasImageChunk = true;
                imagePayloadOffset = absolutePayloadOffset;
                imagePayloadSize = subPayload.Length;
                isLossless = true;
                hasAlpha = bitstreamAlphaHint;
            }
            else
            {
                hasUnknownChunk = true;
            }

            subOffset = nextOffset;
        }

        if (!hasImageChunk)
        {
            return false;
        }

        frameInfo = new AnimatedFrameInfo(frameX, frameY, frameWidth, frameHeight, duration, (flags & 0x02) != 0, (flags & 0x01) != 0,
            hasAlpha,
            imagePayloadOffset, imagePayloadSize, isLossless, hasAlphaChunk, alphaPayloadOffset, alphaPayloadSize);
        return true;
    }

    private static bool ContainsChunk(ReadOnlySpan<byte> data, long riffEnd, uint chunkType)
    {
        for (var offset = 12; offset + 8 <= riffEnd;)
        {
            var currentChunkType = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var nextOffset = offset + 8L + chunkSize + (chunkSize & 1U);

            if (nextOffset > riffEnd)
            {
                return false;
            }

            if (currentChunkType == chunkType)
            {
                return true;
            }

            offset = (int)nextOffset;
        }

        return false;
    }

    private static int ReadUint24(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);

    private static bool TryReadVp8Info(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 10)
        {
            return false;
        }

        if (data[3] != Codecs.Webp.Vp8.Vp8Constants.SyncCode0 ||
            data[4] != Codecs.Webp.Vp8.Vp8Constants.SyncCode1 ||
            data[5] != Codecs.Webp.Vp8.Vp8Constants.SyncCode2)
        {
            return false;
        }

        width = (data[6] | (data[7] << 8)) & 0x3FFF;
        height = (data[8] | (data[9] << 8)) & 0x3FFF;
        return width > 0 && height > 0;
    }

    private static bool TryReadVp8LInfo(ReadOnlySpan<byte> data, out int width, out int height, out bool alphaHint)
    {
        width = 0;
        height = 0;
        alphaHint = false;

        if (data.Length < WebpCodec.Vp8LHeaderSize)
        {
            return false;
        }

        if (data[0] != WebpCodec.Vp8LSignature)
        {
            return false;
        }

        var packed = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1, 4));
        width = (int)(packed & 0x3FFF) + 1;
        height = (int)((packed >> 14) & 0x3FFF) + 1;
        alphaHint = ((packed >> 28) & 1) != 0;
        var version = (packed >> 29) & 0x7;

        return width > 0 && height > 0 && version == 0;
    }
}

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WebpContainerInfo(
    int Width,
    int Height,
    bool HasAlpha,
    bool IsLossless,
    bool IsAnimated,
    int ImagePayloadOffset,
    int ImagePayloadSize,
    bool HasAlphaChunk,
    int AlphaPayloadOffset,
    int AlphaPayloadSize,
    uint AnimationBackgroundColor,
    AnimatedFrameInfo[] AnimationFrames);

[StructLayout(LayoutKind.Auto)]
internal readonly record struct AnimatedFrameInfo(
    int FrameX,
    int FrameY,
    int FrameWidth,
    int FrameHeight,
    int Duration,
    bool DoNotBlend,
    bool DisposeToBackground,
    bool HasAlpha,
    int ImagePayloadOffset,
    int ImagePayloadSize,
    bool IsLossless,
    bool HasAlphaChunk,
    int AlphaPayloadOffset,
    int AlphaPayloadSize);