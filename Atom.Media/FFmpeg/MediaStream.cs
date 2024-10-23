using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MediaStream
{
    public int index;
    public long id;
    public CodecParameters* codecpar;
    public Ratio time_base;
}