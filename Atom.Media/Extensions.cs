namespace Atom.Media;

internal static class Extensions
{
    public static int AsCodecID(this MediaFormat mediaFormat) => mediaFormat switch
    {
        MediaFormat.YUYV => 20,
        _ => 0,
    };

    public static int AsPixelFormat(this MediaFormat mediaFormat) => mediaFormat switch
    {
        MediaFormat.YUYV => 0,
        _ => -1,
    };
}