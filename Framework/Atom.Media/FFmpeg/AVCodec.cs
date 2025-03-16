using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AVCodec
{
    public readonly char* name;
    public readonly char* long_name;
    public int type;
    public MediaCodec Id;
    public int capabilities;
    public byte max_lowres;
}