using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MediaStream
{
    private readonly void* @class;
    public int index;
    public int id;
    public CodecParameters* codecpar;
    public void* priv_data;
    public Ratio time_base;
    public long start_time;
    public long duration;
    public long nb_frames;
    public int disposition;
}