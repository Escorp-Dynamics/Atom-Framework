namespace Atom.Media.Tests;

[TestFixture]
public sealed class FlacDemuxerTests
{
    [Test]
    public void CreateDemuxerReturnsFlacDemuxerForFlacFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac");
        Assert.That(demuxer, Is.Not.Null);
    }

    [Test]
    public void OpenFileNotFoundReturnsFileNotFound()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var result = demuxer.Open("/tmp/nonexistent_audio_file_12345.flac");
        Assert.That(result, Is.EqualTo(ContainerResult.FileNotFound));
    }

    [Test]
    public void OpenCorruptDataReturnsCorruptData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.CorruptData));
    }

    [Test]
    public void OpenNonFlacMagicReturnsUnsupportedFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        // Valid size but wrong magic
        var data = new byte[100];
        "RIFF"u8.CopyTo(data);
        using var ms = new MemoryStream(data);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
    }

    [Test]
    public void OpenValidFlacSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 44100, channels: 2, bitsPerSample: 16, totalSamples: 44100);
        using var ms = new MemoryStream(flac);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void StreamInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 48000, channels: 2, bitsPerSample: 16, totalSamples: 48000);
        using var ms = new MemoryStream(flac);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.BestAudioStreamIndex, Is.Zero);
        Assert.That(demuxer.BestVideoStreamIndex, Is.EqualTo(-1));

        var stream = demuxer.Streams[0];
        Assert.That(stream.Type, Is.EqualTo(MediaStreamType.Audio));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Flac));
        Assert.That(stream.AudioParameters, Is.Not.Null);
        Assert.That(stream.AudioParameters!.Value.SampleRate, Is.EqualTo(48000));
        Assert.That(stream.AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
        Assert.That(stream.AudioParameters!.Value.SampleFormat, Is.EqualTo(AudioSampleFormat.S16));
    }

    [Test]
    public void ContainerInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 44100, channels: 1, bitsPerSample: 16, totalSamples: 44100);
        using var ms = new MemoryStream(flac);
        demuxer.Open(ms);

        Assert.That(demuxer.ContainerInfo.FormatName, Is.EqualTo("flac"));
        Assert.That(demuxer.ContainerInfo.StreamCount, Is.EqualTo(1));
        Assert.That(demuxer.ContainerInfo.IsSeekable, Is.True);
        Assert.That(demuxer.ContainerInfo.DurationUs, Is.GreaterThan(0));
    }

    [Test]
    public void ReadPacketSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 44100, channels: 2, bitsPerSample: 16, totalSamples: 4096);
        using var ms = new MemoryStream(flac);
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
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public async Task ReadPacketAsyncSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 44100, channels: 2, bitsPerSample: 16, totalSamples: 4096);
        using var ms = new MemoryStream(flac);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = await demuxer.ReadPacketAsync(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
    }

    [Test]
    public void ResetReturnsToStartAndCanReadAgain()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 44100, channels: 2, bitsPerSample: 16, totalSamples: 4096);
        using var ms = new MemoryStream(flac);
        demuxer.Open(ms);

        using var packet1 = new MediaPacketBuffer();
        demuxer.ReadPacket(packet1);

        demuxer.Reset();

        using var packet2 = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet2);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet2.PtsUs, Is.Zero);
    }

    [Test]
    public void SeekNotOpenReturnsNotOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var result = demuxer.Seek(TimeSpan.FromSeconds(1));
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public void SeekSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 44100, channels: 2, bitsPerSample: 16, totalSamples: 88200);
        using var ms = new MemoryStream(flac);
        demuxer.Open(ms);

        var result = demuxer.Seek(TimeSpan.FromSeconds(0.5));
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void BitsPerSample24MapsToS32()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 96000, channels: 2, bitsPerSample: 24, totalSamples: 4096);
        using var ms = new MemoryStream(flac);
        demuxer.Open(ms);

        var stream = demuxer.Streams[0];
        Assert.That(stream.AudioParameters!.Value.SampleFormat, Is.EqualTo(AudioSampleFormat.S32));
    }

    [Test]
    public void BitsPerSample8MapsToU8()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        var flac = BuildFlac(sampleRate: 22050, channels: 1, bitsPerSample: 8, totalSamples: 4096);
        using var ms = new MemoryStream(flac);
        demuxer.Open(ms);

        var stream = demuxer.Streams[0];
        Assert.That(stream.AudioParameters!.Value.SampleFormat, Is.EqualTo(AudioSampleFormat.U8));
    }

    [Test]
    public void FlacRegisteredInContainerFactory()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("flac");
        Assert.That(demuxer, Is.Not.Null);

        var format = ContainerFactory.GetFormatFromExtension(".flac");
        Assert.That(format, Is.EqualTo("flac"));
    }

    /// <summary>
    /// Builds a synthetic FLAC file with fLaC magic, STREAMINFO metadata block and fake FLAC frames.
    /// </summary>
    private static byte[] BuildFlac(int sampleRate, int channels, int bitsPerSample, long totalSamples)
    {
        using var ms = new MemoryStream();

        // Magic: "fLaC"
        ms.Write("fLaC"u8);

        // STREAMINFO metadata block (last block, type=0)
        var streamInfoData = BuildStreamInfo(sampleRate, channels, bitsPerSample, totalSamples);
        ms.WriteByte(0x80); // last=1, type=0 (STREAMINFO)
        // 3-byte size
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte((byte)streamInfoData.Length);
        ms.Write(streamInfoData);

        // Write synthetic FLAC frames
        var samplesRemaining = totalSamples;
        var blockSize = 4096;

        while (samplesRemaining > 0)
        {
            var currentBlockSize = (int)Math.Min(blockSize, samplesRemaining);
            WriteFlacFrame(ms, currentBlockSize, sampleRate, channels, bitsPerSample);
            samplesRemaining -= currentBlockSize;
        }

        return ms.ToArray();
    }

    private static byte[] BuildStreamInfo(int sampleRate, int channels, int bitsPerSample, long totalSamples)
    {
        // STREAMINFO: 34 bytes
        // bytes 0-1: min block size
        // bytes 2-3: max block size
        // bytes 4-6: min frame size
        // bytes 7-9: max frame size
        // bytes 10-13: sample rate (20 bits), channels-1 (3 bits), bps-1 (5 bits), total samples upper 4 bits
        // bytes 14-17: total samples lower 32 bits
        // bytes 18-33: MD5 (zeroes)
        var info = new byte[34];

        // Block sizes
        info[0] = 0x10; info[1] = 0x00; // min=4096
        info[2] = 0x10; info[3] = 0x00; // max=4096

        // Sample rate (20 bits) | channels-1 (3 bits) | bps-1 (5 bits) | total samples (36 bits)
        var channelsMinus1 = channels - 1;
        var bpsMinus1 = bitsPerSample - 1;

        info[10] = (byte)(sampleRate >> 12);
        info[11] = (byte)(sampleRate >> 4);
        info[12] = (byte)(((sampleRate & 0x0F) << 4) | ((channelsMinus1 & 0x07) << 1) | ((bpsMinus1 >> 4) & 0x01));
        info[13] = (byte)(((bpsMinus1 & 0x0F) << 4) | (int)((totalSamples >> 32) & 0x0F));
        info[14] = (byte)(totalSamples >> 24);
        info[15] = (byte)(totalSamples >> 16);
        info[16] = (byte)(totalSamples >> 8);
        info[17] = (byte)totalSamples;

        return info;
    }

    private static void WriteFlacFrame(Stream ms, int blockSize, int sampleRate, int channels, int bitsPerSample)
    {
        // FLAC frame sync: 0xFFF8 (14 sync bits + reserved bit + blocking strategy bit)
        ms.WriteByte(0xFF);
        ms.WriteByte(0xF8);

        // Block size code in upper nibble of byte 2
        var blockSizeCode = blockSize switch
        {
            192 => 1,
            576 => 2,
            1152 => 3,
            2304 => 4,
            4608 => 5,
            256 => 8,
            512 => 9,
            1024 => 10,
            2048 => 11,
            4096 => 12,
            8192 => 13,
            16384 => 14,
            32768 => 15,
            _ => 12, // default to 4096
        };

        // byte 2: block_size_code(4) | sample_rate_code(4)
        // sample rate code 0 = use STREAMINFO
        ms.WriteByte((byte)(blockSizeCode << 4));

        // byte 3: channel assignment(4) | sample size(3) | reserved(1)
        ms.WriteByte(0x00);

        // Frame number (UTF-8 coded) - just 0 for simplicity
        ms.WriteByte(0x00);

        // CRC-8 placeholder
        ms.WriteByte(0x00);

        // Some dummy subframe data
        var dummySize = blockSize * channels * (bitsPerSample / 8);
        dummySize = Math.Min(dummySize, 256); // keep test files small
        ms.Write(new byte[dummySize]);
    }
}
