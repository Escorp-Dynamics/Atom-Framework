using System.Buffers.Binary;

namespace Atom.Media.Tests;

[TestFixture]
public sealed class WavMuxerTests
{
    [Test]
    public void CreateMuxerReturnsWavMuxer()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav");
        Assert.That(muxer, Is.Not.Null);
    }

    [Test]
    public void OpenWithStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        var result = muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(muxer.IsOpen, Is.True);
    }

    [Test]
    public void OpenWithoutStreamOrPathReturnsError()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        var result = muxer.Open(new MuxerParameters { FormatName = "wav" });
        Assert.That(result, Is.EqualTo(ContainerResult.Error));
    }

    [Test]
    public void AddAudioStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });

        var (result, index) = muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 2, SampleFormat = AudioSampleFormat.S16 },
            MediaCodecId.PcmS16Le);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(index, Is.Zero);
        Assert.That(muxer.StreamCount, Is.EqualTo(1));
    }

    [Test]
    public void AddVideoStreamReturnsUnsupported()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });

        var (result, index) = muxer.AddVideoStream(
            new VideoCodecParameters { Width = 1920, Height = 1080, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp9);

        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
        Assert.That(index, Is.EqualTo(-1));
    }

    [Test]
    public void WriteHeaderSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 2, SampleFormat = AudioSampleFormat.S16 },
            MediaCodecId.PcmS16Le);

        var result = muxer.WriteHeader();
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(ms.Length, Is.EqualTo(44)); // WAV header = 44 bytes
    }

    [Test]
    public void WriteHeaderWithoutStreamReturnsError()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });

        // No stream added
        var result = muxer.WriteHeader();
        Assert.That(result, Is.EqualTo(ContainerResult.Error));
    }

    [Test]
    public void WritePacketAppendsData()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 1, SampleFormat = AudioSampleFormat.S16 },
            MediaCodecId.PcmS16Le);
        muxer.WriteHeader();

        var audioData = new byte[1024];
        var packet = new MediaPacket(audioData, 0, 0, 0, 0);
        var result = muxer.WritePacket(in packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(ms.Length, Is.EqualTo(44 + 1024));
    }

    [Test]
    public void WriteTrailerFixesRiffHeader()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 1, SampleFormat = AudioSampleFormat.S16 },
            MediaCodecId.PcmS16Le);
        muxer.WriteHeader();

        var audioData = new byte[2000];
        var packet = new MediaPacket(audioData, 0, 0, 0, 0);
        muxer.WritePacket(in packet);

        var result = muxer.WriteTrailer();
        Assert.That(result, Is.EqualTo(ContainerResult.Success));

        // Verify RIFF header
        ms.Position = 0;
        Span<byte> header = stackalloc byte[44];
        ms.ReadExactly(header);

        Assert.That(header[..4].SequenceEqual("RIFF"u8), Is.True);
        Assert.That(header[8..12].SequenceEqual("WAVE"u8), Is.True);

        var riffSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        Assert.That(riffSize, Is.EqualTo((int)ms.Length - 8));

        var dataSize = BinaryPrimitives.ReadInt32LittleEndian(header[40..]);
        Assert.That(dataSize, Is.EqualTo(2000));
    }

    [Test]
    public void RoundTripWavMuxerToDemuxer()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 48000, ChannelCount = 2, SampleFormat = AudioSampleFormat.S16 },
            MediaCodecId.PcmS16Le);
        muxer.WriteHeader();

        // Write some audio data
        var audioData = new byte[4800]; // 25ms of stereo S16 at 48kHz
        var packet = new MediaPacket(audioData, 0, 0, 0, 25_000);
        muxer.WritePacket(in packet);
        muxer.WriteTrailer();

        // Demux
        ms.Position = 0;
        using var demuxer = ContainerFactory.CreateDemuxer("wav")!;
        var openResult = demuxer.Open(ms);
        Assert.That(openResult, Is.EqualTo(ContainerResult.Success));

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));
        Assert.That(demuxer.Streams[0].AudioParameters!.Value.SampleRate, Is.EqualTo(48000));
        Assert.That(demuxer.Streams[0].AudioParameters!.Value.ChannelCount, Is.EqualTo(2));

        // Read all packets (demuxer may chunk the data)
        var totalRead = 0;
        using var readPacket = new MediaPacketBuffer();
        while (demuxer.ReadPacket(readPacket) == ContainerResult.Success)
        {
            totalRead += readPacket.Size;
        }

        Assert.That(totalRead, Is.EqualTo(4800));
    }

    [Test]
    public void FloatFormatWritesCorrectAudioFormat()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 1, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.PcmF32Le);
        muxer.WriteHeader();
        muxer.WriteTrailer();

        ms.Position = 0;
        Span<byte> header = stackalloc byte[44];
        ms.ReadExactly(header);

        // Audio format 3 = IEEE Float
        var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(header[20..]);
        Assert.That(audioFormat, Is.EqualTo(3));
    }

    [Test]
    public async Task WritePacketAsyncSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 1, SampleFormat = AudioSampleFormat.S16 },
            MediaCodecId.PcmS16Le);
        muxer.WriteHeader();

        using var packetBuf = new MediaPacketBuffer();
        packetBuf.SetData(new byte[512]);
        var result = await muxer.WritePacketAsync(packetBuf);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(ms.Length, Is.EqualTo(44 + 512));
    }

    [Test]
    public void DisposeReleasesResources()
    {
        var muxer = ContainerFactory.CreateMuxer("wav")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "wav", OutputStream = ms });
        muxer.Dispose();
        Assert.That(muxer.IsOpen, Is.False);
    }
}
