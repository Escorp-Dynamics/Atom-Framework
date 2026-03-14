using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Демуксер MP3-файлов (MPEG-1/2/2.5 Audio Layer III).
/// </summary>
internal sealed class Mp3Demuxer : IDemuxer
{
    private static readonly int[] Mpeg1Bitrates =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];

    private static readonly int[] Mpeg2Bitrates =
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];

    private static readonly int[] Mpeg1SampleRates = [44100, 48000, 32000];
    private static readonly int[] Mpeg2SampleRates = [22050, 24000, 16000];
    private static readonly int[] Mpeg25SampleRates = [11025, 12000, 8000];

    private byte[] data = [];
    private bool isOpen;
    private bool isDisposed;

    private int FirstFrameOffset { get; set; }
    private int ReadPosition { get; set; }
    private long CurrentSample { get; set; }
    private int SamplesPerFrame { get; set; }
    private long TotalFrames { get; set; }
    private MediaStreamInfo StreamInfo { get; set; }

    /// <inheritdoc/>
    public ContainerInfo ContainerInfo { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<MediaStreamInfo> Streams => isOpen ? [StreamInfo] : [];

    /// <inheritdoc/>
    public int BestVideoStreamIndex => -1;

    /// <inheritdoc/>
    public int BestAudioStreamIndex => isOpen ? 0 : -1;

    /// <inheritdoc/>
    public ContainerResult Open(string path)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!File.Exists(path))
        {
            return ContainerResult.FileNotFound;
        }

        return ParseMp3(File.ReadAllBytes(path));
    }

    /// <inheritdoc/>
    public ContainerResult Open(Stream stream)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseMp3(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <inheritdoc/>
    public ContainerResult ReadPacket(MediaPacketBuffer packet)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isOpen)
        {
            return ContainerResult.NotOpen;
        }

        var syncPos = FindSyncWord(data.AsSpan(), ReadPosition);
        if (syncPos < 0)
        {
            return ContainerResult.EndOfFile;
        }

        var frameSize = CalculateFrameSize(data.AsSpan(), syncPos);
        if (frameSize <= 0 || syncPos + frameSize > data.Length)
        {
            return ContainerResult.EndOfFile;
        }

        packet.SetData(data.AsSpan(syncPos, frameSize));
        packet.StreamIndex = 0;

        var sampleRate = StreamInfo.AudioParameters!.Value.SampleRate;
        packet.PtsUs = (long)(CurrentSample * 1_000_000.0 / sampleRate);
        packet.DtsUs = packet.PtsUs;
        packet.DurationUs = (long)(SamplesPerFrame * 1_000_000.0 / sampleRate);

        ReadPosition = syncPos + frameSize;
        CurrentSample += SamplesPerFrame;

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public ValueTask<ContainerResult> ReadPacketAsync(
        MediaPacketBuffer packet,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable MA0042
        return new ValueTask<ContainerResult>(ReadPacket(packet));
#pragma warning restore MA0042
    }

    /// <inheritdoc/>
    public ContainerResult Seek(TimeSpan timestamp, int streamIndex = -1, bool seekToKeyframe = true)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isOpen)
        {
            return ContainerResult.NotOpen;
        }

        var sampleRate = StreamInfo.AudioParameters!.Value.SampleRate;
        var targetSample = (long)(timestamp.TotalSeconds * sampleRate);
        var targetFrame = targetSample / SamplesPerFrame;

        var dataLength = data.Length - FirstFrameOffset;
        var avgBytesPerFrame = TotalFrames > 0 ? (double)dataLength / TotalFrames : 0;
        var byteOffset = (int)(targetFrame * avgBytesPerFrame) + FirstFrameOffset;

        ReadPosition = Math.Clamp(byteOffset, FirstFrameOffset, data.Length);
        CurrentSample = targetFrame * SamplesPerFrame;

        var syncPos = FindSyncWord(data.AsSpan(), ReadPosition);
        if (syncPos >= 0)
        {
            ReadPosition = syncPos;
        }

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        ReadPosition = FirstFrameOffset;
        CurrentSample = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        data = [];
        isOpen = false;
    }

    private ContainerResult ParseMp3(ReadOnlySpan<byte> raw)
    {
        var offset = SkipId3v2(raw);
        var syncPos = FindSyncWord(raw, offset);

        if (syncPos < 0)
        {
            return ContainerResult.UnsupportedFormat;
        }

        if (!TryParseFrameHeader(raw, syncPos, out var header))
        {
            return ContainerResult.UnsupportedFormat;
        }

        var frameCount = CountFrames(raw, syncPos);
        if (frameCount == 0)
        {
            return ContainerResult.CorruptData;
        }

        return BuildState(raw, syncPos, header, frameCount);
    }

    private ContainerResult BuildState(
        ReadOnlySpan<byte> raw, int offset, FrameHeader header, long frameCount)
    {
        data = raw.ToArray();
        FirstFrameOffset = offset;
        ReadPosition = offset;
        CurrentSample = 0;
        SamplesPerFrame = header.SamplesPerFrame;
        TotalFrames = frameCount;

        var totalSamples = frameCount * header.SamplesPerFrame;
        var durationUs = (long)(totalSamples * 1_000_000.0 / header.SampleRate);
        var bitRate = (long)header.BitrateKbps * 1000;

        var audioParams = new AudioCodecParameters
        {
            SampleRate = header.SampleRate,
            ChannelCount = header.ChannelCount,
            SampleFormat = AudioSampleFormat.F32,
            BitRate = bitRate,
        };

        StreamInfo = new MediaStreamInfo
        {
            Index = 0,
            Type = MediaStreamType.Audio,
            CodecId = MediaCodecId.Mp3,
            DurationUs = durationUs,
            BitRate = bitRate,
            AudioParameters = audioParams,
        };

        ContainerInfo = new ContainerInfo
        {
            FormatName = "mp3",
            DurationUs = durationUs,
            BitRate = bitRate,
            FileSize = data.Length,
            IsSeekable = true,
            IsLiveStream = false,
            StreamCount = 1,
        };

        isOpen = true;
        return ContainerResult.Success;
    }

    private static int SkipId3v2(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
        {
            return 0;
        }

        return 10 + ((data[6] << 21) | (data[7] << 14) | (data[8] << 7) | data[9]);
    }

    private static int FindSyncWord(ReadOnlySpan<byte> data, int start)
    {
        for (var i = start; i + 4 <= data.Length; i++)
        {
            if (data[i] == 0xFF && (data[i + 1] & 0xE0) == 0xE0
                && TryParseFrameHeader(data, i, out _))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryParseFrameHeader(
        ReadOnlySpan<byte> data, int pos, out FrameHeader header)
    {
        header = default;

        if (pos + 4 > data.Length)
        {
            return false;
        }

        var h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
        var mpegVersion = (int)((h >> 19) & 0x03);
        var layer = (int)((h >> 17) & 0x03);
        var bitrateIndex = (int)((h >> 12) & 0x0F);
        var sampleRateIndex = (int)((h >> 10) & 0x03);
        var padding = (int)((h >> 9) & 0x01);
        var channelMode = (int)((h >> 6) & 0x03);

        if (!IsValidHeader(mpegVersion, layer, bitrateIndex, sampleRateIndex))
        {
            return false;
        }

        header = new FrameHeader
        {
            MpegVersion = mpegVersion,
            BitrateKbps = GetBitrate(mpegVersion, bitrateIndex),
            SampleRate = GetSampleRate(mpegVersion, sampleRateIndex),
            ChannelCount = channelMode == 3 ? 1 : 2,
            Padding = padding,
            SamplesPerFrame = mpegVersion == 3 ? 1152 : 576,
        };

        return header.BitrateKbps > 0 && header.SampleRate > 0;
    }

    private static bool IsValidHeader(int mpegVersion, int layer, int bitrateIndex, int sampleRateIndex) =>
        layer == 1 && mpegVersion != 1 && sampleRateIndex != 3 && bitrateIndex is not (0 or 15);

    private static int GetBitrate(int mpegVersion, int index) =>
        mpegVersion == 3 ? Mpeg1Bitrates[index] : Mpeg2Bitrates[index];

    private static int GetSampleRate(int mpegVersion, int index) => mpegVersion switch
    {
        3 => Mpeg1SampleRates[index],
        2 => Mpeg2SampleRates[index],
        0 => Mpeg25SampleRates[index],
        _ => 0,
    };

    private static int CalculateFrameSize(ReadOnlySpan<byte> data, int pos)
    {
        if (!TryParseFrameHeader(data, pos, out var header))
        {
            return 0;
        }

        var multiplier = header.MpegVersion == 3 ? 144 : 72;
        return (multiplier * header.BitrateKbps * 1000 / header.SampleRate) + header.Padding;
    }

    private static long CountFrames(ReadOnlySpan<byte> data, int start)
    {
        var count = 0L;
        var pos = start;

        while (pos + 4 <= data.Length)
        {
            var frameSize = CalculateFrameSize(data, pos);
            if (frameSize <= 0)
            {
                break;
            }

            count++;
            pos += frameSize;
        }

        return count;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct FrameHeader
    {
        public int MpegVersion;
        public int BitrateKbps;
        public int SampleRate;
        public int ChannelCount;
        public int Padding;
        public int SamplesPerFrame;
    }
}
