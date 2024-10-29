using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MediaPacket
{
    public void* buf;
    public long pts;
    public long dts;
    public byte* data;
    public int size;
    public int stream_index;
    public int flags;
    public void* side_data;
    public int side_data_elems;
    public long duration;
    public long pos;
    public long convergence_duration;
}