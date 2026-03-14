namespace Atom.Media;

/// <summary>
/// Демуксер AAC-потоков в формате ADTS (Audio Data Transport Stream).
/// Поддерживает MPEG-4 AAC-LC, HE-AAC и HE-AACv2.
/// </summary>
internal sealed class AacDemuxer : IDemuxer
{
    private static readonly int[] SampleRates =
    [
        96000, 88200, 64000, 48000, 44100, 32000,
        24000, 22050, 16000, 12000, 11025, 8000, 7350,
    ];

    private byte[] fileData = [];
    private bool isOpen;
    private bool isDisposed;

    private int FirstFrameOffset { get; set; }
    private int ReadPosition { get; set; }
    private long CurrentSample { get; set; }
    private int SampleRate { get; set; }
    private int ChannelCount { get; set; }
    private int SamplesPerFrame { get; set; }
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

        return ParseAdts(File.ReadAllBytes(path));
    }

    /// <inheritdoc/>
    public ContainerResult Open(Stream stream)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseAdts(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <inheritdoc/>
    public ContainerResult ReadPacket(MediaPacketBuffer packet)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isOpen)
        {
            return ContainerResult.NotOpen;
        }

        if (ReadPosition >= fileData.Length)
        {
            return ContainerResult.EndOfFile;
        }

        var syncPos = FindSyncWord(ReadPosition);
        if (syncPos < 0)
        {
            return ContainerResult.EndOfFile;
        }

        ReadPosition = syncPos;

        if (!TryParseAdtsHeader(ReadPosition, out var frameLength))
        {
            return ContainerResult.CorruptData;
        }

        if (ReadPosition + frameLength > fileData.Length)
        {
            return ContainerResult.EndOfFile;
        }

        packet.SetData(fileData.AsSpan(ReadPosition, frameLength));
        packet.StreamIndex = 0;

        var ptsUs = SampleRate > 0
            ? CurrentSample * 1_000_000 / SampleRate
            : 0L;
        var durationUs = SampleRate > 0
            ? (long)SamplesPerFrame * 1_000_000 / SampleRate
            : 0L;

        packet.PtsUs = ptsUs;
        packet.DtsUs = ptsUs;
        packet.DurationUs = durationUs;

        CurrentSample += SamplesPerFrame;
        ReadPosition += frameLength;

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

        var targetSample = (long)(timestamp.TotalSeconds * SampleRate);
        targetSample = Math.Max(targetSample, 0);

        var dataLen = fileData.Length - FirstFrameOffset;
        var totalSamples = ContainerInfo.DurationUs > 0
            ? ContainerInfo.DurationUs * SampleRate / 1_000_000
            : 1L;
        var ratio = totalSamples > 0
            ? (double)targetSample / totalSamples
            : 0;
        var approxOffset = FirstFrameOffset + (int)(ratio * dataLen);
        approxOffset = Math.Clamp(approxOffset, FirstFrameOffset, fileData.Length - 2);

        var syncPos = FindSyncWord(approxOffset);
        if (syncPos < 0)
        {
            syncPos = FirstFrameOffset;
        }

        ReadPosition = syncPos;
        CurrentSample = targetSample;

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
        fileData = [];
        isOpen = false;
    }

    private ContainerResult ParseAdts(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
        {
            return ContainerResult.CorruptData;
        }

        var offset = SkipId3v2(data);
        var syncPos = FindSyncWordSpan(data, offset);

        if (syncPos < 0)
        {
            return ContainerResult.UnsupportedFormat;
        }

        if (!TryParseFirstHeader(data, syncPos))
        {
            return ContainerResult.UnsupportedFormat;
        }

        fileData = data.ToArray();
        FirstFrameOffset = syncPos;
        ReadPosition = syncPos;
        CurrentSample = 0;

        var totalFrames = CountFrames(syncPos);
        var totalSamples = (long)totalFrames * SamplesPerFrame;
        var durationUs = SampleRate > 0
            ? totalSamples * 1_000_000 / SampleRate
            : 0L;

        StreamInfo = new MediaStreamInfo
        {
            Index = 0,
            Type = MediaStreamType.Audio,
            CodecId = MediaCodecId.Aac,
            DurationUs = durationUs,
            BitRate = durationUs > 0
                ? (fileData.Length - FirstFrameOffset) * 8L * 1_000_000 / durationUs
                : 0,
            AudioParameters = new AudioCodecParameters
            {
                SampleRate = SampleRate,
                ChannelCount = ChannelCount,
                SampleFormat = AudioSampleFormat.F32,
            },
        };

        ContainerInfo = new ContainerInfo
        {
            FormatName = "aac",
            DurationUs = durationUs,
            BitRate = StreamInfo.BitRate,
            FileSize = fileData.Length,
            IsSeekable = true,
            StreamCount = 1,
        };

        isOpen = true;
        return ContainerResult.Success;
    }

    private bool TryParseFirstHeader(ReadOnlySpan<byte> data, int pos)
    {
        if (pos + 7 > data.Length)
        {
            return false;
        }

        if (data[pos] != 0xFF || (data[pos + 1] & 0xF0) != 0xF0)
        {
            return false;
        }

        // MPEG-2 or MPEG-4
        var sampleRateIndex = (data[pos + 2] >> 2) & 0x0F;
        if (sampleRateIndex >= SampleRates.Length)
        {
            return false;
        }

        SampleRate = SampleRates[sampleRateIndex];

        var channelConfig = ((data[pos + 2] & 0x01) << 2) | ((data[pos + 3] >> 6) & 0x03);
        ChannelCount = channelConfig switch
        {
            7 => 8,
            _ => channelConfig,
        };

        if (ChannelCount == 0)
        {
            return false;
        }

        SamplesPerFrame = 1024;
        return true;
    }

    private bool TryParseAdtsHeader(int pos, out int frameLength)
    {
        frameLength = 0;

        if (pos + 7 > fileData.Length)
        {
            return false;
        }

        if (fileData[pos] != 0xFF || (fileData[pos + 1] & 0xF0) != 0xF0)
        {
            return false;
        }

        frameLength = ((fileData[pos + 3] & 0x03) << 11)
            | (fileData[pos + 4] << 3)
            | ((fileData[pos + 5] >> 5) & 0x07);

        return frameLength >= 7;
    }

    private int FindSyncWord(int startPos)
    {
        for (var i = startPos; i < fileData.Length - 1; i++)
        {
            if (fileData[i] == 0xFF && (fileData[i + 1] & 0xF0) == 0xF0 && TryParseAdtsHeader(i, out _))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindSyncWordSpan(ReadOnlySpan<byte> data, int startPos)
    {
        for (var i = startPos; i < data.Length - 1; i++)
        {
            if (data[i] == 0xFF && (data[i + 1] & 0xF0) == 0xF0)
            {
                return i;
            }
        }

        return -1;
    }

    private int CountFrames(int startPos)
    {
        var count = 0;
        var pos = startPos;

        while (pos < fileData.Length - 1)
        {
            if (!TryParseAdtsHeader(pos, out var frameLength))
            {
                break;
            }

            count++;
            pos += frameLength;
        }

        return count;
    }

    private static int SkipId3v2(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10)
        {
            return 0;
        }

        if (data[0] != 'I' || data[1] != 'D' || data[2] != '3')
        {
            return 0;
        }

        var size = (data[6] << 21) | (data[7] << 14) | (data[8] << 7) | data[9];
        return 10 + size;
    }
}
