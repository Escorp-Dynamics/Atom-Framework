using System.Buffers.Binary;

namespace Atom.Media.Video.Backends.PipeWire;

/// <summary>
/// Конструктор SPA Pod структур для описания формата видеопотока PipeWire.
/// Строит бинарное представление напрямую в <see cref="Span{T}"/>.
/// </summary>
internal static class SpaPodBuilder
{
    // SPA типы
    private const uint SPA_TYPE_Int = 4;
    private const uint SPA_TYPE_Id = 3;
    private const uint SPA_TYPE_Rectangle = 10;
    private const uint SPA_TYPE_Fraction = 11;
    private const uint SPA_TYPE_Object = 15;

    // SPA Object
    private const uint SPA_TYPE_OBJECT_Format = 0x40003;
    private const uint SPA_PARAM_EnumFormat = 3;

    // SPA Format ключи
    private const uint SPA_FORMAT_mediaType = 1;
    private const uint SPA_FORMAT_mediaSubtype = 2;
    private const uint SPA_FORMAT_VIDEO_format = 0x20001;
    private const uint SPA_FORMAT_VIDEO_size = 0x20003;
    private const uint SPA_FORMAT_VIDEO_framerate = 0x20004;

    // SPA Media
    private const uint SPA_MEDIA_TYPE_video = 2;
    private const uint SPA_MEDIA_SUBTYPE_raw = 1;
    private const uint SPA_MEDIA_SUBTYPE_h264 = 0x20001;
    private const uint SPA_MEDIA_SUBTYPE_mjpg = 0x20002;
    private const uint SPA_MEDIA_SUBTYPE_vp8 = 0x2000b;
    private const uint SPA_MEDIA_SUBTYPE_vp9 = 0x2000c;

    /// <summary>
    /// Строит SPA Pod для описания формата видеопотока.
    /// </summary>
    /// <param name="buffer">Целевой буфер (рекомендуется &gt;= 256 байт).</param>
    /// <param name="width">Ширина в пикселях.</param>
    /// <param name="height">Высота в пикселях.</param>
    /// <param name="frameRate">Частота кадров.</param>
    /// <param name="pixelFormat">Формат пикселей Atom.</param>
    /// <param name="paramId">SPA param id для создаваемого format pod.</param>
    /// <returns>Размер построенного pod в байтах.</returns>
    internal static int BuildVideoFormatPod(
        Span<byte> buffer, int width, int height, int frameRate, VideoPixelFormat pixelFormat,
        uint paramId = SPA_PARAM_EnumFormat)
    {
        var isCompressed = pixelFormat.IsCompressed();
        var offset = 0;

        // Для raw: 5 свойств (mediaType, mediaSubtype, VIDEO_format, VIDEO_size, VIDEO_framerate)
        // Для compressed: 4 свойства (без VIDEO_format)
        const int propIdSize = 24;
        const int propRectSize = 24;
        const int propFracSize = 24;
        var objectBodySize = isCompressed
            ? 8 + (propIdSize * 2) + propRectSize + propFracSize
            : 8 + (propIdSize * 3) + propRectSize + propFracSize;

        WritePodHeader(buffer, ref offset, objectBodySize, SPA_TYPE_Object);

        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], SPA_TYPE_OBJECT_Format);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], paramId);
        offset += 4;

        // Property: mediaType = video
        WritePropId(buffer, ref offset, SPA_FORMAT_mediaType, SPA_MEDIA_TYPE_video);

        // Property: mediaSubtype
        var subtype = isCompressed
            ? ToSpaMediaSubtype(pixelFormat)
            : SPA_MEDIA_SUBTYPE_raw;
        WritePropId(buffer, ref offset, SPA_FORMAT_mediaSubtype, subtype);

        // Property: VIDEO_format (raw only)
        if (!isCompressed)
        {
            WritePropId(buffer, ref offset, SPA_FORMAT_VIDEO_format, ToSpaVideoFormat(pixelFormat));
        }

        // Property: VIDEO_size
        WritePropRectangle(buffer, ref offset, SPA_FORMAT_VIDEO_size, (uint)width, (uint)height);

        // Property: VIDEO_framerate
        WritePropFraction(buffer, ref offset, SPA_FORMAT_VIDEO_framerate, (uint)frameRate, 1);

        return offset;
    }

    internal static int BuildBuffersParamPod(Span<byte> buffer, int frameSize, int stride, int bufferCount)
    {
        var offset = 0;
        var objectBodySize = 8 + (24 * 5);

        WritePodHeader(buffer, ref offset, objectBodySize, SPA_TYPE_Object);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PipeWireNative.SPA_TYPE_OBJECT_ParamBuffers);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PipeWireNative.SPA_PARAM_Buffers);
        offset += 4;

        WritePropInt(buffer, ref offset, PipeWireNative.SPA_PARAM_BUFFERS_buffers, bufferCount);
        WritePropInt(buffer, ref offset, PipeWireNative.SPA_PARAM_BUFFERS_blocks, 1);
        WritePropInt(buffer, ref offset, PipeWireNative.SPA_PARAM_BUFFERS_size, frameSize);
        WritePropInt(buffer, ref offset, PipeWireNative.SPA_PARAM_BUFFERS_stride, stride);
        WritePropInt(buffer, ref offset, PipeWireNative.SPA_PARAM_BUFFERS_dataType,
            (1 << (int)PipeWireNative.SPA_DATA_MemPtr) | (1 << (int)PipeWireNative.SPA_DATA_MemFd));

        return offset;
    }

    internal static int BuildMetaHeaderParamPod(Span<byte> buffer)
    {
        var offset = 0;
        var objectBodySize = 8 + (24 * 2);

        WritePodHeader(buffer, ref offset, objectBodySize, SPA_TYPE_Object);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PipeWireNative.SPA_TYPE_OBJECT_ParamMeta);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PipeWireNative.SPA_PARAM_Meta);
        offset += 4;

        WritePropId(buffer, ref offset, PipeWireNative.SPA_PARAM_META_type, PipeWireNative.SPA_META_Header);
        WritePropInt(buffer, ref offset, PipeWireNative.SPA_PARAM_META_size, (sizeof(long) * 2) + (sizeof(uint) * 2) + sizeof(ulong));

        return offset;
    }

    internal static int BuildIoBuffersParamPod(Span<byte> buffer)
    {
        var offset = 0;
        var objectBodySize = 8 + (24 * 2);

        WritePodHeader(buffer, ref offset, objectBodySize, SPA_TYPE_Object);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PipeWireNative.SPA_TYPE_OBJECT_ParamIO);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PipeWireNative.SPA_PARAM_IO);
        offset += 4;

        WritePropId(buffer, ref offset, PipeWireNative.SPA_PARAM_IO_id, PipeWireNative.SPA_IO_Buffers);
        WritePropInt(buffer, ref offset, PipeWireNative.SPA_PARAM_IO_size, sizeof(int) + sizeof(uint));

        return offset;
    }

    private static void WritePodHeader(Span<byte> buf, ref int offset, int bodySize, uint type)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], (uint)bodySize);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], type);
        offset += 4;
    }

    private static void WritePropId(Span<byte> buf, ref int offset, uint key, uint value)
    {
        // Key (4) + Flags (4)
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], key);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0); // flags
        offset += 4;

        // Pod header: size=4, type=SPA_TYPE_Id
        WritePodHeader(buf, ref offset, bodySize: 4, SPA_TYPE_Id);

        // Body: uint32 value
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], value);
        offset += 4;

        // Padding to 8-byte alignment (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;
    }

    private static void WritePropInt(Span<byte> buf, ref int offset, uint key, int value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], key);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;

        WritePodHeader(buf, ref offset, bodySize: 4, SPA_TYPE_Int);
        BinaryPrimitives.WriteInt32LittleEndian(buf[offset..], value);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;
    }

    private static void WritePropRectangle(Span<byte> buf, ref int offset, uint key, uint width, uint height)
    {
        // Key (4) + Flags (4)
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], key);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;

        // Pod header: size=8, type=SPA_TYPE_Rectangle
        WritePodHeader(buf, ref offset, bodySize: 8, SPA_TYPE_Rectangle);

        // Body: { width, height }
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], width);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], height);
        offset += 4;
    }

    private static void WritePropFraction(Span<byte> buf, ref int offset, uint key, uint num, uint denom)
    {
        // Key (4) + Flags (4)
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], key);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;

        // Pod header: size=8, type=SPA_TYPE_Fraction
        WritePodHeader(buf, ref offset, bodySize: 8, SPA_TYPE_Fraction);

        // Body: { num, denom }
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], num);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], denom);
        offset += 4;
    }

    private static uint ToSpaVideoFormat(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Yuv420P => 2,       // SPA_VIDEO_FORMAT_I420
        VideoPixelFormat.Yuv422P => 18,      // SPA_VIDEO_FORMAT_Y42B
        VideoPixelFormat.Yuv444P => 20,      // SPA_VIDEO_FORMAT_Y444
        VideoPixelFormat.Yuv420P10Le => 43,  // SPA_VIDEO_FORMAT_I420_10LE
        VideoPixelFormat.Yuv422P10Le => 45,  // SPA_VIDEO_FORMAT_I422_10LE
        VideoPixelFormat.Yuv444P10Le => 47,  // SPA_VIDEO_FORMAT_Y444_10LE
        VideoPixelFormat.Rgba32 => 11,       // SPA_VIDEO_FORMAT_RGBA
        VideoPixelFormat.Bgra32 => 12,       // SPA_VIDEO_FORMAT_BGRA
        VideoPixelFormat.Argb32 => 13,       // SPA_VIDEO_FORMAT_ARGB
        VideoPixelFormat.Abgr32 => 14,       // SPA_VIDEO_FORMAT_ABGR
        VideoPixelFormat.Rgb24 => 15,        // SPA_VIDEO_FORMAT_RGB
        VideoPixelFormat.Bgr24 => 16,        // SPA_VIDEO_FORMAT_BGR
        VideoPixelFormat.Nv12 => 23,         // SPA_VIDEO_FORMAT_NV12
        VideoPixelFormat.Nv21 => 24,         // SPA_VIDEO_FORMAT_NV21
        VideoPixelFormat.P010Le => 62,       // SPA_VIDEO_FORMAT_P010_10LE
        VideoPixelFormat.Yuyv422 => 4,       // SPA_VIDEO_FORMAT_YUY2
        VideoPixelFormat.Uyvy422 => 5,       // SPA_VIDEO_FORMAT_UYVY
        VideoPixelFormat.Gray8 => 25,        // SPA_VIDEO_FORMAT_GRAY8
        VideoPixelFormat.Gray16Le => 27,     // SPA_VIDEO_FORMAT_GRAY16_LE
        _ => throw new VirtualCameraException(
            $"Формат пикселей {format} не поддерживается PipeWire."),
    };

    private static uint ToSpaMediaSubtype(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Mjpeg => SPA_MEDIA_SUBTYPE_mjpg,
        VideoPixelFormat.H264 => SPA_MEDIA_SUBTYPE_h264,
        VideoPixelFormat.Vp8 => SPA_MEDIA_SUBTYPE_vp8,
        VideoPixelFormat.Vp9 => SPA_MEDIA_SUBTYPE_vp9,
        _ => throw new VirtualCameraException(
            $"Формат {format} не является кодеком."),
    };
}
