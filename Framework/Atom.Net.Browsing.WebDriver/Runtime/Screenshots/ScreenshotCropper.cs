using Atom.Media;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Atom.Net.Browsing.WebDriver;

internal static class ScreenshotCropper
{
    public static Memory<byte> CropPng(Memory<byte> screenshot, DrawingRectangle cropBounds)
    {
        if (screenshot.IsEmpty || cropBounds.Width <= 0 || cropBounds.Height <= 0)
            return Memory<byte>.Empty;

        using var decoder = new PngCodec();
        if (decoder.GetInfo(screenshot.Span, out var info) != CodecResult.Success)
            return Memory<byte>.Empty;

        var cropRectangle = NormalizeCropBounds(cropBounds, info.Width, info.Height);
        if (cropRectangle.Width <= 0 || cropRectangle.Height <= 0)
            return Memory<byte>.Empty;

        using var sourceBuffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        decoder.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        var sourceFrame = sourceBuffer.AsFrame();
        if (decoder.Decode(screenshot.Span, ref sourceFrame) != CodecResult.Success)
            return Memory<byte>.Empty;

        using var croppedBuffer = new VideoFrameBuffer(cropRectangle.Width, cropRectangle.Height, VideoPixelFormat.Rgba32);
        CopyCrop(sourceBuffer.AsReadOnlyFrame(), croppedBuffer.AsFrame(), cropRectangle);

        using var encoder = new PngCodec();
        encoder.InitializeEncoder(new ImageCodecParameters(cropRectangle.Width, cropRectangle.Height, VideoPixelFormat.Rgba32));

        var encoded = new byte[encoder.EstimateEncodedSize(cropRectangle.Width, cropRectangle.Height, VideoPixelFormat.Rgba32)];
        return encoder.Encode(croppedBuffer.AsReadOnlyFrame(), encoded, out var bytesWritten) == CodecResult.Success
            ? encoded.AsMemory(0, bytesWritten).ToArray()
            : Memory<byte>.Empty;
    }

    private static void CopyCrop(in ReadOnlyVideoFrame source, VideoFrame destination, DrawingRectangle cropRectangle)
    {
        var rowBytes = cropRectangle.Width * 4;
        for (var y = 0; y < cropRectangle.Height; y++)
        {
            var sourceRow = source.PackedData.GetRow(cropRectangle.Y + y).Slice(cropRectangle.X * 4, rowBytes);
            sourceRow.CopyTo(destination.PackedData.GetRow(y)[..rowBytes]);
        }
    }

    private static DrawingRectangle NormalizeCropBounds(DrawingRectangle cropBounds, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(cropBounds.Left, 0, imageWidth);
        var top = Math.Clamp(cropBounds.Top, 0, imageHeight);
        var right = Math.Clamp(cropBounds.Right, 0, imageWidth);
        var bottom = Math.Clamp(cropBounds.Bottom, 0, imageHeight);

        return right <= left || bottom <= top
            ? default
            : new DrawingRectangle(left, top, right - left, bottom - top);
    }
}