using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FormatContext
{
    public readonly void* av_class;
    public readonly void* iformat;
    public readonly OutputFormat* oformat;
    public void* priv_data;
    public void* pb;
    public int ctx_flags;
    public uint nb_streams;
    public MediaStream** streams;
    public uint nb_stream_groups;
    public void** stream_groups;
    public uint nb_chapters;
    public void** chapters;
    public char* url;
    public long start_time;
    public long duration;
    public long bit_rate;
    public uint packet_size;
    public int max_delay;
    public int flags;
    public long probeSize;
    public long max_analyze_duration;
    public readonly byte* key;
    public int keyLen;
    public uint nb_programs;
    public void** programs;
    public int video_codec_id;
    public int audio_codec_id;
    public int subtitle_codec_id;
    public int data_codec_id;
    public void* metadata;
    public long start_time_realtime;
    public int fps_probe_size;
    public int error_recognition;
    public fixed byte interrupt_callback[16];
    public int debug;
    public int max_streams;
    public uint max_index_size;
    public uint max_picture_buffer;
}