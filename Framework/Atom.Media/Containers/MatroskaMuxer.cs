using System.Buffers.Binary;
using System.Text;

namespace Atom.Media;

/// <summary>
/// Муксер Matroska/WebM контейнера (.mkv, .webm).
/// </summary>
internal sealed class MatroskaMuxer : IMuxer
{
    private const uint IdEbml = 0x1A45DFA3;
    private const uint IdSegment = 0x18538067;
    private const uint IdInfo = 0x1549A966;
    private const uint IdTimecodeScale = 0x2AD7B1;
    private const uint IdTracks = 0x1654AE6B;
    private const uint IdTrackEntry = 0xAE;
    private const uint IdTrackNumber = 0xD7;
    private const uint IdTrackType = 0x83;
    private const uint IdCodecIdElement = 0x86;
    private const uint IdAudio = 0xE1;
    private const uint IdSamplingFrequency = 0xB5;
    private const uint IdChannels = 0x9F;
    private const uint IdVideo = 0xE0;
    private const uint IdPixelWidth = 0xB0;
    private const uint IdPixelHeight = 0xBA;
    private const uint IdCluster = 0x1F43B675;
    private const uint IdTimecode = 0xE7;
    private const uint IdSimpleBlock = 0xA3;

    private Stream? outputStream;
    private bool ownsStream;
    private bool isDisposed;
    private bool headerWritten;
    private string formatName = "matroska";
    private long clusterTimecodeMs;

    private readonly List<TrackInfo> tracks = [];

    /// <inheritdoc/>
    public bool IsOpen { get; private set; }

    /// <inheritdoc/>
    public int StreamCount => tracks.Count;

    /// <inheritdoc/>
    public ContainerResult Open(in MuxerParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        formatName = parameters.FormatName;

        if (parameters.OutputStream is not null)
        {
            outputStream = parameters.OutputStream;
            ownsStream = false;
        }
        else if (parameters.OutputPath is not null)
        {
            outputStream = File.Create(parameters.OutputPath);
            ownsStream = true;
        }
        else
        {
            return ContainerResult.Error;
        }

        IsOpen = true;
        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public (ContainerResult Result, int StreamIndex) AddVideoStream(
        in VideoCodecParameters parameters, MediaCodecId codecId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!IsOpen)
        {
            return (ContainerResult.Error, -1);
        }

        var index = tracks.Count;
        tracks.Add(new TrackInfo
        {
            TrackNumber = index + 1,
            Type = 1, // video
            CodecId = MapCodecIdToString(codecId),
            VideoParams = parameters,
        });

        return (ContainerResult.Success, index);
    }

    /// <inheritdoc/>
    public (ContainerResult Result, int StreamIndex) AddAudioStream(
        in AudioCodecParameters parameters, MediaCodecId codecId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!IsOpen)
        {
            return (ContainerResult.Error, -1);
        }

        var index = tracks.Count;
        tracks.Add(new TrackInfo
        {
            TrackNumber = index + 1,
            Type = 2, // audio
            CodecId = MapCodecIdToString(codecId),
            AudioParams = parameters,
        });

        return (ContainerResult.Success, index);
    }

    /// <inheritdoc/>
    public ContainerResult WriteHeader()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!IsOpen || tracks.Count == 0 || outputStream is null)
        {
            return ContainerResult.Error;
        }

        WriteEbmlHeader();
        WriteSegmentStart();
        WriteInfoElement();
        WriteTracksElement();

        headerWritten = true;
        clusterTimecodeMs = 0;
        StartNewCluster(0);

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public ContainerResult WritePacket(in MediaPacket packet)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!headerWritten || outputStream is null)
        {
            return ContainerResult.Error;
        }

        var timecodeMs = packet.PtsUs / 1000;

        // Start new cluster every 5 seconds
        if (timecodeMs - clusterTimecodeMs > 5000)
        {
            StartNewCluster(timecodeMs);
        }

        WriteSimpleBlock(packet, timecodeMs);

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public async ValueTask<ContainerResult> WritePacketAsync(
        MediaPacketBuffer packet,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!headerWritten || outputStream is null)
        {
            return ContainerResult.Error;
        }

        var result = WritePacket(packet.AsPacket());
        await Task.CompletedTask.ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>
    public ContainerResult WriteTrailer()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!headerWritten || outputStream is null)
        {
            return ContainerResult.Error;
        }

        outputStream.Flush();
        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (ownsStream)
        {
            outputStream?.Dispose();
        }

        outputStream = null;
        IsOpen = false;
    }

    private void WriteEbmlHeader()
    {
        using var header = new MemoryStream();
        WriteUIntElement(header, 0x4286, 1); // EBMLVersion
        WriteUIntElement(header, 0x42F7, 1); // EBMLReadVersion
        WriteUIntElement(header, 0x42F2, 4); // EBMLMaxIDLength
        WriteUIntElement(header, 0x42F3, 8); // EBMLMaxSizeLength

        var docType = string.Equals(formatName, "webm", StringComparison.Ordinal) ? "webm"u8 : "matroska"u8;
        WriteStringElement(header, 0x4282, docType);
        WriteUIntElement(header, 0x4287, 4); // DocTypeVersion
        WriteUIntElement(header, 0x4285, 2); // DocTypeReadVersion

        var headerData = header.ToArray();
        WriteElementId(outputStream!, IdEbml);
        WriteVIntSize(outputStream!, headerData.Length);
        outputStream!.Write(headerData);
    }

    private void WriteSegmentStart()
    {
        WriteElementId(outputStream!, IdSegment);
        // Unknown size (streaming mode)
        outputStream!.WriteByte(0x01);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
    }

    private void WriteInfoElement()
    {
        using var info = new MemoryStream();
        WriteUIntElement(info, IdTimecodeScale, 1_000_000); // 1ms

        var infoData = info.ToArray();
        WriteElementId(outputStream!, IdInfo);
        WriteVIntSize(outputStream!, infoData.Length);
        outputStream!.Write(infoData);
    }

    private void WriteTracksElement()
    {
        using var tracksStream = new MemoryStream();

        foreach (var track in tracks)
        {
            WriteTrackEntry(tracksStream, track);
        }

        var tracksData = tracksStream.ToArray();
        WriteElementId(outputStream!, IdTracks);
        WriteVIntSize(outputStream!, tracksData.Length);
        outputStream!.Write(tracksData);
    }

    private static void WriteTrackEntry(MemoryStream output, TrackInfo track)
    {
        using var entry = new MemoryStream();
        WriteUIntElement(entry, IdTrackNumber, (ulong)track.TrackNumber);
        WriteUIntElement(entry, IdTrackType, (ulong)track.Type);
        WriteStringElement(entry, IdCodecIdElement, Encoding.ASCII.GetBytes(track.CodecId));

        if (track.Type == 1 && track.VideoParams is { } vp)
        {
            WriteVideoElement(entry, vp);
        }
        else if (track.Type == 2 && track.AudioParams is { } ap)
        {
            WriteAudioElement(entry, ap);
        }

        var entryData = entry.ToArray();
        WriteElementId(output, IdTrackEntry);
        WriteVIntSize(output, entryData.Length);
        output.Write(entryData);
    }

    private static void WriteVideoElement(MemoryStream output, VideoCodecParameters vp)
    {
        using var video = new MemoryStream();
        WriteUIntElement(video, IdPixelWidth, (ulong)vp.Width);
        WriteUIntElement(video, IdPixelHeight, (ulong)vp.Height);

        var videoData = video.ToArray();
        WriteElementId(output, IdVideo);
        WriteVIntSize(output, videoData.Length);
        output.Write(videoData);
    }

    private static void WriteAudioElement(MemoryStream output, AudioCodecParameters ap)
    {
        using var audio = new MemoryStream();
        WriteFloatElement(audio, IdSamplingFrequency, ap.SampleRate);
        WriteUIntElement(audio, IdChannels, (ulong)ap.ChannelCount);

        var audioData = audio.ToArray();
        WriteElementId(output, IdAudio);
        WriteVIntSize(output, audioData.Length);
        output.Write(audioData);
    }

    private void StartNewCluster(long timecodeMs)
    {
        clusterTimecodeMs = timecodeMs;

        WriteElementId(outputStream!, IdCluster);
        // Unknown size for streaming
        outputStream!.WriteByte(0x01);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);
        outputStream.WriteByte(0xFF);

        WriteUIntElement(outputStream, IdTimecode, (ulong)timecodeMs);
    }

    private void WriteSimpleBlock(in MediaPacket packet, long timecodeMs)
    {
        var trackIndex = packet.StreamIndex;
        if (trackIndex < 0 || trackIndex >= tracks.Count)
        {
            return;
        }

        var trackNumber = tracks[trackIndex].TrackNumber;
        var timeOffset = (short)(timecodeMs - clusterTimecodeMs);

        // SimpleBlock: trackNumber(VINT) + timeOffset(int16) + flags(1) + data
        var blockSize = 1 + 2 + 1 + packet.Data.Length; // 1-byte VINT for small track nums

        WriteElementId(outputStream!, IdSimpleBlock);
        WriteVIntSize(outputStream!, blockSize);

        outputStream!.WriteByte((byte)(0x80 | trackNumber));

        Span<byte> timeBuf = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(timeBuf, timeOffset);
        outputStream.Write(timeBuf);

        var flags = packet.IsKeyframe ? (byte)0x80 : (byte)0;
        outputStream.WriteByte(flags);

        outputStream.Write(packet.Data);
    }

    private static void WriteElementId(Stream output, uint id)
    {
        if (id >= 0x10000000)
        {
            output.WriteByte((byte)(id >> 24));
            output.WriteByte((byte)(id >> 16));
            output.WriteByte((byte)(id >> 8));
            output.WriteByte((byte)id);
        }
        else if (id >= 0x200000)
        {
            output.WriteByte((byte)(id >> 16));
            output.WriteByte((byte)(id >> 8));
            output.WriteByte((byte)id);
        }
        else if (id >= 0x4000)
        {
            output.WriteByte((byte)(id >> 8));
            output.WriteByte((byte)id);
        }
        else
        {
            output.WriteByte((byte)id);
        }
    }

    private static void WriteVIntSize(Stream output, long size)
    {
        if (size < 0x7F)
        {
            output.WriteByte((byte)(0x80 | size));
        }
        else if (size < 0x3FFF)
        {
            output.WriteByte((byte)(0x40 | (size >> 8)));
            output.WriteByte((byte)size);
        }
        else if (size < 0x1FFFFF)
        {
            output.WriteByte((byte)(0x20 | (size >> 16)));
            output.WriteByte((byte)(size >> 8));
            output.WriteByte((byte)size);
        }
        else
        {
            output.WriteByte((byte)(0x10 | (size >> 24)));
            output.WriteByte((byte)(size >> 16));
            output.WriteByte((byte)(size >> 8));
            output.WriteByte((byte)size);
        }
    }

    private static void WriteUIntElement(Stream output, uint id, ulong value)
    {
        WriteElementId(output, id);
        var size = GetUIntSize(value);
        WriteVIntSize(output, size);

        for (var i = size - 1; i >= 0; i--)
        {
            output.WriteByte((byte)(value >> (i * 8)));
        }
    }

    private static void WriteStringElement(Stream output, uint id, ReadOnlySpan<byte> value)
    {
        WriteElementId(output, id);
        WriteVIntSize(output, value.Length);
        output.Write(value);
    }

    private static void WriteFloatElement(Stream output, uint id, double value)
    {
        WriteElementId(output, id);
        WriteVIntSize(output, 8);
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buf, BitConverter.DoubleToInt64Bits(value));
        output.Write(buf);
    }

    private static int GetUIntSize(ulong value)
    {
        if (value <= 0xFF) return 1;
        if (value <= 0xFFFF) return 2;
        if (value <= 0xFFFFFF) return 3;
        if (value <= 0xFFFFFFFF) return 4;
        return 8;
    }

    private static string MapCodecIdToString(MediaCodecId id) => id switch
    {
        MediaCodecId.Vp8 => "V_VP8",
        MediaCodecId.Vp9 => "V_VP9",
        MediaCodecId.Av1 => "V_AV1",
        MediaCodecId.H264 => "V_MPEG4/ISO/AVC",
        MediaCodecId.H265 => "V_MPEGH/ISO/HEVC",
        MediaCodecId.Opus => "A_OPUS",
        MediaCodecId.Vorbis => "A_VORBIS",
        MediaCodecId.Aac => "A_AAC",
        MediaCodecId.Flac => "A_FLAC",
        MediaCodecId.Mp3 => "A_MPEG/L3",
        _ => "A_PCM/INT/LIT",
    };

    private sealed class TrackInfo
    {
        public required int TrackNumber { get; init; }
        public required int Type { get; init; }
        public required string CodecId { get; init; }
        public VideoCodecParameters? VideoParams { get; init; }
        public AudioCodecParameters? AudioParams { get; init; }
    }
}
