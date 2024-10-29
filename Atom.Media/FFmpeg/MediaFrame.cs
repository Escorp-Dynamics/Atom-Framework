using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct MediaFrame
{
    [FieldOffset(0)] public Fixed<byte> data;    // 64
    [FieldOffset(64)] public fixed int linesize[8];
    [FieldOffset(96)] public byte** extended_data;
    [FieldOffset(104)] public int width;
    [FieldOffset(108)] public int height;
    [FieldOffset(112)] public int nb_samples;
    [FieldOffset(116)] public PixelFormat Format;
    [Obsolete][FieldOffset(120)] public int key_frame;
    [FieldOffset(124)] public int pict_type;
    [FieldOffset(128)] public Ratio sample_aspect_ratio;    // 8
    [FieldOffset(136)] public long pts;
    [FieldOffset(144)] public long pkt_dts;
    [FieldOffset(152)] public Ratio time_base;  // 8
    [FieldOffset(160)] public int quality;
    [FieldOffset(164)] public void* opaque;
    [FieldOffset(172)] public int repeat_pict;
    [Obsolete][FieldOffset(176)] public int interlaced_frame;
    [Obsolete][FieldOffset(180)] public int top_field_first;
    [Obsolete][FieldOffset(184)] public int palette_has_changed;
    [FieldOffset(188)] public int sample_rate;
    [FieldOffset(192)] public Fixed<AVBufferRef> buf;    // 64
    [FieldOffset(256)] public AVBufferRef** extended_buf;
    [FieldOffset(264)] public int nb_extended_buf;
    [FieldOffset(268)] public void** side_data;
    [FieldOffset(276)] public int nb_side_data;
    [FieldOffset(280)] public int flags;
    [FieldOffset(284)] public int color_range;
    [FieldOffset(288)] public int color_primaries;
    [FieldOffset(292)] public int color_trc;
    [FieldOffset(296)] public int colorSpace;
    [FieldOffset(300)] public int chroma_location;
    [FieldOffset(304)] public long best_effort_timestamp;
    [Obsolete][FieldOffset(312)] public long pkt_pos;
    [FieldOffset(320)] public void* metadata;
    [FieldOffset(328)] public int decode_error_flags;
    [Obsolete][FieldOffset(332)] public int pkt_size;
    [FieldOffset(336)] public AVBufferRef* hw_frames_ctx;
    [FieldOffset(344)] public AVBufferRef* opaque_ref;
    [FieldOffset(352)] public ulong crop_top;
    [FieldOffset(360)] public ulong crop_bottom;
    [FieldOffset(368)] public ulong crop_left;
    [FieldOffset(376)] public ulong crop_right;
    [FieldOffset(384)] public AVBufferRef* private_ref;
    [FieldOffset(392)] public ChannelLayout ChannelLayout;  // 24
    [FieldOffset(416)] public long duration;
}

#pragma warning disable CA1823, CS0169
internal unsafe struct Fixed<T> where T : unmanaged
{
    const uint Size = 8;

    private readonly T* data0;

    private readonly T* data1;

    private readonly T* data2;

    private readonly T* data3;

    private readonly T* data4;

    private readonly T* data5;

    private readonly T* data6;

    private readonly T* data7;

    public readonly T* this[uint i]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(i, Size);
            fixed (T** ptr = &data0) return ptr[i];
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(i, Size);
            fixed (T** ptr = &data0) ptr[i] = value;
        }
    }

    public readonly unsafe T*[] ToArray()
    {
        fixed (T** ptr = &data0)
        {
            var array = new T*[Size];
            for (var num = 0u; num < Size; ++num) array[num] = ptr[num];
            return array;
        }
    }

    public readonly unsafe void UpdateFrom(byte*[] array)
    {
        fixed (T** ptr2 = &data0)
        {
            var num = 0u;

            foreach (T* ptr in array)
            {
                ptr2[num++] = ptr;
                if (num >= Size) return;
            }
        }
    }

    public unsafe static implicit operator T*[](Fixed<T> @struct) => @struct.ToArray();
}
#pragma warning restore CA1823, CS0169