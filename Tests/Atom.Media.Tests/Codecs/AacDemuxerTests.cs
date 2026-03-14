namespace Atom.Media.Tests;

[TestFixture]
public sealed class AacDemuxerTests
{
    [Test]
    public void CreateDemuxerReturnsAacDemuxerForAacFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac");
        Assert.That(demuxer, Is.Not.Null);
    }

    [Test]
    public void OpenFileNotFoundReturnsFileNotFound()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var result = demuxer.Open("/tmp/nonexistent_audio_file_12345.aac");
        Assert.That(result, Is.EqualTo(ContainerResult.FileNotFound));
    }

    [Test]
    public void OpenCorruptDataReturnsCorruptData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.CorruptData));
    }

    [Test]
    public void OpenValidAacSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var aac = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 10);
        using var ms = new MemoryStream(aac);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void StreamInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        // sampleRateIndex=3 → 48000, channelConfig=2 → stereo
        var aac = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 5);
        using var ms = new MemoryStream(aac);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.BestAudioStreamIndex, Is.Zero);
        Assert.That(demuxer.BestVideoStreamIndex, Is.EqualTo(-1));

        var stream = demuxer.Streams[0];
        Assert.That(stream.Type, Is.EqualTo(MediaStreamType.Audio));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Aac));
        Assert.That(stream.AudioParameters, Is.Not.Null);
        Assert.That(stream.AudioParameters!.Value.SampleRate, Is.EqualTo(48000));
        Assert.That(stream.AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
        Assert.That(stream.AudioParameters!.Value.SampleFormat, Is.EqualTo(AudioSampleFormat.F32));
    }

    [Test]
    public void ContainerInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        // sampleRateIndex=4 → 44100, channelConfig=1 → mono
        var aac = BuildAdts(sampleRateIndex: 4, channelConfig: 1, frameCount: 50);
        using var ms = new MemoryStream(aac);
        demuxer.Open(ms);

        Assert.That(demuxer.ContainerInfo.FormatName, Is.EqualTo("aac"));
        Assert.That(demuxer.ContainerInfo.StreamCount, Is.EqualTo(1));
        Assert.That(demuxer.ContainerInfo.IsSeekable, Is.True);
        Assert.That(demuxer.ContainerInfo.DurationUs, Is.GreaterThan(0));
    }

    [Test]
    public void ReadPacketSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var aac = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 5);
        using var ms = new MemoryStream(aac);
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
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public async Task ReadPacketAsyncSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var aac = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 5);
        using var ms = new MemoryStream(aac);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = await demuxer.ReadPacketAsync(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
    }

    [Test]
    public void ReadAllPacketsReturnsEndOfFile()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var aac = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 3);
        using var ms = new MemoryStream(aac);
        demuxer.Open(ms);

        var readCount = 0;
        using var packet = new MediaPacketBuffer();

        while (demuxer.ReadPacket(packet) == ContainerResult.Success)
        {
            readCount++;
        }

        Assert.That(readCount, Is.EqualTo(3));
    }

    [Test]
    public void ResetReturnsToStartAndCanReadAgain()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var aac = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 5);
        using var ms = new MemoryStream(aac);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        demuxer.ReadPacket(packet);

        demuxer.Reset();

        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.PtsUs, Is.Zero);
    }

    [Test]
    public void SeekNotOpenReturnsNotOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var result = demuxer.Seek(TimeSpan.FromSeconds(1));
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public void SeekSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var aac = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 100);
        using var ms = new MemoryStream(aac);
        demuxer.Open(ms);

        var result = demuxer.Seek(TimeSpan.FromSeconds(0.5));
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void AacRegisteredInContainerFactory()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac");
        Assert.That(demuxer, Is.Not.Null);

        var format = ContainerFactory.GetFormatFromExtension(".aac");
        Assert.That(format, Is.EqualTo("aac"));
    }

    [Test]
    public void DifferentSampleRates()
    {
        // sampleRateIndex=0 → 96000
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var aac = BuildAdts(sampleRateIndex: 0, channelConfig: 2, frameCount: 5);
        using var ms = new MemoryStream(aac);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams[0].AudioParameters!.Value.SampleRate, Is.EqualTo(96000));
    }

    [Test]
    public void Id3v2TagSkipped()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("aac")!;
        var adts = BuildAdts(sampleRateIndex: 3, channelConfig: 2, frameCount: 5);

        // Prepend ID3v2 header
        using var ms = new MemoryStream();
        ms.Write("ID3"u8);
        ms.WriteByte(4); // version major
        ms.WriteByte(0); // version minor
        ms.WriteByte(0); // flags
        // size: 100 bytes in syncsafe int (each byte uses 7 bits)
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(100);
        ms.Write(new byte[100]); // tag data
        ms.Write(adts);

        ms.Position = 0;
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    /// <summary>
    /// Builds a synthetic ADTS AAC stream.
    /// </summary>
    /// <param name="sampleRateIndex">Index into MPEG-4 sample rate table (0=96000, 3=48000, 4=44100, etc.)</param>
    /// <param name="channelConfig">Channel configuration (1=mono, 2=stereo)</param>
    /// <param name="frameCount">Number of ADTS frames to generate</param>
    private static byte[] BuildAdts(int sampleRateIndex, int channelConfig, int frameCount)
    {
        using var ms = new MemoryStream();

        for (var i = 0; i < frameCount; i++)
        {
            WriteAdtsFrame(ms, sampleRateIndex, channelConfig);
        }

        return ms.ToArray();
    }

    private static void WriteAdtsFrame(Stream ms, int sampleRateIndex, int channelConfig)
    {
        // ADTS frame: 7-byte header + payload
        var payloadSize = 128;
        var frameLength = 7 + payloadSize;

        // Byte 0: sync word high (0xFF)
        ms.WriteByte(0xFF);

        // Byte 1: sync word low (0xF0) | ID=1 (MPEG-4) | Layer=0 | Protection absent=1
        ms.WriteByte(0xF1);

        // Byte 2: Profile(2) | SampleRateIndex(4) | Private(1) | ChannelConfig high(1)
        // Profile: 01 = AAC-LC
        var byte2 = (byte)(0x40 | ((sampleRateIndex & 0x0F) << 2) | ((channelConfig >> 2) & 0x01));
        ms.WriteByte(byte2);

        // Byte 3: ChannelConfig low(2) | Original(1) | Home(1) | Copyright(1) | CopyrightStart(1) | FrameLength high(2)
        var byte3 = (byte)(((channelConfig & 0x03) << 6) | ((frameLength >> 11) & 0x03));
        ms.WriteByte(byte3);

        // Byte 4: FrameLength mid(8)
        ms.WriteByte((byte)((frameLength >> 3) & 0xFF));

        // Byte 5: FrameLength low(3) | Buffer fullness high(5)
        var byte5 = (byte)(((frameLength & 0x07) << 5) | 0x1F);
        ms.WriteByte(byte5);

        // Byte 6: Buffer fullness low(6) | Number of AAC frames minus 1 (2)
        ms.WriteByte(0xFC);

        // Payload
        ms.Write(new byte[payloadSize]);
    }
}
