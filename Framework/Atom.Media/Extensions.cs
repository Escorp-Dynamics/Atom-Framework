namespace Atom.Media;

internal static class Extensions
{
    public static SampleFormat ToSampleFormat(this MediaCodec codec) => codec switch
    {
        MediaCodec.AAC => SampleFormat.FLTP,
        MediaCodec.VORBIS => SampleFormat.FLTP,
        MediaCodec.MP2 => SampleFormat.S16,
        MediaCodec.MP3 => SampleFormat.S32P,
        MediaCodec.WMAV2 => SampleFormat.FLTP,
        MediaCodec.OPUS => SampleFormat.S16,
        _ => throw new NotSupportedException("Неподдерживаемый формат аудиокодека"),
    };

    public static PixelFormat ToPixelFormat(this MediaCodec codec) => codec switch
    {
        MediaCodec.MJPEG => PixelFormat.YUVJ420P,
        MediaCodec.WEBP => PixelFormat.BGRA,// PixelFormat.YUVA420P,
        _ => PixelFormat.YUV420P,
    };
}