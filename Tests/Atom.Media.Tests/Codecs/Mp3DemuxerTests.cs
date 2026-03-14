using System.Buffers.Binary;

namespace Atom.Media.Tests;

[TestFixture]
public sealed class Mp3DemuxerTests
{
    [Test]
    public void CreateDemuxerReturnsMp3DemuxerForMp3Format()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3");
        Assert.That(demuxer, Is.Not.Null);
    }

    [Test]
    public void OpenFileNotFoundReturnsFileNotFound()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var result = demuxer.Open("/tmp/nonexistent_audio_file_12345.mp3");
        Assert.That(result, Is.EqualTo(ContainerResult.FileNotFound));
    }

    [Test]
    public void OpenCorruptDataReturnsUnsupportedFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
    }

    [Test]
    public void OpenValidMp3Succeeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 10);
        using var ms = new MemoryStream(mp3);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void StreamInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 5);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.BestAudioStreamIndex, Is.Zero);
        Assert.That(demuxer.BestVideoStreamIndex, Is.EqualTo(-1));

        var stream = demuxer.Streams[0];
        Assert.That(stream.Type, Is.EqualTo(MediaStreamType.Audio));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Mp3));
        Assert.That(stream.AudioParameters, Is.Not.Null);
        Assert.That(stream.AudioParameters!.Value.SampleRate, Is.EqualTo(44100));
        Assert.That(stream.AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void ContainerInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 20);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        Assert.That(demuxer.ContainerInfo.FormatName, Is.EqualTo("mp3"));
        Assert.That(demuxer.ContainerInfo.IsSeekable, Is.True);
        Assert.That(demuxer.ContainerInfo.IsLiveStream, Is.False);
        Assert.That(demuxer.ContainerInfo.StreamCount, Is.EqualTo(1));
        Assert.That(demuxer.ContainerInfo.DurationUs, Is.GreaterThan(0));
    }

    [Test]
    public void ReadPacketReturnsData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 5);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
        Assert.That(packet.StreamIndex, Is.Zero);
        Assert.That(packet.PtsUs, Is.Zero);
    }

    [Test]
    public void ReadPacketReturnsEndOfFileWhenExhausted()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 2);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();

        var readCount = 0;
        while (demuxer.ReadPacket(packet) == ContainerResult.Success)
        {
            readCount++;
        }

        Assert.That(readCount, Is.EqualTo(2));
    }

    [Test]
    public void ReadPacketNotOpenReturnsNotOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public async Task ReadPacketAsyncReturnsData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 3);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = await demuxer.ReadPacketAsync(packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
    }

    [Test]
    public void ResetRestartsReading()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 3);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        while (demuxer.ReadPacket(packet) == ContainerResult.Success) { }

        demuxer.Reset();

        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.PtsUs, Is.Zero);
    }

    [Test]
    public void SeekSuccess()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 50);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        var result = demuxer.Seek(TimeSpan.FromMilliseconds(500));
        Assert.That(result, Is.EqualTo(ContainerResult.Success));

        using var packet = new MediaPacketBuffer();
        var readResult = demuxer.ReadPacket(packet);
        Assert.That(readResult, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void OpenWithId3v2TagSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3WithId3v2(frameCount: 5);
        using var ms = new MemoryStream(mp3);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void DurationIncreasesWithMoreFrames()
    {
        using var demuxer1 = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3Short = BuildMp3(frameCount: 10);
        using var ms1 = new MemoryStream(mp3Short);
        demuxer1.Open(ms1);

        using var demuxer2 = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3Long = BuildMp3(frameCount: 100);
        using var ms2 = new MemoryStream(mp3Long);
        demuxer2.Open(ms2);

        Assert.That(demuxer2.ContainerInfo.DurationUs,
            Is.GreaterThan(demuxer1.ContainerInfo.DurationUs));
    }

    [Test]
    public void GetRegisteredDemuxersContainsMp3()
    {
        var formats = ContainerFactory.GetRegisteredDemuxers();
        Assert.That(formats, Does.Contain("mp3"));
    }

    [Test]
    public void PtsAdvancesAcrossPackets()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("mp3")!;
        var mp3 = BuildMp3(frameCount: 5);
        using var ms = new MemoryStream(mp3);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        demuxer.ReadPacket(packet);
        var firstPts = packet.PtsUs;

        demuxer.ReadPacket(packet);
        var secondPts = packet.PtsUs;

        Assert.That(secondPts, Is.GreaterThan(firstPts));
    }

    /// <summary>
    /// Builds a synthetic CBR MP3 (MPEG1 Layer III, 128kbps, 44100Hz, stereo).
    /// </summary>
    private static byte[] BuildMp3(int frameCount)
    {
        // MPEG1 Layer III, 128kbps, 44100Hz, JointStereo
        // Frame header: 0xFF 0xFB 0x90 0x00
        // Frame size = 144 * 128000 / 44100 = 417 bytes (no padding)
        const int frameSize = 417;
        var data = new byte[frameCount * frameSize];

        for (var i = 0; i < frameCount; i++)
        {
            var offset = i * frameSize;
            data[offset] = 0xFF;
            data[offset + 1] = 0xFB; // MPEG1, Layer III, no CRC
            data[offset + 2] = 0x90; // 128kbps, 44100Hz
            data[offset + 3] = 0x00; // Stereo, no padding
        }

        return data;
    }

    /// <summary>
    /// Builds a synthetic MP3 with an ID3v2 tag prepended.
    /// </summary>
    private static byte[] BuildMp3WithId3v2(int frameCount)
    {
        var mp3Data = BuildMp3(frameCount);
        var id3Size = 64;

        // ID3v2 header: "ID3" + version(2 bytes) + flags(1 byte) + size(4 bytes syncsafe)
        var result = new byte[10 + id3Size + mp3Data.Length];
        result[0] = (byte)'I';
        result[1] = (byte)'D';
        result[2] = (byte)'3';
        result[3] = 4; // version 2.4
        result[4] = 0; // revision
        result[5] = 0; // flags

        // Syncsafe integer for size (each byte uses only 7 bits)
        result[6] = (byte)((id3Size >> 21) & 0x7F);
        result[7] = (byte)((id3Size >> 14) & 0x7F);
        result[8] = (byte)((id3Size >> 7) & 0x7F);
        result[9] = (byte)(id3Size & 0x7F);

        mp3Data.CopyTo(result, 10 + id3Size);
        return result;
    }
}
