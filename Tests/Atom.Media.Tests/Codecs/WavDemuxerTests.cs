using System.Buffers.Binary;

namespace Atom.Media.Tests;

[TestFixture]
public sealed class WavDemuxerTests
{
    [Test]
    public void CreateDemuxerReturnsWavDemuxerForWavFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav");
        Assert.That(demuxer, Is.Not.Null);
    }

    [Test]
    public void OpenFileNotFoundReturnsFileNotFound()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var result = demuxer.Open("/tmp/nonexistent_audio_file_12345.wav");
        Assert.That(result, Is.EqualTo(ContainerResult.FileNotFound));
    }

    [Test]
    public void OpenCorruptDataReturnsCorruptData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        using var ms = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.CorruptData));
    }

    [Test]
    public void OpenNonWaveRiffReturnsUnsupportedFormat()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var data = new byte[44];
        "RIFF"u8.CopyTo(data);
        "AVI "u8.CopyTo(data.AsSpan(8));
        using var ms = new MemoryStream(data);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
    }

    [Test]
    public void OpenValidWavSucceeds()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 1, bitsPerSample: 16, sampleCount: 1024);
        using var ms = new MemoryStream(wav);
        var result = demuxer.Open(ms);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void StreamInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 48000, channels: 2, bitsPerSample: 16, sampleCount: 512);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.BestAudioStreamIndex, Is.Zero);
        Assert.That(demuxer.BestVideoStreamIndex, Is.EqualTo(-1));

        var stream = demuxer.Streams[0];
        Assert.That(stream.Type, Is.EqualTo(MediaStreamType.Audio));
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.PcmS16Le));
        Assert.That(stream.AudioParameters, Is.Not.Null);
        Assert.That(stream.AudioParameters!.Value.SampleRate, Is.EqualTo(48000));
        Assert.That(stream.AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
        Assert.That(stream.AudioParameters!.Value.SampleFormat, Is.EqualTo(AudioSampleFormat.S16));
    }

    [Test]
    public void ContainerInfoCorrectAfterOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 1, bitsPerSample: 16, sampleCount: 44100);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        Assert.That(demuxer.ContainerInfo.FormatName, Is.EqualTo("wav"));
        Assert.That(demuxer.ContainerInfo.IsSeekable, Is.True);
        Assert.That(demuxer.ContainerInfo.IsLiveStream, Is.False);
        Assert.That(demuxer.ContainerInfo.StreamCount, Is.EqualTo(1));
        Assert.That(demuxer.ContainerInfo.DurationUs, Is.GreaterThan(0));
    }

    [Test]
    public void ReadPacketReturnsAudioData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 1, bitsPerSample: 16, sampleCount: 100);
        using var ms = new MemoryStream(wav);
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
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 1, bitsPerSample: 16, sampleCount: 10);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();

        // Read all data
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));

        // Next read should be EOF
        result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.EndOfFile));
    }

    [Test]
    public async Task ReadPacketAsyncReturnsData()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 1, bitsPerSample: 16, sampleCount: 100);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var result = await demuxer.ReadPacketAsync(packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));
    }

    [Test]
    public void ResetAllowsReReading()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 1, bitsPerSample: 16, sampleCount: 10);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        demuxer.ReadPacket(packet);
        var firstSize = packet.Size;

        // Exhaust
        while (demuxer.ReadPacket(packet) != ContainerResult.EndOfFile) { }

        // Reset and re-read
        demuxer.Reset();
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.EqualTo(firstSize));
    }

    [Test]
    public void SeekReturnsSuccess()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 1, bitsPerSample: 16, sampleCount: 44100);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        var result = demuxer.Seek(TimeSpan.FromSeconds(0.5));
        Assert.That(result, Is.EqualTo(ContainerResult.Success));

        using var packet = new MediaPacketBuffer();
        demuxer.ReadPacket(packet);
        Assert.That(packet.PtsUs, Is.GreaterThan(0));
    }

    [Test]
    public void ReadPacketNotOpenReturnsNotOpen()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        using var packet = new MediaPacketBuffer();
        var result = demuxer.ReadPacket(packet);
        Assert.That(result, Is.EqualTo(ContainerResult.NotOpen));
    }

    [Test]
    public void FloatWavOpensSuccessfully()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 48000, channels: 1, bitsPerSample: 32, sampleCount: 256, audioFormat: 3);
        using var ms = new MemoryStream(wav);
        var result = demuxer.Open(ms);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(demuxer.Streams[0].CodecId, Is.EqualTo(MediaCodecId.PcmF32Le));
        Assert.That(demuxer.Streams[0].AudioParameters!.Value.SampleFormat, Is.EqualTo(AudioSampleFormat.F32));
    }

    [Test]
    public void StereoWavHasCorrectChannelCount()
    {
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: 2, bitsPerSample: 16, sampleCount: 100);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams[0].AudioParameters!.Value.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void GetRegisteredDemuxersIncludesWav()
    {
        var formats = ContainerFactory.GetRegisteredDemuxers().ToList();
        Assert.That(formats, Does.Contain("wav"));
    }

    [Test]
    public void TotalReadBytesMatchPcmData()
    {
        const int sampleCount = 500;
        const int channels = 1;
        const int bitsPerSample = 16;

        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var wav = BuildWav(sampleRate: 44100, channels: channels, bitsPerSample: bitsPerSample, sampleCount: sampleCount);
        using var ms = new MemoryStream(wav);
        demuxer.Open(ms);

        using var packet = new MediaPacketBuffer();
        var totalBytes = 0;
        while (demuxer.ReadPacket(packet) == ContainerResult.Success)
        {
            totalBytes += packet.Size;
        }

        Assert.That(totalBytes, Is.EqualTo(sampleCount * channels * (bitsPerSample / 8)));
    }

    // --- Helpers ---

    private static byte[] BuildWav(int sampleRate, int channels, int bitsPerSample, int sampleCount, int audioFormat = 1)
    {
        var bytesPerSample = bitsPerSample / 8;
        var dataSize = sampleCount * channels * bytesPerSample;
        var fmtChunkSize = 16;
        var totalSize = 4 + (8 + fmtChunkSize) + (8 + dataSize);
        var wav = new byte[12 + (8 + fmtChunkSize) + (8 + dataSize)];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), totalSize);
        "WAVE"u8.CopyTo(span.Slice(8));

        var pos = 12;
        "fmt "u8.CopyTo(span.Slice(pos));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 4), fmtChunkSize);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 8), (short)audioFormat);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 10), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 12), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 16), sampleRate * channels * bytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 20), (short)(channels * bytesPerSample));
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 22), (short)bitsPerSample);

        pos += 8 + fmtChunkSize;
        "data"u8.CopyTo(span.Slice(pos));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 4), dataSize);

        return wav;
    }
}
