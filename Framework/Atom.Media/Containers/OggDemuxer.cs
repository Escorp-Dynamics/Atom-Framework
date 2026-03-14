using System.Buffers.Binary;

namespace Atom.Media;

/// <summary>
/// Демуксер OGG-файлов (Ogg Vorbis / Ogg Opus).
/// </summary>
internal sealed class OggDemuxer : IDemuxer
{
    private const int PageHeaderSize = 27;

    private byte[] data = [];
    private bool isOpen;
    private bool isDisposed;

    private int PageOffset { get; set; }
    private int SegmentIndex { get; set; }
    private long PreviousGranule { get; set; }
    private int HeaderPages { get; set; }
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

        return ParseOgg(File.ReadAllBytes(path));
    }

    /// <inheritdoc/>
    public ContainerResult Open(Stream stream)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseOgg(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <inheritdoc/>
    public ContainerResult ReadPacket(MediaPacketBuffer packet)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isOpen)
        {
            return ContainerResult.NotOpen;
        }

        return AssembleNextPacket(packet);
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

        var targetGranule = (long)(timestamp.TotalSeconds * StreamInfo.AudioParameters!.Value.SampleRate);
        SeekToGranule(targetGranule);
        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        SkipHeaderPages();
        PreviousGranule = 0;
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

    private ContainerResult ParseOgg(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < PageHeaderSize || !IsOggPage(raw, 0))
        {
            return ContainerResult.UnsupportedFormat;
        }

        data = raw.ToArray();
        var result = ParseFirstPage();
        if (result != ContainerResult.Success)
        {
            return result;
        }

        var lastGranule = FindLastGranule();
        BuildContainerInfo(lastGranule);

        SkipHeaderPages();
        isOpen = true;
        return ContainerResult.Success;
    }

    private ContainerResult ParseFirstPage()
    {
        var numSegments = data[26];
        var dataOffset = PageHeaderSize + numSegments;
        var firstSegmentSize = GetFirstPacketSize(numSegments);

        if (firstSegmentSize <= 0 || dataOffset + firstSegmentSize > data.Length)
        {
            return ContainerResult.CorruptData;
        }

        var identData = data.AsSpan(dataOffset, firstSegmentSize);
        return ParseIdentificationHeader(identData);
    }

    private ContainerResult ParseIdentificationHeader(ReadOnlySpan<byte> identData)
    {
        if (IsVorbisIdent(identData))
        {
            return ParseVorbisIdent(identData);
        }

        if (IsOpusIdent(identData))
        {
            return ParseOpusIdent(identData);
        }

        return ContainerResult.UnsupportedFormat;
    }

    private ContainerResult ParseVorbisIdent(ReadOnlySpan<byte> ident)
    {
        if (ident.Length < 30)
        {
            return ContainerResult.CorruptData;
        }

        var channels = ident[11];
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(ident.Slice(12, 4));
        var bitrateMax = BinaryPrimitives.ReadInt32LittleEndian(ident.Slice(16, 4));
        var bitrateNominal = BinaryPrimitives.ReadInt32LittleEndian(ident.Slice(20, 4));
        var bitRate = bitrateNominal > 0 ? bitrateNominal : bitrateMax;

        StreamInfo = BuildStreamInfo(MediaCodecId.Vorbis, sampleRate, channels, bitRate);
        HeaderPages = 3;
        return ContainerResult.Success;
    }

    private ContainerResult ParseOpusIdent(ReadOnlySpan<byte> ident)
    {
        if (ident.Length < 19)
        {
            return ContainerResult.CorruptData;
        }

        var channels = ident[9];
        var originalSampleRate = BinaryPrimitives.ReadInt32LittleEndian(ident.Slice(12, 4));
        var sampleRate = originalSampleRate > 0 ? originalSampleRate : 48000;

        StreamInfo = BuildStreamInfo(MediaCodecId.Opus, sampleRate, channels, 0);
        HeaderPages = 2;
        return ContainerResult.Success;
    }

    private static MediaStreamInfo BuildStreamInfo(
        MediaCodecId codecId, int sampleRate, int channels, long bitRate) =>
        new()
        {
            Index = 0,
            Type = MediaStreamType.Audio,
            CodecId = codecId,
            BitRate = bitRate,
            AudioParameters = new AudioCodecParameters
            {
                SampleRate = sampleRate,
                ChannelCount = channels,
                SampleFormat = AudioSampleFormat.F32,
                BitRate = bitRate,
            },
        };

    private void BuildContainerInfo(long lastGranule)
    {
        var sampleRate = StreamInfo.AudioParameters!.Value.SampleRate;
        var durationUs = sampleRate > 0 ? (long)(lastGranule * 1_000_000.0 / sampleRate) : 0;
        var bitRate = StreamInfo.BitRate;

        ContainerInfo = new ContainerInfo
        {
            FormatName = "ogg",
            DurationUs = durationUs,
            BitRate = bitRate,
            FileSize = data.Length,
            IsSeekable = true,
            IsLiveStream = false,
            StreamCount = 1,
        };
    }

    private long FindLastGranule()
    {
        var pos = data.Length - PageHeaderSize;

        while (pos >= 0)
        {
            if (IsOggPage(data.AsSpan(), pos))
            {
                return BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pos + 6, 8));
            }

            pos--;
        }

        return 0;
    }

    private ContainerResult AssembleNextPacket(MediaPacketBuffer packet)
    {
        using var assembler = new MemoryStream();

        while (PageOffset + PageHeaderSize <= data.Length && IsOggPage(data.AsSpan(), PageOffset))
        {
            var result = ReadSegments(assembler);
            if (result == SegmentReadResult.PacketComplete)
            {
                return FinalizePacket(packet, assembler);
            }

            if (result == SegmentReadResult.Error)
            {
                return ContainerResult.EndOfFile;
            }
        }

        return assembler.Length > 0
            ? FinalizePacket(packet, assembler)
            : ContainerResult.EndOfFile;
    }

    private SegmentReadResult ReadSegments(MemoryStream assembler)
    {
        var numSegments = data[PageOffset + 26];
        var headerEnd = PageOffset + PageHeaderSize + numSegments;

        if (headerEnd > data.Length)
        {
            return SegmentReadResult.Error;
        }

        var dataPos = CalculateDataPosition(numSegments);

        while (SegmentIndex < numSegments)
        {
            var segSize = data[PageOffset + PageHeaderSize + SegmentIndex];
            if (dataPos + segSize > data.Length)
            {
                return SegmentReadResult.Error;
            }

            assembler.Write(data, dataPos, segSize);
            dataPos += segSize;
            SegmentIndex++;

            if (segSize < 255)
            {
                UpdateGranuleIfNeeded();
                AdvancePageIfNeeded(numSegments, dataPos);
                return SegmentReadResult.PacketComplete;
            }
        }

        AdvanceToNextPage(dataPos);
        return SegmentReadResult.Continue;
    }

    private int CalculateDataPosition(int numSegments)
    {
        var dataPos = PageOffset + PageHeaderSize + numSegments;

        for (var i = 0; i < SegmentIndex; i++)
        {
            dataPos += data[PageOffset + PageHeaderSize + i];
        }

        return dataPos;
    }

    private void UpdateGranuleIfNeeded()
    {
        var granule = BinaryPrimitives.ReadInt64LittleEndian(
            data.AsSpan(PageOffset + 6, 8));
        if (granule >= 0)
        {
            PreviousGranule = granule;
        }
    }

    private void AdvancePageIfNeeded(int numSegments, int dataPos)
    {
        if (SegmentIndex >= numSegments)
        {
            AdvanceToNextPage(dataPos);
        }
    }

    private void AdvanceToNextPage(int nextPos)
    {
        PageOffset = nextPos;
        SegmentIndex = 0;
    }

    private ContainerResult FinalizePacket(MediaPacketBuffer packet, MemoryStream assembler)
    {
        var packetData = assembler.GetBuffer().AsSpan(0, (int)assembler.Length);
        packet.SetData(packetData);
        packet.StreamIndex = 0;

        var sampleRate = StreamInfo.AudioParameters!.Value.SampleRate;
        packet.PtsUs = sampleRate > 0
            ? (long)(PreviousGranule * 1_000_000.0 / sampleRate)
            : 0;
        packet.DtsUs = packet.PtsUs;
        packet.DurationUs = 0;

        return ContainerResult.Success;
    }

    private void SkipHeaderPages()
    {
        PageOffset = 0;
        SegmentIndex = 0;

        var headersSkipped = 0;

        while (headersSkipped < HeaderPages && PageOffset + PageHeaderSize <= data.Length)
        {
            if (!IsOggPage(data.AsSpan(), PageOffset))
            {
                break;
            }

            var numSegments = data[PageOffset + 26];
            var pageDataSize = SumSegments(numSegments);
            PageOffset += PageHeaderSize + numSegments + pageDataSize;
            headersSkipped++;
        }
    }

    private int SumSegments(int numSegments)
    {
        var total = 0;

        for (var i = 0; i < numSegments; i++)
        {
            total += data[PageOffset + PageHeaderSize + i];
        }

        return total;
    }

    private void SeekToGranule(long targetGranule)
    {
        var ratio = ContainerInfo.DurationUs > 0
            ? targetGranule / (ContainerInfo.DurationUs / 1_000_000.0
                * StreamInfo.AudioParameters!.Value.SampleRate)
            : 0;

        var byteOffset = (int)(ratio * data.Length);
        byteOffset = Math.Clamp(byteOffset, 0, data.Length - 1);

        var pagePos = FindNearestPage(byteOffset);
        if (pagePos >= 0)
        {
            PageOffset = pagePos;
            SegmentIndex = 0;
            PreviousGranule = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pagePos + 6, 8));
        }
    }

    private int FindNearestPage(int startOffset)
    {
        for (var i = startOffset; i + PageHeaderSize <= data.Length; i++)
        {
            if (IsOggPage(data.AsSpan(), i))
            {
                return i;
            }
        }

        return -1;
    }

    private int GetFirstPacketSize(int numSegments)
    {
        var size = 0;

        for (var i = 0; i < numSegments; i++)
        {
            size += data[PageHeaderSize + i];

            if (data[PageHeaderSize + i] < 255)
            {
                break;
            }
        }

        return size;
    }

    private static bool IsOggPage(ReadOnlySpan<byte> data, int pos) =>
        pos + 4 <= data.Length
        && data[pos] == 'O' && data[pos + 1] == 'g'
        && data[pos + 2] == 'g' && data[pos + 3] == 'S';

    private static bool IsVorbisIdent(ReadOnlySpan<byte> data) =>
        data.Length >= 7 && data[0] == 0x01
        && data[1] == 'v' && data[2] == 'o' && data[3] == 'r'
        && data[4] == 'b' && data[5] == 'i' && data[6] == 's';

    private static bool IsOpusIdent(ReadOnlySpan<byte> data) =>
        data.Length >= 8
        && data[0] == 'O' && data[1] == 'p' && data[2] == 'u' && data[3] == 's'
        && data[4] == 'H' && data[5] == 'e' && data[6] == 'a' && data[7] == 'd';

    private enum SegmentReadResult
    {
        Continue,
        PacketComplete,
        Error,
    }
}
