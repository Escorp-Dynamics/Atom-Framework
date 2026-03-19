using System.Reflection;

namespace Atom.Media.Video.Tests;

[TestFixture]
public class VideoStreamTests
{
    [Test]
    public void FromStillImagePngDecodesFrame()
    {
        var pngBytes = CreateTestPngBytes(2, 2);
        var parameters = new VideoCodecParameters
        {
            Width = 2,
            Height = 2,
            PixelFormat = VideoPixelFormat.Rgba32,
            FrameRate = 30,
        };

        using var stream = VideoStream.FromStillImage(pngBytes, ".png", parameters);
        var minimumFrameSize = 2 * 2 * 4;

        Assert.That(stream.StreamType, Is.EqualTo(MediaStreamType.Video));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Png));
        Assert.That(stream.HasFrame, Is.True);
        Assert.That(stream.CurrentFrame.Length, Is.GreaterThanOrEqualTo(minimumFrameSize));
        Assert.That(stream.EndOfStream, Is.False);
    }

    [Test]
    public void ReadLoopsCurrentFrameWhenLooped()
    {
        var pngBytes = CreateTestPngBytes(2, 2);
        var parameters = new VideoCodecParameters
        {
            Width = 2,
            Height = 2,
            PixelFormat = VideoPixelFormat.Rgba32,
        };

        using var stream = VideoStream.FromStillImage(pngBytes, ".png", parameters);
        stream.IsLooped = true;

        var doubledFrame = new byte[stream.CurrentFrame.Length * 2];
        var bytesRead = stream.Read(doubledFrame);
        var firstFrame = doubledFrame[..stream.CurrentFrame.Length].ToArray();
        var secondFrame = doubledFrame[stream.CurrentFrame.Length..].ToArray();

        Assert.That(bytesRead, Is.EqualTo(doubledFrame.Length));
        Assert.That(firstFrame, Is.EqualTo(secondFrame));
    }

    [Test]
    public async Task ReadFrameAsyncCopiesDecodedFrame()
    {
        var pngBytes = CreateTestPngBytes(3, 2);
        var parameters = new VideoCodecParameters
        {
            Width = 3,
            Height = 2,
            PixelFormat = VideoPixelFormat.Rgba32,
        };

        using var stream = VideoStream.FromStillImage(pngBytes, ".png", parameters);
        using var buffer = new VideoFrameBuffer(3, 2, VideoPixelFormat.Rgba32);

        var copied = await stream.ReadFrameAsync(buffer);
        var copiedFrame = buffer.AsReadOnlyFrame().PackedData;
        var copiedVisibleBytes = new byte[stream.CurrentFrame.Length];

        copiedFrame.GetRow(0).CopyTo(copiedVisibleBytes);
        copiedFrame.GetRow(1).CopyTo(copiedVisibleBytes.AsSpan(copiedFrame.Width));

        Assert.That(copied, Is.True);
        Assert.That(buffer.GetRawData().Length, Is.GreaterThan(stream.CurrentFrame.Length));
        Assert.That(copiedVisibleBytes, Is.EqualTo(stream.CurrentFrame.ToArray()));
    }

    [Test]
    public void CurrentFramePacksVisibleRowsForPackedFormats()
    {
        var parameters = new VideoCodecParameters
        {
            Width = 3,
            Height = 2,
            PixelFormat = VideoPixelFormat.Rgba32,
        };

        using var stream = CreateStreamWithFrame(parameters, static frame =>
        {
            var packedPlane = frame.PackedData;
            packedPlane.Data.Fill(0xEE);

            FillSequentialRow(packedPlane.GetRow(0), 1);
            FillSequentialRow(packedPlane.GetRow(1), 21);
        });

        var currentVideoFrame = stream.CurrentVideoFrame;
        var expected = new byte[]
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
            21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        };

        Assert.That(currentVideoFrame.PackedData.Stride, Is.GreaterThan(parameters.Width * parameters.PixelFormat.GetBytesPerPixel()));
        Assert.That(currentVideoFrame.PackedData.Data.Length, Is.GreaterThan(expected.Length));
        Assert.That(stream.CurrentFrame.Length, Is.EqualTo(expected.Length));
        Assert.That(stream.CurrentFrame.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void CopyCurrentFrameToPacksPlanarFormatsWithoutStridePadding()
    {
        var parameters = new VideoCodecParameters
        {
            Width = 4,
            Height = 4,
            PixelFormat = VideoPixelFormat.Yuv420P,
        };

        using var stream = CreateStreamWithFrame(parameters, static frame =>
        {
            var planeY = frame.GetPlaneY();
            var planeU = frame.GetPlaneU();
            var planeV = frame.GetPlaneV();

            planeY.Data.Fill(0xEE);
            planeU.Data.Fill(0xDD);
            planeV.Data.Fill(0xCC);

            FillSequentialRow(planeY.GetRow(0), 1);
            FillSequentialRow(planeY.GetRow(1), 5);
            FillSequentialRow(planeY.GetRow(2), 9);
            FillSequentialRow(planeY.GetRow(3), 13);

            FillSequentialRow(planeU.GetRow(0), 101);
            FillSequentialRow(planeU.GetRow(1), 103);

            FillSequentialRow(planeV.GetRow(0), 201);
            FillSequentialRow(planeV.GetRow(1), 203);
        });

        var copied = new byte[parameters.PixelFormat.CalculateFrameSize(parameters.Width, parameters.Height)];
        var expected = new byte[]
        {
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12,
            13, 14, 15, 16,
            101, 102,
            103, 104,
            201, 202,
            203, 204,
        };

        stream.CopyCurrentFrameTo(copied);

        Assert.That(stream.CurrentVideoFrame.GetPlaneY().Stride, Is.GreaterThan(parameters.Width));
        Assert.That(stream.CurrentVideoFrame.GetPlaneU().Stride, Is.GreaterThan(parameters.Width / 2));
        Assert.That(stream.CurrentVideoFrame.GetPlaneV().Stride, Is.GreaterThan(parameters.Width / 2));
        Assert.That(copied, Is.EqualTo(expected));
        Assert.That(stream.CurrentFrame.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void EndOfStreamUsesPackedFrameLengthAfterRead()
    {
        var pngBytes = CreateTestPngBytes(3, 2);
        var parameters = new VideoCodecParameters
        {
            Width = 3,
            Height = 2,
            PixelFormat = VideoPixelFormat.Rgba32,
        };

        using var stream = VideoStream.FromStillImage(pngBytes, ".png", parameters);
        var destination = new byte[stream.CurrentFrame.Length];

        var bytesRead = stream.Read(destination);

        Assert.That(bytesRead, Is.EqualTo(destination.Length));
        Assert.That(stream.EndOfStream, Is.True);
    }

    private static byte[] CreateTestPngBytes(int width, int height)
    {
        using var codec = new PngCodec();
        var parameters = new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32);
        codec.InitializeEncoder(parameters);

        using var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        buffer.GetRawData().Fill(0x7A);

        var estimatedSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[estimatedSize];
        var roFrame = buffer.AsReadOnlyFrame();
        codec.Encode(in roFrame, encoded, out var bytesWritten);

        return encoded[..bytesWritten];
    }

    private static VideoStream CreateStreamWithFrame(in VideoCodecParameters parameters, Action<VideoFrame> fillFrame)
    {
        var stream = new VideoStream(parameters);
        var frameBuffer = GetPrivateField<VideoFrameBuffer>(stream, "frameBuffer");
        var frame = frameBuffer.AsFrame();

        fillFrame(frame);

        SetPrivateProperty(stream, nameof(VideoStream.HasFrame), true);
        SetPrivateField(stream, "isPackedFrameDirty", true);
        stream.Reset();

        return stream;
    }

    private static void FillSequentialRow(Span<byte> row, byte startValue)
    {
        for (var index = 0; index < row.Length; index++)
        {
            row[index] = (byte)(startValue + index);
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);

        return (T)(field.GetValue(instance)
            ?? throw new InvalidOperationException($"Поле '{fieldName}' не содержит значения."));
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);

        field.SetValue(instance, value);
    }

    private static void SetPrivateProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);

        property.SetValue(instance, value);
    }
}