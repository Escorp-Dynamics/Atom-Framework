using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ChannelLayout
{
    public int order;
    public int nb_channels;
    public long u;
    public void* opaque;
}