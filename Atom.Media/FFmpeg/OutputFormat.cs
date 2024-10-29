using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct OutputFormat
{
    public readonly char* name;
    public readonly char* long_name;
    public readonly char* mime_type;
    public readonly char* extensions;
    public MediaCodec AudioCodec;
    public MediaCodec VideoCodec;
    public MediaCodec SubtitleCodec;
    public int flags;
    public readonly void* codec_tag;
    public readonly void* av_class;
}