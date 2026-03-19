using System.Buffers.Binary;
using Atom.Media.Video;
using Atom.Media.Video.Backends.PipeWire;

namespace Atom.Media.Video.Tests;

[TestFixture]
public class SpaPodBuilderTests(ILogger logger) : BenchmarkTests<SpaPodBuilderTests>(logger)
{
    // Константы SPA типов
    private const uint SPA_TYPE_Id = 3;
    private const uint SPA_TYPE_Rectangle = 10;
    private const uint SPA_TYPE_Fraction = 11;
    private const uint SPA_TYPE_Object = 15;
    private const uint SPA_TYPE_OBJECT_Format = 0x40003;
    private const uint SPA_PARAM_EnumFormat = 3;

    // Свойства SPA формата
    private const uint SPA_FORMAT_mediaType = 1;
    private const uint SPA_FORMAT_mediaSubtype = 2;
    private const uint SPA_FORMAT_VIDEO_format = 0x20001;
    private const uint SPA_FORMAT_VIDEO_size = 0x20003;
    private const uint SPA_FORMAT_VIDEO_framerate = 0x20004;

    public SpaPodBuilderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Размер pod для видеоформата равен 136 байт")]
    public void PodSizeIs136Bytes()
    {
        Span<byte> buffer = stackalloc byte[256];
        var size = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 1920, height: 1080, frameRate: 30, VideoPixelFormat.Yuv420P);

        // 8 (object header) + 8 (type+id) + 5 * 24 (properties) = 136
        Assert.That(size, Is.EqualTo(136));
    }

    [TestCase(TestName = "Заголовок pod содержит правильный тип Object")]
    public void PodHeaderHasObjectType()
    {
        Span<byte> buffer = stackalloc byte[256];
        var size = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 640, height: 480, frameRate: 24, VideoPixelFormat.Rgb24);

        // Pod header: [bodySize:u32] [type:u32]
        var bodySize = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var podType = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);

        Assert.That(podType, Is.EqualTo(SPA_TYPE_Object));
        Assert.That(bodySize, Is.EqualTo((uint)(size - 8)));
    }

    [TestCase(TestName = "Тело Object содержит SPA_TYPE_OBJECT_Format и SPA_PARAM_EnumFormat")]
    public void ObjectBodyHasFormatTypeAndId()
    {
        Span<byte> buffer = stackalloc byte[256];
        _ = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 1280, height: 720, frameRate: 60, VideoPixelFormat.Nv12);

        // Object body начинается после 8-байтного header
        var objectType = BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]);
        var objectId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]);

        Assert.That(objectType, Is.EqualTo(SPA_TYPE_OBJECT_Format));
        Assert.That(objectId, Is.EqualTo(SPA_PARAM_EnumFormat));
    }

    [TestCase(TestName = "Первые два свойства: mediaType=video, mediaSubtype=raw")]
    public void MediaTypeAndSubtypeAreCorrect()
    {
        Span<byte> buffer = stackalloc byte[256];
        _ = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 640, height: 480, frameRate: 30, VideoPixelFormat.Rgba32);

        // После object header (8) + body header (8) = offset 16
        // Property 1: key(4) + flags(4) + pod_header(8) + value(4) + pad(4) = 24
        var prop1Key = BinaryPrimitives.ReadUInt32LittleEndian(buffer[16..]);
        var prop1Type = BinaryPrimitives.ReadUInt32LittleEndian(buffer[28..]);  // 16+4+4+4=28
        var prop1Value = BinaryPrimitives.ReadUInt32LittleEndian(buffer[32..]); // 28+4=32

        Assert.That(prop1Key, Is.EqualTo(SPA_FORMAT_mediaType));
        Assert.That(prop1Type, Is.EqualTo(SPA_TYPE_Id));
        Assert.That(prop1Value, Is.EqualTo(2u)); // SPA_MEDIA_TYPE_video

        // Property 2: offset 16 + 24 = 40
        var prop2Key = BinaryPrimitives.ReadUInt32LittleEndian(buffer[40..]);
        var prop2Value = BinaryPrimitives.ReadUInt32LittleEndian(buffer[56..]); // 40+4+4+4+4=56

        Assert.That(prop2Key, Is.EqualTo(SPA_FORMAT_mediaSubtype));
        Assert.That(prop2Value, Is.EqualTo(1u)); // SPA_MEDIA_SUBTYPE_raw
    }

    [TestCase(TestName = "Свойство VIDEO_size содержит переданные размеры")]
    public void VideoSizePropertyIsCorrect()
    {
        Span<byte> buffer = stackalloc byte[256];
        _ = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 3840, height: 2160, frameRate: 30, VideoPixelFormat.Bgra32);

        // Property 4 (VIDEO_size): offset = 16 + 3*24 = 88
        var propKey = BinaryPrimitives.ReadUInt32LittleEndian(buffer[88..]);
        var podType = BinaryPrimitives.ReadUInt32LittleEndian(buffer[100..]); // 88+4+4+4=100
        var width = BinaryPrimitives.ReadUInt32LittleEndian(buffer[104..]);
        var height = BinaryPrimitives.ReadUInt32LittleEndian(buffer[108..]);

        Assert.That(propKey, Is.EqualTo(SPA_FORMAT_VIDEO_size));
        Assert.That(podType, Is.EqualTo(SPA_TYPE_Rectangle));
        Assert.That(width, Is.EqualTo(3840u));
        Assert.That(height, Is.EqualTo(2160u));
    }

    [TestCase(TestName = "Свойство VIDEO_framerate содержит переданную частоту")]
    public void VideoFrameratePropertyIsCorrect()
    {
        Span<byte> buffer = stackalloc byte[256];
        _ = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 1920, height: 1080, frameRate: 60, VideoPixelFormat.Yuv420P);

        // Property 5 (VIDEO_framerate): offset = 16 + 4*24 = 112
        var propKey = BinaryPrimitives.ReadUInt32LittleEndian(buffer[112..]);
        var podType = BinaryPrimitives.ReadUInt32LittleEndian(buffer[124..]); // 112+4+4+4=124
        var num = BinaryPrimitives.ReadUInt32LittleEndian(buffer[128..]);
        var denom = BinaryPrimitives.ReadUInt32LittleEndian(buffer[132..]);

        Assert.That(propKey, Is.EqualTo(SPA_FORMAT_VIDEO_framerate));
        Assert.That(podType, Is.EqualTo(SPA_TYPE_Fraction));
        Assert.That(num, Is.EqualTo(60u));
        Assert.That(denom, Is.EqualTo(1u));
    }

    [TestCase(VideoPixelFormat.Yuv420P, 2u, TestName = "Yuv420P маппится в SPA I420 (2)")]
    [TestCase(VideoPixelFormat.Yuv422P, 18u, TestName = "Yuv422P маппится в SPA Y42B (18)")]
    [TestCase(VideoPixelFormat.Yuv444P, 20u, TestName = "Yuv444P маппится в SPA Y444 (20)")]
    [TestCase(VideoPixelFormat.Yuv420P10Le, 43u, TestName = "Yuv420P10Le маппится в SPA I420_10LE (43)")]
    [TestCase(VideoPixelFormat.Yuv422P10Le, 45u, TestName = "Yuv422P10Le маппится в SPA I422_10LE (45)")]
    [TestCase(VideoPixelFormat.Yuv444P10Le, 47u, TestName = "Yuv444P10Le маппится в SPA Y444_10LE (47)")]
    [TestCase(VideoPixelFormat.Rgba32, 11u, TestName = "Rgba32 маппится в SPA RGBA (11)")]
    [TestCase(VideoPixelFormat.Bgra32, 12u, TestName = "Bgra32 маппится в SPA BGRA (12)")]
    [TestCase(VideoPixelFormat.Argb32, 13u, TestName = "Argb32 маппится в SPA ARGB (13)")]
    [TestCase(VideoPixelFormat.Abgr32, 14u, TestName = "Abgr32 маппится в SPA ABGR (14)")]
    [TestCase(VideoPixelFormat.Rgb24, 15u, TestName = "Rgb24 маппится в SPA RGB (15)")]
    [TestCase(VideoPixelFormat.Bgr24, 16u, TestName = "Bgr24 маппится в SPA BGR (16)")]
    [TestCase(VideoPixelFormat.Nv12, 23u, TestName = "Nv12 маппится в SPA NV12 (23)")]
    [TestCase(VideoPixelFormat.Nv21, 24u, TestName = "Nv21 маппится в SPA NV21 (24)")]
    [TestCase(VideoPixelFormat.P010Le, 62u, TestName = "P010Le маппится в SPA P010_10LE (62)")]
    [TestCase(VideoPixelFormat.Yuyv422, 4u, TestName = "Yuyv422 маппится в SPA YUY2 (4)")]
    [TestCase(VideoPixelFormat.Uyvy422, 5u, TestName = "Uyvy422 маппится в SPA UYVY (5)")]
    [TestCase(VideoPixelFormat.Gray8, 25u, TestName = "Gray8 маппится в SPA GRAY8 (25)")]
    [TestCase(VideoPixelFormat.Gray16Le, 27u, TestName = "Gray16Le маппится в SPA GRAY16_LE (27)")]
    public void PixelFormatMappingIsCorrect(VideoPixelFormat format, uint expectedSpaFormat)
    {
        Span<byte> buffer = stackalloc byte[256];
        _ = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 640, height: 480, frameRate: 30, format);

        // Property 3 (VIDEO_format): offset = 16 + 2*24 = 64
        var spaFormat = BinaryPrimitives.ReadUInt32LittleEndian(buffer[80..]); // 64+4+4+4+4=80
        Assert.That(spaFormat, Is.EqualTo(expectedSpaFormat));
    }

    [TestCase(VideoPixelFormat.Unknown, TestName = "Unknown формат выбрасывает VirtualCameraException")]
    [TestCase(VideoPixelFormat.Rgb48, TestName = "Rgb48 формат выбрасывает VirtualCameraException")]
    [TestCase(VideoPixelFormat.Rgba64, TestName = "Rgba64 формат выбрасывает VirtualCameraException")]
    [TestCase(VideoPixelFormat.GrayAlpha16, TestName = "GrayAlpha16 формат выбрасывает VirtualCameraException")]
    public void UnsupportedFormatThrowsException(VideoPixelFormat format)
    {
        var array = new byte[256];

        Assert.Throws<VirtualCameraException>(() =>
            SpaPodBuilder.BuildVideoFormatPod(array.AsSpan(), width: 640, height: 480, frameRate: 30, format));
    }

    // --- Кодеки (compressed formats) ---

    [TestCase(TestName = "Compressed pod имеет размер 112 байт (4 свойства вместо 5)")]
    public void CompressedPodSizeIs112()
    {
        Span<byte> buffer = stackalloc byte[256];
        var size = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 1920, height: 1080, frameRate: 30, VideoPixelFormat.H264);

        // 8 (header) + 8 (type+id) + 4 * 24 (props: mediaType, mediaSubtype, size, framerate) = 112
        Assert.That(size, Is.EqualTo(112));
    }

    [TestCase(VideoPixelFormat.H264, 0x20001u, TestName = "H264 → SPA_MEDIA_SUBTYPE_h264 (0x20001)")]
    [TestCase(VideoPixelFormat.Mjpeg, 0x20002u, TestName = "Mjpeg → SPA_MEDIA_SUBTYPE_mjpg (0x20002)")]
    [TestCase(VideoPixelFormat.Vp8, 0x2000bu, TestName = "Vp8 → SPA_MEDIA_SUBTYPE_vp8 (0x2000b)")]
    [TestCase(VideoPixelFormat.Vp9, 0x2000cu, TestName = "Vp9 → SPA_MEDIA_SUBTYPE_vp9 (0x2000c)")]
    public void CompressedSubtypeMapping(VideoPixelFormat format, uint expectedSubtype)
    {
        Span<byte> buffer = stackalloc byte[256];
        _ = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 640, height: 480, frameRate: 30, format);

        // Property 2 (mediaSubtype): offset = 16 + 24 = 40, value at 56
        var subtype = BinaryPrimitives.ReadUInt32LittleEndian(buffer[56..]);
        Assert.That(subtype, Is.EqualTo(expectedSubtype));
    }

    [TestCase(TestName = "Compressed pod не содержит VIDEO_format свойство")]
    public void CompressedPodHasNoVideoFormat()
    {
        Span<byte> buffer = stackalloc byte[256];
        var size = SpaPodBuilder.BuildVideoFormatPod(buffer, width: 640, height: 480, frameRate: 30, VideoPixelFormat.Mjpeg);

        // Property 3 в compressed pod — это VIDEO_size (0x20003), а не VIDEO_format (0x20001)
        // offset: 16 + 2*24 = 64 (key)
        var key = BinaryPrimitives.ReadUInt32LittleEndian(buffer[64..]);
        Assert.That(key, Is.EqualTo(SPA_FORMAT_VIDEO_size));
    }

    [TestCase(TestName = "IsCompressed возвращает true для кодеков")]
    public void IsCompressedForCodecs()
    {
        Assert.That(VideoPixelFormat.Mjpeg.IsCompressed(), Is.True);
        Assert.That(VideoPixelFormat.H264.IsCompressed(), Is.True);
        Assert.That(VideoPixelFormat.Vp8.IsCompressed(), Is.True);
        Assert.That(VideoPixelFormat.Vp9.IsCompressed(), Is.True);
    }

    [TestCase(TestName = "IsCompressed возвращает false для raw форматов")]
    public void IsCompressedFalseForRaw()
    {
        Assert.That(VideoPixelFormat.Yuv420P.IsCompressed(), Is.False);
        Assert.That(VideoPixelFormat.Rgb24.IsCompressed(), Is.False);
        Assert.That(VideoPixelFormat.Nv12.IsCompressed(), Is.False);
    }
}
