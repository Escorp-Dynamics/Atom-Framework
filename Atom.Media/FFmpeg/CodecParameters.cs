using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal struct CodecParameters
{
    public int codec_type;
        public int codec_id;
        public int codec_tag;
        public nint extradata;
        public int extradata_size;
        public long format;
        public long bit_rate;
        public int bits_per_coded_sample;
        public int bits_per_raw_sample;
        public int profile;
        public int level;
        public int width;
        public int height;
        public Ratio sample_aspect_ratio;
        public int field_order;
        public int color_range;
        public int color_primaries;
        public int color_trc;
        public int color_space;
        public int chroma_location;
        public long video_delay;
        public int channel_layout;
        public int channels;
        public int sample_rate;
        public int block_align;
        public int frame_size;
        public int initial_padding;
        public int trailing_padding;
        public int seek_preroll;
}