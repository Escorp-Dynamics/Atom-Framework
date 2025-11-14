using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MediaFrame
{
    public Fixed<byte> data;
    public fixed int linesize[8];
    public byte** extended_data;
    public int width;
    public int height;
    public int nb_samples;
    public int Format;
    [Obsolete] public int key_frame;
    public int pict_type;
    public Ratio sample_aspect_ratio;
    public long pts;
    public long pkt_dts;
    public Ratio time_base;
    public int quality;
    public void* opaque;
    public int repeat_pict;
    [Obsolete] public int interlaced_frame;
    [Obsolete] public int top_field_first;
    [Obsolete] public int palette_has_changed;
    public int sample_rate;
    public Fixed<AVBufferRef> buf;
    public AVBufferRef** extended_buf;
    public int nb_extended_buf;
    public void** side_data;
    public int nb_side_data;
    public int flags;
    public int color_range;
    public int color_primaries;
    public int color_trc;
    public int colorSpace;
    public int chroma_location;
    public long best_effort_timestamp;
    [Obsolete] public long pkt_pos;
    public void* metadata;
    public int decode_error_flags;
    [Obsolete] public int pkt_size;
    public AVBufferRef* hw_frames_ctx;
    public AVBufferRef* opaque_ref;
    public ulong crop_top;
    public ulong crop_bottom;
    public ulong crop_left;
    public ulong crop_right;
    public AVBufferRef* private_ref;
    public ChannelLayout ChannelLayout;
    public long duration;
}

#pragma warning disable CA1823, CS0169
[StructLayout(LayoutKind.Auto)]
internal unsafe struct Fixed<T> where T : unmanaged
{
    private const uint Size = 8;

    private readonly T* data0;

    private readonly T* data1;

    private readonly T* data2;

    private readonly T* data3;

    private readonly T* data4;

    private readonly T* data5;

    private readonly T* data6;

    private readonly T* data7;

    public readonly T** Source
    {
        get
        {
            fixed (T** ptr = &data0) return ptr;
        }
    }

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

    public static unsafe implicit operator T*[](Fixed<T> @struct) => @struct.ToArray();
}