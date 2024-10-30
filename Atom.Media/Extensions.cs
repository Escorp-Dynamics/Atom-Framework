namespace Atom.Media;

internal static class Extensions
{
    public static SampleFormat ToSampleFormat(this MediaCodec codec) => codec switch
    {
        MediaCodec.AAC => SampleFormat.FLTP,
        MediaCodec.VORBIS  => SampleFormat.FLTP,
        MediaCodec.MP2 => SampleFormat.S16,
        MediaCodec.MP3 => SampleFormat.S32P,
        MediaCodec.WMAV2 => SampleFormat.S16,
        _ => throw new NotSupportedException("Неподдерживаемый формат аудиокодека"),
    };
}