#pragma warning disable CA1028

namespace Atom.Media;

/// <summary>
/// Идентификатор видео/аудио кодека.
/// </summary>
public enum MediaCodecId : ushort
{
    /// <summary>Неизвестный кодек.</summary>
    Unknown = 0,

    // ═══════════════════════════════════════════════════════════════
    // RAW VIDEO (0x0001 - 0x00FF)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Raw RGB24 (8 бит на канал, packed).</summary>
    RawRgb24 = 0x0001,

    /// <summary>Raw RGBA32 (8 бит на канал, packed).</summary>
    RawRgba32 = 0x0002,

    /// <summary>Raw BGR24 (8 бит на канал, packed).</summary>
    RawBgr24 = 0x0003,

    /// <summary>Raw BGRA32 (8 бит на канал, packed).</summary>
    RawBgra32 = 0x0004,

    /// <summary>Raw YUV420P (planar, 8 бит).</summary>
    RawYuv420P = 0x0010,

    /// <summary>Raw YUV422P (planar, 8 бит).</summary>
    RawYuv422P = 0x0011,

    /// <summary>Raw YUV444P (planar, 8 бит).</summary>
    RawYuv444P = 0x0012,

    /// <summary>Raw NV12 (semi-planar, Y + interleaved UV).</summary>
    RawNv12 = 0x0020,

    /// <summary>Raw NV21 (semi-planar, Y + interleaved VU).</summary>
    RawNv21 = 0x0021,

    // ═══════════════════════════════════════════════════════════════
    // IMAGE CODECS (0x0100 - 0x01FF)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>JPEG (Joint Photographic Experts Group).</summary>
    Jpeg = 0x0100,

    /// <summary>Motion JPEG (покадровый JPEG).</summary>
    Mjpeg = 0x0101,

    /// <summary>PNG (Portable Network Graphics).</summary>
    Png = 0x0102,

    /// <summary>BMP (Windows Bitmap).</summary>
    Bmp = 0x0103,

    /// <summary>WebP (Google WebP).</summary>
    WebP = 0x0104,

    /// <summary>GIF (Graphics Interchange Format).</summary>
    Gif = 0x0105,

    // ═══════════════════════════════════════════════════════════════
    // VIDEO CODECS (0x0200 - 0x02FF)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>H.264 / AVC (Advanced Video Coding).</summary>
    H264 = 0x0200,

    /// <summary>H.265 / HEVC (High Efficiency Video Coding).</summary>
    H265 = 0x0201,

    /// <summary>VP8 (WebM).</summary>
    Vp8 = 0x0210,

    /// <summary>VP9 (WebM).</summary>
    Vp9 = 0x0211,

    /// <summary>AV1 (AOMedia Video 1).</summary>
    Av1 = 0x0220,

    /// <summary>MPEG-1 Video.</summary>
    Mpeg1 = 0x0230,

    /// <summary>MPEG-2 Video.</summary>
    Mpeg2 = 0x0231,

    /// <summary>MPEG-4 Part 2 (DivX, Xvid).</summary>
    Mpeg4 = 0x0232,

    /// <summary>Theora (Ogg).</summary>
    Theora = 0x0240,

    // ═══════════════════════════════════════════════════════════════
    // RAW AUDIO (0x0300 - 0x03FF)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>PCM Signed 16-bit Little Endian.</summary>
    PcmS16Le = 0x0300,

    /// <summary>PCM Signed 16-bit Big Endian.</summary>
    PcmS16Be = 0x0301,

    /// <summary>PCM Signed 32-bit Little Endian.</summary>
    PcmS32Le = 0x0302,

    /// <summary>PCM Signed 32-bit Big Endian.</summary>
    PcmS32Be = 0x0303,

    /// <summary>PCM Float 32-bit Little Endian.</summary>
    PcmF32Le = 0x0310,

    /// <summary>PCM Float 32-bit Big Endian.</summary>
    PcmF32Be = 0x0311,

    /// <summary>PCM Float 64-bit Little Endian.</summary>
    PcmF64Le = 0x0312,

    /// <summary>PCM Unsigned 8-bit.</summary>
    PcmU8 = 0x0320,

    /// <summary>PCM A-law.</summary>
    PcmALaw = 0x0330,

    /// <summary>PCM μ-law.</summary>
    PcmMuLaw = 0x0331,

    // ═══════════════════════════════════════════════════════════════
    // AUDIO CODECS (0x0400 - 0x04FF)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>AAC (Advanced Audio Coding).</summary>
    Aac = 0x0400,

    /// <summary>MP3 (MPEG-1 Audio Layer 3).</summary>
    Mp3 = 0x0401,

    /// <summary>Opus (высокое качество, низкая задержка).</summary>
    Opus = 0x0410,

    /// <summary>Vorbis (Ogg).</summary>
    Vorbis = 0x0411,

    /// <summary>FLAC (Free Lossless Audio Codec).</summary>
    Flac = 0x0420,

    /// <summary>ALAC (Apple Lossless Audio Codec).</summary>
    Alac = 0x0421,

    /// <summary>AC-3 (Dolby Digital).</summary>
    Ac3 = 0x0430,

    /// <summary>E-AC-3 (Dolby Digital Plus).</summary>
    Eac3 = 0x0431,

    /// <summary>DTS (Digital Theater Systems).</summary>
    Dts = 0x0432,
}
