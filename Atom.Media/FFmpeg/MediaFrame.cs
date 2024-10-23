using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MediaFrame
{
    public nint** data;
    public int* linesize;
    public nint extended_data;
    public int width;
    public int height;
    public int nb_samples;
    public int format;
    public int key_frame;
    public int pict_type;
    public nint pts;
    public nint pkt_dts;
    public nint pkt_duration;
    public nint pkt_pos;
    public nint pkt_size;
    public int sample_aspect_ratio;
    public nint quality;
    public nint opaque;
    public nint error;
    public int repeat_pict;
    public int interlaced_frame;
    public int top_field_first;
    public int palette_has_changed;
    public int reordered_opaque;
    public int sample_rate;
    public nint channel_layout;
    public nint buf;
    public nint extended_buf;
    public int nb_extended_buf;
    public nint side_data;
    public int nb_side_data;
    public int flags;
    public int color_range;
    public int color_primaries;
    public int color_trc;
    public int colorspace;
    public int chroma_location;
    public long best_effort_timestamp;
    public long pkt_duration_time;
    public long pkt_pos_time;
    public long pkt_time;
    public int coded_picture_number;
    public int display_picture_number;
}