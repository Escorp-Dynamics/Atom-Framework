using System.Buffers.Binary;

namespace Atom.Media.Tests;

[TestFixture]
public sealed class OggDemuxerTests
{
    [Test]
    public void CreateDemuxerReturnsOggDemuxerForOggFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg");
        Assert.That(demuxer, Is.Not.Null);
    }

    [Test]
    public void OpenFileNotFoundReturnsFileNotFound()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var result = demuxer.Open("/tmp/nonexistent_audio_file_12345.ogg");
        Assert.That(result, Is.EqualTo(ContainerResult.FileNotFound));
    }

    [Test]
    public void OpenCorruptDataReturnsUnsupportedFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
    }

    [Test]
    public void OpenValidVorbisOggSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildVorbisOgg(sampleRate: 44100, channels: 2, totalGranule: 44100);
        using var ms = new MemoryStream(ogg);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void StreamInfoCorrectAfterVorbisOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildVorbisOgg(sampleRate: 48000, channels: 2, totalGranule: 48000);
        using var ms = new MemoryStream(ogg);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.BestAudioStreamIndex, Is.Zero);
        Assert.That(demuxer.BestVideoStreamIndex, Is.EqualTo(-1));

        var stream = demuxer.Streams[0];
        Assert.That(stream.Type, Is.EqualTo(MediaStreamType.Audio));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Vorbis));
        Assert.That(stream.AudioParameters, Is.Not.Null);
        Assert.That(stream.AudioParameters!.Value.SampleRate, Is.EqualTo(48000));
        Assert.That(stream.AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void ContainerInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildVorbisOgg(sampleRate: 44100, channels: 2, totalGranule: 88200);
        using var ms = new MemoryStream(ogg);
        demuxer.Open(ms);

        Assert.That(demuxer.ContainerInfo.FormatName, Is.EqualTo("ogg"));
        Assert.That(demuxer.ContainerInfo.IsSeekable, Is.True);
        Assert.That(demuxer.ContainerInfo.StreamCount, Is.EqualTo(1));
        Assert.That(demuxer.ContainerInfo.DurationUs, Is.GreaterThan(0));
    }

    [Test]
    public void ReadPacketReturnsData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildVorbisOgg(sampleRate: 44100, channels: 2, totalGranule: 44100);
        using var ms = new MemoryStream(ogg);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
        Assert.That(packet.StreamIndex, Is.Zero);
    }

    [Test]
    public void ReadPacketNotOpenReturnsNotOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public async Task ReadPacketAsyncReturnsData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildVorbisOgg(sampleRate: 44100, channels: 2, totalGranule: 44100);
        using var ms = new MemoryStream(ogg);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = await demuxer.ReadPacketAsync(packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
    }

    [Test]
    public void OpenValidOpusOggSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildOpusOgg(sampleRate: 48000, channels: 2, totalGranule: 48000);
        using var ms = new MemoryStream(ogg);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void OpusStreamInfoCorrect()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildOpusOgg(sampleRate: 48000, channels: 2, totalGranule: 96000);
        using var ms = new MemoryStream(ogg);
        demuxer.Open(ms);

        var stream = demuxer.Streams[0];
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Opus));
        Assert.That(stream.AudioParameters!.Value.SampleRate, Is.EqualTo(48000));
        Assert.That(stream.AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void ResetRestartsReading()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildVorbisOgg(sampleRate: 44100, channels: 2, totalGranule: 44100);
        using var ms = new MemoryStream(ogg);
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
        using var demuxer = ContainerFactory.CreateDemuxer("ogg")!;
        var ogg = BuildVorbisOgg(sampleRate: 44100, channels: 2, totalGranule: 441000);
        using var ms = new MemoryStream(ogg);
        demuxer.Open(ms);

        var result = demuxer.Seek(TimeSpan.FromSeconds(2));
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void GetRegisteredDemuxersContainsOgg()
    {
        var formats = ContainerFactory.GetRegisteredDemuxers();
        Assert.That(formats, Does.Contain("ogg"));
    }

    /// <summary>
    /// Builds a minimal Ogg Vorbis stream with identification + comment + setup + audio pages.
    /// </summary>
    private static byte[] BuildVorbisOgg(int sampleRate, int channels, long totalGranule)
    {
        using var output = new MemoryStream();
        var serial = 0x12345678;

        // Page 0: Vorbis identification header (BOS)
        var identHeader = BuildVorbisIdentHeader(sampleRate, channels);
        WriteOggPage(output, serial, 0, granule: 0, headerType: 0x02, identHeader);

        // Page 1: Vorbis comment header
        var commentHeader = BuildVorbisCommentHeader();
        WriteOggPage(output, serial, 1, granule: 0, headerType: 0x00, commentHeader);

        // Page 2: Vorbis setup header
        var setupHeader = BuildVorbisSetupHeader();
        WriteOggPage(output, serial, 2, granule: 0, headerType: 0x00, setupHeader);

        // Page 3: Audio data
        var audioData = new byte[256];
        new Random(42).NextBytes(audioData);
        WriteOggPage(output, serial, 3, granule: totalGranule, headerType: 0x00, audioData);

        return output.ToArray();
    }

    /// <summary>
    /// Builds a minimal Ogg Opus stream with head + tags + audio pages.
    /// </summary>
    private static byte[] BuildOpusOgg(int sampleRate, int channels, long totalGranule)
    {
        using var output = new MemoryStream();
        var serial = 0x07654321;

        // Page 0: OpusHead (BOS)
        var opusHead = BuildOpusHead(sampleRate, channels);
        WriteOggPage(output, serial, 0, granule: 0, headerType: 0x02, opusHead);

        // Page 1: OpusTags
        var opusTags = BuildOpusTags();
        WriteOggPage(output, serial, 1, granule: 0, headerType: 0x00, opusTags);

        // Page 2: Audio data
        var audioData = new byte[256];
        new Random(42).NextBytes(audioData);
        WriteOggPage(output, serial, 2, granule: totalGranule, headerType: 0x00, audioData);

        return output.ToArray();
    }

    private static byte[] BuildVorbisIdentHeader(int sampleRate, int channels)
    {
        // Vorbis identification header: type(1) + "vorbis"(6) + version(4) + channels(1)
        // + sampleRate(4) + bitrateMax(4) + bitrateNominal(4) + bitrateMin(4) + blocksize(1) + framing(1)
        var header = new byte[30];
        header[0] = 0x01; // Identification header type
        "vorbis"u8.CopyTo(header.AsSpan(1));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(7), 0); // version
        header[11] = (byte)channels;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 320000); // bitrate max
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), 192000); // bitrate nominal
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), 128000); // bitrate min
        header[28] = 0x88; // blocksize 0=256, 1=2048
        header[29] = 0x01; // framing flag
        return header;
    }

    private static byte[] BuildVorbisCommentHeader()
    {
        // Minimal comment header: type(1) + "vorbis"(6) + vendor_length(4) + vendor(0) + comment_count(4) + framing(1)
        var header = new byte[16];
        header[0] = 0x03; // Comment header type
        "vorbis"u8.CopyTo(header.AsSpan(1));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(7), 0); // vendor length
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(11), 0); // comment count
        header[15] = 0x01; // framing flag
        return header;
    }

    private static byte[] BuildVorbisSetupHeader()
    {
        // Minimal setup header (just type marker + "vorbis" + dummy data)
        var header = new byte[32];
        header[0] = 0x05; // Setup header type
        "vorbis"u8.CopyTo(header.AsSpan(1));
        return header;
    }

    private static byte[] BuildOpusHead(int sampleRate, int channels)
    {
        // OpusHead: "OpusHead"(8) + version(1) + channels(1) + preSkip(2) + sampleRate(4) + gain(2) + mappingFamily(1)
        var header = new byte[19];
        "OpusHead"u8.CopyTo(header.AsSpan(0));
        header[8] = 1; // version
        header[9] = (byte)channels;
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(10), 312); // pre-skip
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), sampleRate);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(16), 0); // output gain
        header[18] = 0; // channel mapping family
        return header;
    }

    private static byte[] BuildOpusTags()
    {
        // OpusTags: "OpusTags"(8) + vendor_length(4) + vendor(0) + comment_count(4)
        var header = new byte[16];
        "OpusTags"u8.CopyTo(header.AsSpan(0));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), 0); // vendor length
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), 0); // comment count
        return header;
    }

    private static void WriteOggPage(
        MemoryStream output, int serialNumber, int sequenceNumber,
        long granule, byte headerType, byte[] pageData)
    {
        // OggS capture pattern
        output.Write("OggS"u8);

        // Version
        output.WriteByte(0);

        // Header type
        output.WriteByte(headerType);

        // Granule position
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, granule);
        output.Write(buf);

        // Serial number
        BinaryPrimitives.WriteInt32LittleEndian(buf, serialNumber);
        output.Write(buf[..4]);

        // Page sequence number
        BinaryPrimitives.WriteInt32LittleEndian(buf, sequenceNumber);
        output.Write(buf[..4]);

        // CRC (placeholder)
        BinaryPrimitives.WriteInt32LittleEndian(buf, 0);
        output.Write(buf[..4]);

        // Segment count and table
        var segments = (pageData.Length + 254) / 255;
        var lastSegSize = pageData.Length % 255;

        if (pageData.Length == 0)
        {
            output.WriteByte(1);
            output.WriteByte(0);
        }
        else
        {
            output.WriteByte((byte)segments);
            for (var i = 0; i < segments - 1; i++)
            {
                output.WriteByte(255);
            }

            output.WriteByte((byte)(lastSegSize == 0 ? 255 : lastSegSize));
        }

        // Page data
        output.Write(pageData);
    }
}
