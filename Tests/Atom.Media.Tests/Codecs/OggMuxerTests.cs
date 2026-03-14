namespace Atom.Media.Tests;

[TestFixture]
public sealed class OggMuxerTests
{
    [Test]
    public void CreateMuxerReturnsOggMuxer()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg");
        Assert.That(muxer, Is.Not.Null);
    }

    [Test]
    public void OpenWithStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        var result = muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(muxer.IsOpen, Is.True);
    }

    [Test]
    public void OpenWithoutStreamOrPathReturnsError()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        var result = muxer.Open(new MuxerParameters { FormatName = "ogg" });
        Assert.That(result, Is.EqualTo(ContainerResult.Error));
    }

    [Test]
    public void AddOpusAudioStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });

        var (result, index) = muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 48000, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Opus);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(index, Is.Zero);
    }

    [Test]
    public void AddVorbisAudioStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });

        var (result, index) = muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Vorbis);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(index, Is.Zero);
    }

    [Test]
    public void AddUnsupportedCodecReturnsUnsupported()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });

        var (result, _) = muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 2, SampleFormat = AudioSampleFormat.S16 },
            MediaCodecId.Mp3);

        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
    }

    [Test]
    public void AddVideoStreamReturnsUnsupported()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });

        var (result, _) = muxer.AddVideoStream(
            new VideoCodecParameters { Width = 1920, Height = 1080, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp9);

        Assert.That(result, Is.EqualTo(ContainerResult.UnsupportedFormat));
    }

    [Test]
    public void WriteOpusHeaderProducesOggPages()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 48000, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Opus);

        var result = muxer.WriteHeader();
        Assert.That(result, Is.EqualTo(ContainerResult.Success));

        // Should have written at least 2 Ogg pages (OpusHead + OpusTags)
        ms.Position = 0;
        var capture = "OggS"u8;
        Span<byte> buf = stackalloc byte[4];
        ms.ReadExactly(buf);
        Assert.That(buf.SequenceEqual(capture), Is.True);
    }

    [Test]
    public void WriteVorbisHeaderProducesOggPages()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 44100, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Vorbis);

        var result = muxer.WriteHeader();
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(ms.Length, Is.GreaterThan(0));
    }

    [Test]
    public void WritePacketProducesData()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 48000, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Opus);
        muxer.WriteHeader();

        var beforeLen = ms.Length;
        var audioData = new byte[960];
        var packet = new MediaPacket(audioData, 0, 0, 0, 20_000); // 20ms of Opus audio
        var result = muxer.WritePacket(in packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(ms.Length, Is.GreaterThan(beforeLen));
    }

    [Test]
    public void WriteTrailerWritesEosPage()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });
        muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 48000, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Opus);
        muxer.WriteHeader();
        muxer.WriteTrailer();

        // Look for last OggS page
        var data = ms.ToArray();
        var lastOggS = -1;
        for (var i = data.Length - 27; i >= 0; i--)
        {
            if (data[i] == 'O' && data[i + 1] == 'g' && data[i + 2] == 'g' && data[i + 3] == 'S')
            {
                lastOggS = i;
                break;
            }
        }

        Assert.That(lastOggS, Is.GreaterThanOrEqualTo(0));
        // Byte 5 of Ogg page is header type flags; 0x04 = EOS
        Assert.That(data[lastOggS + 5] & 0x04, Is.EqualTo(0x04));
    }

    [Test]
    public void WritePacketWithoutHeaderReturnsError()
    {
        using var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });

        var audioData = new byte[100];
        var packet = new MediaPacket(audioData, 0, 0, 0, 0);
        var result = muxer.WritePacket(in packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Error));
    }

    [Test]
    public void DisposeReleasesResources()
    {
        var muxer = ContainerFactory.CreateMuxer("ogg")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "ogg", OutputStream = ms });
        muxer.Dispose();
        Assert.That(muxer.IsOpen, Is.False);
    }
}
