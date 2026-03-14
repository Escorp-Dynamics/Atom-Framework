using System.Buffers.Binary;

namespace Atom.Media;

/// <summary>
/// Демуксер Matroska/WebM-файлов (EBML-контейнер).
/// </summary>
internal sealed class MatroskaDemuxer : IDemuxer
{
    // EBML Element IDs
    private const uint IdEbml = 0x1A45DFA3;
    private const uint IdSegment = 0x18538067;
    private const uint IdInfo = 0x1549A966;
    private const uint IdTimecodeScale = 0x2AD7B1;
    private const uint IdDuration = 0x4489;
    private const uint IdTracks = 0x1654AE6B;
    private const uint IdTrackEntry = 0xAE;
    private const uint IdTrackNumber = 0xD7;
    private const uint IdTrackType = 0x83;
    private const uint IdCodecId = 0x86;
    private const uint IdAudio = 0xE1;
    private const uint IdSamplingFrequency = 0xB5;
    private const uint IdChannels = 0x9F;
    private const uint IdVideo = 0xE0;
    private const uint IdPixelWidth = 0xB0;
    private const uint IdPixelHeight = 0xBA;
    private const uint IdCluster = 0x1F43B675;
    private const uint IdTimecode = 0xE7;
    private const uint IdSimpleBlock = 0xA3;

    private byte[] data = [];
    private bool isOpen;
    private bool isDisposed;

    private int SegmentDataOffset { get; set; }
    private int FirstClusterOffset { get; set; }
    private int ReadPosition { get; set; }
    private long ClusterTimecode { get; set; }
    private long TimecodeScaleNs { get; set; }
    private List<MediaStreamInfo> StreamList { get; set; } = [];

    /// <inheritdoc/>
    public ContainerInfo ContainerInfo { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<MediaStreamInfo> Streams => StreamList;

    /// <inheritdoc/>
    public int BestVideoStreamIndex { get; private set; } = -1;

    /// <inheritdoc/>
    public int BestAudioStreamIndex { get; private set; } = -1;

    /// <inheritdoc/>
    public ContainerResult Open(string path)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!File.Exists(path))
        {
            return ContainerResult.FileNotFound;
        }

        return ParseMatroska(File.ReadAllBytes(path));
    }

    /// <inheritdoc/>
    public ContainerResult Open(Stream stream)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseMatroska(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <inheritdoc/>
    public ContainerResult ReadPacket(MediaPacketBuffer packet)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isOpen)
        {
            return ContainerResult.NotOpen;
        }

        return ReadNextBlock(packet);
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

        var targetMs = (long)timestamp.TotalMilliseconds;
        SeekToTimestamp(targetMs);
        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        ReadPosition = FirstClusterOffset;
        ClusterTimecode = 0;
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
        StreamList = [];
    }

    private ContainerResult ParseMatroska(ReadOnlySpan<byte> raw)
    {
        data = raw.ToArray();
        var pos = 0;

        if (!ReadEbmlHeader(ref pos))
        {
            return ContainerResult.UnsupportedFormat;
        }

        return ParseSegment(pos);
    }

    private bool ReadEbmlHeader(ref int pos)
    {
        var idLen = ReadElementId(data, pos, out var id);
        if (idLen == 0 || id != IdEbml)
        {
            return false;
        }

        var sizeLen = ReadVIntSize(data, pos + idLen, out var size);
        if (sizeLen == 0)
        {
            return false;
        }

        pos += idLen + sizeLen + (int)size;
        return true;
    }

    private ContainerResult ParseSegment(int pos)
    {
        var idLen = ReadElementId(data, pos, out var id);
        if (idLen == 0 || id != IdSegment)
        {
            return ContainerResult.UnsupportedFormat;
        }

        var sizeLen = ReadVIntSize(data, pos + idLen, out _);
        SegmentDataOffset = pos + idLen + sizeLen;

        TimecodeScaleNs = 1_000_000;
        var durationMs = 0.0;

        var scanPos = SegmentDataOffset;
        var result = ScanSegmentChildren(ref scanPos, ref durationMs);
        if (result != ContainerResult.Success)
        {
            return result;
        }

        BuildContainerInfo(durationMs);
        isOpen = true;
        return ContainerResult.Success;
    }

    private ContainerResult ScanSegmentChildren(ref int scanPos, ref double durationMs)
    {
        while (scanPos + 2 < data.Length)
        {
            var childIdLen = ReadElementId(data, scanPos, out var childId);
            if (childIdLen == 0)
            {
                break;
            }

            var childSizeLen = ReadVIntSize(data, scanPos + childIdLen, out var childSize);
            if (childSizeLen == 0)
            {
                break;
            }

            var childDataOffset = scanPos + childIdLen + childSizeLen;

            if (childId == IdInfo)
            {
                ParseInfo(childDataOffset, (int)childSize, ref durationMs);
            }
            else if (childId == IdTracks)
            {
                ParseTracks(childDataOffset, (int)childSize);
            }
            else if (childId == IdCluster)
            {
                FirstClusterOffset = scanPos;
                ReadPosition = scanPos;
                return ContainerResult.Success;
            }

            scanPos = childDataOffset + (int)childSize;
        }

        return StreamList.Count > 0 ? ContainerResult.Success : ContainerResult.CorruptData;
    }

    private void ParseInfo(int offset, int size, ref double durationMs)
    {
        var end = offset + size;
        var pos = offset;

        while (pos < end)
        {
            var idLen = ReadElementId(data, pos, out var id);
            if (idLen == 0)
            {
                break;
            }

            var sizeLen = ReadVIntSize(data, pos + idLen, out var elemSize);
            if (sizeLen == 0)
            {
                break;
            }

            var dataOffset = pos + idLen + sizeLen;

            if (id == IdTimecodeScale)
            {
                TimecodeScaleNs = ReadUIntValue(dataOffset, (int)elemSize);
            }
            else if (id == IdDuration)
            {
                durationMs = ReadFloatValue(dataOffset, (int)elemSize)
                    * TimecodeScaleNs / 1_000_000.0;
            }

            pos = dataOffset + (int)elemSize;
        }
    }

    private void ParseTracks(int offset, int size)
    {
        var end = offset + size;
        var pos = offset;

        while (pos < end)
        {
            var idLen = ReadElementId(data, pos, out var id);
            if (idLen == 0)
            {
                break;
            }

            var sizeLen = ReadVIntSize(data, pos + idLen, out var elemSize);
            if (sizeLen == 0)
            {
                break;
            }

            var dataOffset = pos + idLen + sizeLen;

            if (id == IdTrackEntry)
            {
                ParseTrackEntry(dataOffset, (int)elemSize);
            }

            pos = dataOffset + (int)elemSize;
        }
    }

    private void ParseTrackEntry(int offset, int size)
    {
        var end = offset + size;
        var pos = offset;

        var trackNumber = 0;
        var trackType = 0;
        var codecIdStr = string.Empty;
        var sampleRate = 0;
        var channels = 0;
        var pixelWidth = 0;
        var pixelHeight = 0;

        while (pos < end)
        {
            var idLen = ReadElementId(data, pos, out var id);
            if (idLen == 0)
            {
                break;
            }

            var sizeLen = ReadVIntSize(data, pos + idLen, out var elemSize);
            if (sizeLen == 0)
            {
                break;
            }

            var dataOffset = pos + idLen + sizeLen;
            ReadTrackField(id, dataOffset, (int)elemSize,
                ref trackNumber, ref trackType, ref codecIdStr,
                ref sampleRate, ref channels,
                ref pixelWidth, ref pixelHeight);

            pos = dataOffset + (int)elemSize;
        }

        AddTrack(trackType, codecIdStr, sampleRate, channels, pixelWidth, pixelHeight);
    }

    private void ReadTrackField(
        uint id, int offset, int size,
        ref int trackNumber, ref int trackType, ref string codecIdStr,
        ref int sampleRate, ref int channels,
        ref int pixelWidth, ref int pixelHeight)
    {
        if (id == IdTrackNumber)
        {
            trackNumber = (int)ReadUIntValue(offset, size);
        }
        else if (id == IdTrackType)
        {
            trackType = (int)ReadUIntValue(offset, size);
        }
        else if (id == IdCodecId)
        {
            codecIdStr = System.Text.Encoding.ASCII.GetString(data, offset, size);
        }
        else if (id == IdAudio)
        {
            ParseAudioElement(offset, size, ref sampleRate, ref channels);
        }
        else if (id == IdVideo)
        {
            ParseVideoElement(offset, size, ref pixelWidth, ref pixelHeight);
        }
    }

    private void ParseAudioElement(int offset, int size,
        ref int sampleRate, ref int channels)
    {
        var end = offset + size;
        var pos = offset;

        while (pos < end)
        {
            var idLen = ReadElementId(data, pos, out var id);
            if (idLen == 0)
            {
                break;
            }

            var sizeLen = ReadVIntSize(data, pos + idLen, out var elemSize);
            if (sizeLen == 0)
            {
                break;
            }

            var dataOffset = pos + idLen + sizeLen;

            if (id == IdSamplingFrequency)
            {
                sampleRate = (int)ReadFloatValue(dataOffset, (int)elemSize);
            }
            else if (id == IdChannels)
            {
                channels = (int)ReadUIntValue(dataOffset, (int)elemSize);
            }

            pos = dataOffset + (int)elemSize;
        }
    }

    private void ParseVideoElement(int offset, int size, ref int pixelWidth, ref int pixelHeight)
    {
        var end = offset + size;
        var pos = offset;

        while (pos < end)
        {
            var idLen = ReadElementId(data, pos, out var id);
            if (idLen == 0)
            {
                break;
            }

            var sizeLen = ReadVIntSize(data, pos + idLen, out var elemSize);
            if (sizeLen == 0)
            {
                break;
            }

            var dataOffset = pos + idLen + sizeLen;

            if (id == IdPixelWidth)
            {
                pixelWidth = (int)ReadUIntValue(dataOffset, (int)elemSize);
            }
            else if (id == IdPixelHeight)
            {
                pixelHeight = (int)ReadUIntValue(dataOffset, (int)elemSize);
            }

            pos = dataOffset + (int)elemSize;
        }
    }

    private void AddTrack(int trackType, string codecIdStr,
        int sampleRate, int channels,
        int pixelWidth, int pixelHeight)
    {
        var codecId = MapCodecId(codecIdStr);
        var index = StreamList.Count;

        if (trackType == 2)
        {
            AddAudioTrack(index, codecId, sampleRate, channels);
        }
        else if (trackType == 1)
        {
            AddVideoTrack(index, codecId, pixelWidth, pixelHeight);
        }
    }

    private void AddAudioTrack(int index, MediaCodecId codecId,
        int sampleRate, int channels)
    {
        var sampleFormat = MapSampleFormat(codecId);

        StreamList.Add(new MediaStreamInfo
        {
            Index = index,
            Type = MediaStreamType.Audio,
            CodecId = codecId,
            AudioParameters = new AudioCodecParameters
            {
                SampleRate = sampleRate > 0 ? sampleRate : 44100,
                ChannelCount = channels > 0 ? channels : 2,
                SampleFormat = sampleFormat,
            },
        });

        if (BestAudioStreamIndex < 0)
        {
            BestAudioStreamIndex = index;
        }
    }

    private void AddVideoTrack(int index, MediaCodecId codecId, int pixelWidth, int pixelHeight)
    {
        StreamList.Add(new MediaStreamInfo
        {
            Index = index,
            Type = MediaStreamType.Video,
            CodecId = codecId,
            VideoParameters = new VideoCodecParameters
            {
                Width = pixelWidth,
                Height = pixelHeight,
                PixelFormat = VideoPixelFormat.Rgb24,
            },
        });

        if (BestVideoStreamIndex < 0)
        {
            BestVideoStreamIndex = index;
        }
    }

    private void BuildContainerInfo(double durationMs)
    {
        var durationUs = (long)(durationMs * 1000);

        ContainerInfo = new ContainerInfo
        {
            FormatName = "matroska",
            DurationUs = durationUs,
            FileSize = data.Length,
            IsSeekable = true,
            IsLiveStream = false,
            StreamCount = StreamList.Count,
        };
    }

    private ContainerResult ReadNextBlock(MediaPacketBuffer packet)
    {
        while (ReadPosition + 4 < data.Length)
        {
            var idLen = ReadElementId(data, ReadPosition, out var id);
            if (idLen == 0)
            {
                return ContainerResult.EndOfFile;
            }

            var sizeLen = ReadVIntSize(data, ReadPosition + idLen, out var size);
            if (sizeLen == 0)
            {
                return ContainerResult.EndOfFile;
            }

            var dataOffset = ReadPosition + idLen + sizeLen;

            if (id == IdCluster)
            {
                ReadPosition = dataOffset;
                continue;
            }

            if (id == IdTimecode)
            {
                ClusterTimecode = ReadUIntValue(dataOffset, (int)size);
                ReadPosition = dataOffset + (int)size;
                continue;
            }

            if (id == IdSimpleBlock)
            {
                return ParseSimpleBlock(packet, dataOffset, (int)size);
            }

            ReadPosition = dataOffset + (int)size;
        }

        return ContainerResult.EndOfFile;
    }

    private ContainerResult ParseSimpleBlock(MediaPacketBuffer packet, int offset, int totalSize)
    {
        var trackLen = ReadVIntSize(data, offset, out var trackNumber);
        if (trackLen == 0 || offset + trackLen + 3 > data.Length)
        {
            return ContainerResult.CorruptData;
        }

        var timeOffset = BinaryPrimitives.ReadInt16BigEndian(
            data.AsSpan(offset + trackLen, 2));

        var headerSize = trackLen + 3;
        var frameDataOffset = offset + headerSize;
        var frameSize = totalSize - headerSize;

        if (frameSize <= 0 || frameDataOffset + frameSize > data.Length)
        {
            ReadPosition = offset + totalSize;
            return ContainerResult.EndOfFile;
        }

        packet.SetData(data.AsSpan(frameDataOffset, frameSize));
        packet.StreamIndex = FindStreamIndex(trackNumber);

        var timestampMs = ClusterTimecode + timeOffset;
        var timestampUs = timestampMs * TimecodeScaleNs / 1000;
        packet.PtsUs = timestampUs;
        packet.DtsUs = timestampUs;
        packet.DurationUs = 0;

        ReadPosition = offset + totalSize;
        return ContainerResult.Success;
    }

    private int FindStreamIndex(long trackNumber)
    {
        for (var i = 0; i < StreamList.Count; i++)
        {
            if (StreamList[i].Index == (int)trackNumber - 1)
            {
                return i;
            }
        }

        return 0;
    }

    private void SeekToTimestamp(long targetMs)
    {
        var ratio = ContainerInfo.DurationUs > 0
            ? (double)targetMs * 1000 / ContainerInfo.DurationUs
            : 0.0;

        var dataSize = data.Length - FirstClusterOffset;
        var byteOffset = FirstClusterOffset + (int)(ratio * dataSize);
        byteOffset = Math.Clamp(byteOffset, FirstClusterOffset, data.Length - 1);

        var pos = FindCluster(byteOffset);
        if (pos >= 0)
        {
            ReadPosition = pos;
            ClusterTimecode = 0;
        }
    }

    private int FindCluster(int startOffset)
    {
        for (var i = startOffset; i + 4 < data.Length; i++)
        {
            if (data[i] == 0x1F && data[i + 1] == 0x43
                && data[i + 2] == 0xB6 && data[i + 3] == 0x75)
            {
                return i;
            }
        }

        return FirstClusterOffset;
    }

    private long ReadUIntValue(int offset, int size)
    {
        var value = 0L;

        for (var i = 0; i < size && offset + i < data.Length; i++)
        {
            value = (value << 8) | data[offset + i];
        }

        return value;
    }

    private double ReadFloatValue(int offset, int size) => size switch
    {
        4 => BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4))),
        8 => BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(offset, 8))),
        _ => 0.0,
    };

    private static int ReadElementId(byte[] data, int pos, out uint id)
    {
        id = 0;
        if (pos >= data.Length)
        {
            return 0;
        }

        var first = data[pos];
        var length = GetVIntLength(first);

        if (length == 0 || pos + length > data.Length)
        {
            return 0;
        }

        id = first;
        for (var i = 1; i < length; i++)
        {
            id = (id << 8) | data[pos + i];
        }

        return length;
    }

    private static int ReadVIntSize(byte[] data, int pos, out long value)
    {
        value = 0;
        if (pos >= data.Length)
        {
            return 0;
        }

        var first = data[pos];
        var length = GetVIntLength(first);

        if (length == 0 || pos + length > data.Length)
        {
            return 0;
        }

        value = first & (0xFF >> length);
        for (var i = 1; i < length; i++)
        {
            value = (value << 8) | data[pos + i];
        }

        return length;
    }

    private static int GetVIntLength(byte first)
    {
        for (var i = 0; i < 8; i++)
        {
            if ((first & (0x80 >> i)) != 0)
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static MediaCodecId MapCodecId(string codecId) => codecId switch
    {
        "A_VORBIS" => MediaCodecId.Vorbis,
        "A_OPUS" => MediaCodecId.Opus,
        "A_AAC" or "A_AAC/MPEG4/LC" or "A_AAC/MPEG4/SBR" => MediaCodecId.Aac,
        "A_FLAC" => MediaCodecId.Flac,
        "A_AC3" => MediaCodecId.Ac3,
        "A_DTS" => MediaCodecId.Dts,
        "A_MPEG/L3" => MediaCodecId.Mp3,
        "A_PCM/INT/LIT" => MediaCodecId.PcmS16Le,
        "A_PCM/INT/BIG" => MediaCodecId.PcmS16Be,
        "A_PCM/FLOAT/IEEE" => MediaCodecId.PcmF32Le,
        "V_MPEG4/ISO/AVC" => MediaCodecId.H264,
        "V_MPEGH/ISO/HEVC" => MediaCodecId.H265,
        "V_VP8" => MediaCodecId.Vp8,
        "V_VP9" => MediaCodecId.Vp9,
        "V_AV1" => MediaCodecId.Av1,
        "V_THEORA" => MediaCodecId.Theora,
        _ => MediaCodecId.Unknown,
    };

    private static AudioSampleFormat MapSampleFormat(MediaCodecId codecId) =>
        codecId switch
        {
            MediaCodecId.PcmS16Le or MediaCodecId.PcmS16Be => AudioSampleFormat.S16,
            MediaCodecId.PcmS32Le or MediaCodecId.PcmS32Be => AudioSampleFormat.S32,
            MediaCodecId.PcmF32Le or MediaCodecId.PcmF32Be => AudioSampleFormat.F32,
            MediaCodecId.PcmF64Le => AudioSampleFormat.F64,
            MediaCodecId.PcmU8 => AudioSampleFormat.U8,
            _ => AudioSampleFormat.F32,
        };
}
