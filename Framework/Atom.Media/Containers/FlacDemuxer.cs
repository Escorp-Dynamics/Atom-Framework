namespace Atom.Media;

/// <summary>
/// Демуксер FLAC-файлов (Free Lossless Audio Codec).
/// Парсит метаданные STREAMINFO и возвращает FLAC-фреймы как пакеты.
/// </summary>
internal sealed class FlacDemuxer : IDemuxer
{
    private const int StreamInfoMinSize = 34;

    private byte[] fileData = [];
    private bool isOpen;
    private bool isDisposed;

    private int FirstFrameOffset { get; set; }
    private int ReadPosition { get; set; }
    private long CurrentSample { get; set; }
    private int SampleRate { get; set; }
    private int ChannelCount { get; set; }
    private int BitsPerSample { get; set; }
    private long TotalSamples { get; set; }
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

        return ParseFlac(File.ReadAllBytes(path));
    }

    /// <inheritdoc/>
    public ContainerResult Open(Stream stream)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseFlac(ms.GetBuffer().AsSpan(0, (int)ms.Length));
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

        var frameEnd = FindNextFrame(ReadPosition + 2);
        if (frameEnd < 0)
        {
            frameEnd = fileData.Length;
        }

        var frameSize = frameEnd - ReadPosition;
        packet.SetData(fileData.AsSpan(ReadPosition, frameSize));
        packet.StreamIndex = 0;

        var blockSize = ParseBlockSize(ReadPosition);
        var ptsUs = TotalSamples > 0
            ? CurrentSample * 1_000_000 / SampleRate
            : 0L;
        var durationUs = blockSize > 0
            ? blockSize * 1_000_000L / SampleRate
            : 0L;

        packet.PtsUs = ptsUs;
        packet.DtsUs = ptsUs;
        packet.DurationUs = durationUs;

        CurrentSample += blockSize;
        ReadPosition = frameEnd;

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
        targetSample = Math.Clamp(targetSample, 0, TotalSamples);

        var ratio = TotalSamples > 0
            ? (double)targetSample / TotalSamples
            : 0;
        var dataLen = fileData.Length - FirstFrameOffset;
        var approxOffset = FirstFrameOffset + (int)(ratio * dataLen);
        approxOffset = Math.Clamp(approxOffset, FirstFrameOffset, fileData.Length - 2);

        var framePos = FindNextFrame(approxOffset);
        if (framePos < 0)
        {
            framePos = FirstFrameOffset;
        }

        ReadPosition = framePos;
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

    private ContainerResult ParseFlac(ReadOnlySpan<byte> data)
    {
        // Minimum: "fLaC"(4) + metadata block header(4) + STREAMINFO(34) = 42
        if (data.Length < 42)
        {
            return ContainerResult.CorruptData;
        }

        if (data[0] != 'f' || data[1] != 'L' || data[2] != 'a' || data[3] != 'C')
        {
            return ContainerResult.UnsupportedFormat;
        }

        var pos = 4;
        var foundStreamInfo = false;

        while (pos + 4 <= data.Length)
        {
            var blockHeader = data[pos];
            var isLast = (blockHeader & 0x80) != 0;
            var blockType = blockHeader & 0x7F;
            var blockSize = (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;

            if (pos + blockSize > data.Length)
            {
                return ContainerResult.CorruptData;
            }

            if (blockType == 0 && blockSize >= StreamInfoMinSize)
            {
                ParseStreamInfo(data.Slice(pos, blockSize));
                foundStreamInfo = true;
            }

            pos += blockSize;

            if (isLast)
            {
                break;
            }
        }

        if (!foundStreamInfo)
        {
            return ContainerResult.CorruptData;
        }

        return BuildState(data, pos);
    }

    private void ParseStreamInfo(ReadOnlySpan<byte> info)
    {
        // bytes 10-12: sample rate (20 bits), channels-1 (3 bits), bps-1 (5 bits)
        var sr20 = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
        SampleRate = sr20;
        ChannelCount = ((info[12] >> 1) & 0x07) + 1;
        BitsPerSample = (((info[12] & 0x01) << 4) | (info[13] >> 4)) + 1;

        // bytes 13-17: total samples (36 bits)
        TotalSamples = ((long)(info[13] & 0x0F) << 32)
            | ((long)info[14] << 24)
            | ((long)info[15] << 16)
            | ((long)info[16] << 8)
            | info[17];
    }

    private ContainerResult BuildState(ReadOnlySpan<byte> data, int audioStart)
    {
        fileData = data.ToArray();
        FirstFrameOffset = audioStart;
        ReadPosition = audioStart;
        CurrentSample = 0;

        var durationUs = TotalSamples > 0 && SampleRate > 0
            ? TotalSamples * 1_000_000 / SampleRate
            : 0L;

        var sampleFormat = BitsPerSample switch
        {
            8 => AudioSampleFormat.U8,
            16 => AudioSampleFormat.S16,
            24 => AudioSampleFormat.S32,
            32 => AudioSampleFormat.S32,
            _ => AudioSampleFormat.S16,
        };

        StreamInfo = new MediaStreamInfo
        {
            Index = 0,
            Type = MediaStreamType.Audio,
            CodecId = MediaCodecId.Flac,
            DurationUs = durationUs,
            BitRate = durationUs > 0
                ? (fileData.Length - FirstFrameOffset) * 8L * 1_000_000 / durationUs
                : 0,
            AudioParameters = new AudioCodecParameters
            {
                SampleRate = SampleRate,
                ChannelCount = ChannelCount,
                SampleFormat = sampleFormat,
            },
        };

        ContainerInfo = new ContainerInfo
        {
            FormatName = "flac",
            DurationUs = durationUs,
            BitRate = StreamInfo.BitRate,
            FileSize = fileData.Length,
            IsSeekable = true,
            StreamCount = 1,
        };

        isOpen = true;
        return ContainerResult.Success;
    }

    private int FindNextFrame(int startPos)
    {
        for (var i = startPos; i < fileData.Length - 1; i++)
        {
            if (fileData[i] == 0xFF && (fileData[i + 1] & 0xFC) == 0xF8)
            {
                return i;
            }
        }

        return -1;
    }

    private int ParseBlockSize(int framePos)
    {
        if (framePos + 4 >= fileData.Length)
        {
            return 0;
        }

        // FLAC frame header byte 2 has block size code in upper 4 bits
        var blockSizeCode = (fileData[framePos + 2] >> 4) & 0x0F;

        return blockSizeCode switch
        {
            1 => 192,
            2 => 576,
            3 => 1152,
            4 => 2304,
            5 => 4608,
            6 or 7 => 0, // stored at end of header, skip for simplicity
            >= 8 and <= 15 => 256 << (blockSizeCode - 8),
            _ => 0,
        };
    }
}
