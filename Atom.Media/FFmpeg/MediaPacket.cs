using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal struct MediaPacket
{
    public nint buf;              // AVBufferRef*
    public long pts;                // int64_t
    public long dts;                // int64_t
    public nint data;             // uint8_t*
    public int size;                // int
    public int stream_index;        // int
    public int flags;               // int
    public nint side_data;        // AVPacketSideData*
    public int side_data_elems;     // int
    public long duration;           // int64_t
    public long pos;                // int64_t
    public long convergence_duration; // int64_t
}