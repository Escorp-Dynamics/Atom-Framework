using System.Buffers.Binary;
using System.Text;

namespace Atom.Media.Tests;

[TestFixture]
public sealed class MatroskaDemuxerTests
{
    [Test]
    public void CreateDemuxerReturnsMatroskaDemuxerForMatroskaFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska");
        Assert.That(demuxer, Is.Not.Null);
    }

    [Test]
    public void CreateDemuxerReturnsMatroskaDemuxerForWebmFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("webm");
        Assert.That(demuxer, Is.Not.Null);
    }

    [Test]
    public void OpenFileNotFoundReturnsFileNotFound()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var result = demuxer.Open("/tmp/nonexistent_video_file_12345.mkv");
        Assert.That(result, Is.EqualTo(ContainerResult.FileNotFound));
    }

    [Test]
    public void OpenCorruptDataReturnsUnsupportedFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
    }

    [Test]
    public void OpenValidMatroskaSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: false);
        using var ms = new MemoryStream(mkv);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void AudioTrackInfoCorrect()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: false);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.BestAudioStreamIndex, Is.Zero);

        var stream = demuxer.Streams[0];
        Assert.That(stream.Type, Is.EqualTo(MediaStreamType.Audio));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Opus));
        Assert.That(stream.AudioParameters, Is.Not.Null);
        Assert.That(stream.AudioParameters!.Value.SampleRate, Is.EqualTo(48000));
        Assert.That(stream.AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void VideoTrackInfoCorrect()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: false, hasVideo: true);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.BestVideoStreamIndex, Is.Zero);

        var stream = demuxer.Streams[0];
        Assert.That(stream.Type, Is.EqualTo(MediaStreamType.Video));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Vp9));
        Assert.That(stream.VideoParameters, Is.Not.Null);
        Assert.That(stream.VideoParameters!.Value.Width, Is.EqualTo(1920));
        Assert.That(stream.VideoParameters!.Value.Height, Is.EqualTo(1080));
    }

    [Test]
    public void MultiTrackContainerDetectsBoth()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: true);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(2));
        Assert.That(demuxer.BestVideoStreamIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(demuxer.BestAudioStreamIndex, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ContainerInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: false);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        Assert.That(demuxer.ContainerInfo.FormatName, Is.EqualTo("matroska"));
        Assert.That(demuxer.ContainerInfo.IsSeekable, Is.True);
    }

    [Test]
    public void ReadPacketReturnsData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: false);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
    }

    [Test]
    public void ReadPacketNotOpenReturnsNotOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public async Task ReadPacketAsyncReturnsData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: false);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = await demuxer.ReadPacketAsync(packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
    }

    [Test]
    public void ResetRestartsReading()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: false);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        while (demuxer.ReadPacket(packet) == ContainerResult.Success) { }

        demuxer.Reset();

        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void SeekSuccess()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("matroska")!;
        var mkv = BuildMatroska(hasAudio: true, hasVideo: false);
        using var ms = new MemoryStream(mkv);
        demuxer.Open(ms);

        var result = demuxer.Seek(TimeSpan.FromMilliseconds(500));
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void GetRegisteredDemuxersContainsMatroskaAndWebm()
    {
        var formats = ContainerFactory.GetRegisteredDemuxers().ToList();
        Assert.That(formats, Does.Contain("matroska"));
        Assert.That(formats, Does.Contain("webm"));
    }

    /// <summary>
    /// Builds a minimal EBML/Matroska file with optional audio and video tracks + one cluster.
    /// </summary>
    private static byte[] BuildMatroska(bool hasAudio, bool hasVideo)
    {
        using var output = new MemoryStream();

        // EBML Header
        WriteEbmlHeader(output);

        // Segment (unknown size = 0x01FFFFFFFFFFFFFF)
        WriteElementId(output, 0x18538067); // Segment
        WriteVIntSize(output, 0x01FFFFFFFFFFFFFF);

        // Info element
        WriteInfoElement(output);

        // Tracks element
        WriteTracksElement(output, hasAudio, hasVideo);

        // Cluster with one block
        WriteCluster(output, hasAudio, hasVideo);

        return output.ToArray();
    }

    private static void WriteEbmlHeader(MemoryStream output)
    {
        using var header = new MemoryStream();

        // EBMLVersion = 1
        WriteElementId(header, 0x4286);
        WriteVIntSize(header, 1);
        header.WriteByte(1);

        // EBMLReadVersion = 1
        WriteElementId(header, 0x42F7);
        WriteVIntSize(header, 1);
        header.WriteByte(1);

        // EBMLMaxIDLength = 4
        WriteElementId(header, 0x42F2);
        WriteVIntSize(header, 1);
        header.WriteByte(4);

        // EBMLMaxSizeLength = 8
        WriteElementId(header, 0x42F3);
        WriteVIntSize(header, 1);
        header.WriteByte(8);

        // DocType = "matroska"
        var docType = Encoding.ASCII.GetBytes("matroska");
        WriteElementId(header, 0x4282);
        WriteVIntSize(header, docType.Length);
        header.Write(docType);

        // DocTypeVersion = 4
        WriteElementId(header, 0x4287);
        WriteVIntSize(header, 1);
        header.WriteByte(4);

        // DocTypeReadVersion = 2
        WriteElementId(header, 0x4285);
        WriteVIntSize(header, 1);
        header.WriteByte(2);

        var headerData = header.ToArray();
        WriteElementId(output, 0x1A45DFA3); // EBML
        WriteVIntSize(output, headerData.Length);
        output.Write(headerData);
    }

    private static void WriteInfoElement(MemoryStream output)
    {
        using var info = new MemoryStream();

        // TimecodeScale = 1000000 (default, 1ms)
        WriteElementId(info, 0x2AD7B1);
        WriteVIntSize(info, 3);
        info.WriteByte(0x0F);
        info.WriteByte(0x42);
        info.WriteByte(0x40);

        // Duration = 5000.0 (5 seconds in timecode scale units)
        WriteElementId(info, 0x4489);
        WriteVIntSize(info, 8);
        Span<byte> durationBuf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(durationBuf, BitConverter.DoubleToInt64Bits(5000.0));
        info.Write(durationBuf);

        var infoData = info.ToArray();
        WriteElementId(output, 0x1549A966); // Info
        WriteVIntSize(output, infoData.Length);
        output.Write(infoData);
    }

    private static void WriteTracksElement(MemoryStream output, bool hasAudio, bool hasVideo)
    {
        using var tracks = new MemoryStream();
        var trackNum = 1;

        if (hasVideo)
        {
            WriteVideoTrackEntry(tracks, trackNum++);
        }

        if (hasAudio)
        {
            WriteAudioTrackEntry(tracks, trackNum);
        }

        var tracksData = tracks.ToArray();
        WriteElementId(output, 0x1654AE6B); // Tracks
        WriteVIntSize(output, tracksData.Length);
        output.Write(tracksData);
    }

    private static void WriteVideoTrackEntry(MemoryStream output, int trackNumber)
    {
        using var entry = new MemoryStream();

        // TrackNumber
        WriteElementId(entry, 0xD7);
        WriteVIntSize(entry, 1);
        entry.WriteByte((byte)trackNumber);

        // TrackType = 1 (video)
        WriteElementId(entry, 0x83);
        WriteVIntSize(entry, 1);
        entry.WriteByte(1);

        // CodecID = "V_VP9"
        var codecId = Encoding.ASCII.GetBytes("V_VP9");
        WriteElementId(entry, 0x86);
        WriteVIntSize(entry, codecId.Length);
        entry.Write(codecId);

        // Video element
        using var video = new MemoryStream();

        // PixelWidth = 1920
        WriteElementId(video, 0xB0);
        WriteVIntSize(video, 2);
        video.WriteByte(0x07);
        video.WriteByte(0x80);

        // PixelHeight = 1080
        WriteElementId(video, 0xBA);
        WriteVIntSize(video, 2);
        video.WriteByte(0x04);
        video.WriteByte(0x38);

        var videoData = video.ToArray();
        WriteElementId(entry, 0xE0); // Video
        WriteVIntSize(entry, videoData.Length);
        entry.Write(videoData);

        var entryData = entry.ToArray();
        WriteElementId(output, 0xAE); // TrackEntry
        WriteVIntSize(output, entryData.Length);
        output.Write(entryData);
    }

    private static void WriteAudioTrackEntry(MemoryStream output, int trackNumber)
    {
        using var entry = new MemoryStream();

        // TrackNumber
        WriteElementId(entry, 0xD7);
        WriteVIntSize(entry, 1);
        entry.WriteByte((byte)trackNumber);

        // TrackType = 2 (audio)
        WriteElementId(entry, 0x83);
        WriteVIntSize(entry, 1);
        entry.WriteByte(2);

        // CodecID = "A_OPUS"
        var codecId = Encoding.ASCII.GetBytes("A_OPUS");
        WriteElementId(entry, 0x86);
        WriteVIntSize(entry, codecId.Length);
        entry.Write(codecId);

        // Audio element
        using var audio = new MemoryStream();

        // SamplingFrequency = 48000.0 (float64)
        WriteElementId(audio, 0xB5);
        WriteVIntSize(audio, 8);
        Span<byte> rateBuf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(rateBuf, BitConverter.DoubleToInt64Bits(48000.0));
        audio.Write(rateBuf);

        // Channels = 2
        WriteElementId(audio, 0x9F);
        WriteVIntSize(audio, 1);
        audio.WriteByte(2);

        var audioData = audio.ToArray();
        WriteElementId(entry, 0xE1); // Audio
        WriteVIntSize(entry, audioData.Length);
        entry.Write(audioData);

        var entryData = entry.ToArray();
        WriteElementId(output, 0xAE); // TrackEntry
        WriteVIntSize(output, entryData.Length);
        output.Write(entryData);
    }

    private static void WriteCluster(MemoryStream output, bool hasAudio, bool hasVideo)
    {
        using var cluster = new MemoryStream();

        // Timecode = 0
        WriteElementId(cluster, 0xE7);
        WriteVIntSize(cluster, 1);
        cluster.WriteByte(0);

        // SimpleBlock for the first available track
        var trackNum = hasVideo ? 1 : (hasAudio ? 1 : 0);
        if (trackNum > 0)
        {
            WriteSimpleBlock(cluster, trackNum);
        }

        var clusterData = cluster.ToArray();
        WriteElementId(output, 0x1F43B675); // Cluster
        WriteVIntSize(output, clusterData.Length);
        output.Write(clusterData);
    }

    private static void WriteSimpleBlock(MemoryStream cluster, int trackNumber)
    {
        // SimpleBlock: trackNumber(VINT) + timecodeOffset(int16) + flags(1) + data
        using var block = new MemoryStream();

        // Track number as VINT (1 byte for small track numbers)
        block.WriteByte((byte)(0x80 | trackNumber));

        // Timecode offset (int16 big-endian) = 0
        block.WriteByte(0);
        block.WriteByte(0);

        // Flags: keyframe
        block.WriteByte(0x80);

        // Frame data (128 bytes of random data)
        var frameData = new byte[128];
        new Random(42).NextBytes(frameData);
        block.Write(frameData);

        var blockData = block.ToArray();
        WriteElementId(cluster, 0xA3); // SimpleBlock
        WriteVIntSize(cluster, blockData.Length);
        cluster.Write(blockData);
    }

    private static void WriteElementId(MemoryStream output, uint id)
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

    private static void WriteVIntSize(MemoryStream output, long size)
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
        else if (size < 0x0FFFFFFF)
        {
            output.WriteByte((byte)(0x10 | (size >> 24)));
            output.WriteByte((byte)(size >> 16));
            output.WriteByte((byte)(size >> 8));
            output.WriteByte((byte)size);
        }
        else
        {
            // Unknown size (8 bytes)
            output.WriteByte(0x01);
            output.WriteByte((byte)(size >> 48));
            output.WriteByte((byte)(size >> 40));
            output.WriteByte((byte)(size >> 32));
            output.WriteByte((byte)(size >> 24));
            output.WriteByte((byte)(size >> 16));
            output.WriteByte((byte)(size >> 8));
            output.WriteByte((byte)size);
        }
    }
}
