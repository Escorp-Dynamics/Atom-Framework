namespace Atom.Media.Tests;

[TestFixture]
public sealed class MatroskaMuxerTests
{
    [Test]
    public void CreateMuxerReturnsMatroskaMuxer()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska");
        Assert.That(muxer, Is.Not.Null);
    }

    [Test]
    public void CreateWebmMuxerReturnsMatroskaMuxer()
    {
        using var muxer = ContainerFactory.CreateMuxer("webm");
        Assert.That(muxer, Is.Not.Null);
    }

    [Test]
    public void OpenWithStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        var result = muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(muxer.IsOpen, Is.True);
    }

    [Test]
    public void OpenWithoutStreamOrPathReturnsError()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        var result = muxer.Open(new MuxerParameters { FormatName = "matroska" });
        Assert.That(result, Is.EqualTo(ContainerResult.Error));
    }

    [Test]
    public void AddVideoStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });

        var (result, index) = muxer.AddVideoStream(
            new VideoCodecParameters { Width = 1920, Height = 1080, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp9);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(index, Is.Zero);
        Assert.That(muxer.StreamCount, Is.EqualTo(1));
    }

    [Test]
    public void AddAudioStreamSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });

        var (result, index) = muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 48000, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Opus);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(index, Is.Zero);
    }

    [Test]
    public void AddMultipleStreams()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });

        var (vResult, vIndex) = muxer.AddVideoStream(
            new VideoCodecParameters { Width = 1920, Height = 1080, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.H264);
        var (aResult, aIndex) = muxer.AddAudioStream(
            new AudioCodecParameters { SampleRate = 48000, ChannelCount = 2, SampleFormat = AudioSampleFormat.F32 },
            MediaCodecId.Aac);

        Assert.That(vResult, Is.EqualTo(ContainerResult.Success));
        Assert.That(aResult, Is.EqualTo(ContainerResult.Success));
        Assert.That(vIndex, Is.Zero);
        Assert.That(aIndex, Is.EqualTo(1));
        Assert.That(muxer.StreamCount, Is.EqualTo(2));
    }

    [Test]
    public void WriteHeaderProducesEbmlData()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });
        muxer.AddVideoStream(
            new VideoCodecParameters { Width = 1920, Height = 1080, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp9);

        var result = muxer.WriteHeader();
        Assert.That(result, Is.EqualTo(ContainerResult.Success));

        // EBML header starts with 0x1A45DFA3
        ms.Position = 0;
        var first = ms.ReadByte();
        Assert.That(first, Is.EqualTo(0x1A));
    }

    [Test]
    public void WritePacketSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });
        muxer.AddVideoStream(
            new VideoCodecParameters { Width = 1280, Height = 720, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp9);
        muxer.WriteHeader();

        var beforeLen = ms.Length;
        var frameData = new byte[1024];
        var packet = new MediaPacket(frameData, 0, 0, 0, 33_333, PacketProperty.Keyframe);
        var result = muxer.WritePacket(in packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Success));
        Assert.That(ms.Length, Is.GreaterThan(beforeLen));
    }

    [Test]
    public void WritePacketWithoutHeaderReturnsError()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });

        var frameData = new byte[100];
        var packet = new MediaPacket(frameData, 0, 0, 0, 0);
        var result = muxer.WritePacket(in packet);

        Assert.That(result, Is.EqualTo(ContainerResult.Error));
    }

    [Test]
    public void WriteTrailerSucceeds()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });
        muxer.AddVideoStream(
            new VideoCodecParameters { Width = 1280, Height = 720, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp9);
        muxer.WriteHeader();

        var result = muxer.WriteTrailer();
        Assert.That(result, Is.EqualTo(ContainerResult.Success));
    }

    [Test]
    public void NewClusterAfterFiveSeconds()
    {
        using var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });
        muxer.AddVideoStream(
            new VideoCodecParameters { Width = 640, Height = 480, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp8);
        muxer.WriteHeader();

        // Write packet at t=0
        var data1 = new byte[100];
        var pkt1 = new MediaPacket(data1, 0, 0, 0, 33_333, PacketProperty.Keyframe);
        muxer.WritePacket(in pkt1);
        var len1 = ms.Length;

        // Write packet at t=6s (should trigger new cluster)
        var data2 = new byte[100];
        var pkt2 = new MediaPacket(data2, 0, 6_000_000, 6_000_000, 33_333, PacketProperty.Keyframe);
        muxer.WritePacket(in pkt2);

        // New cluster headers should add extra bytes
        var bytesAfterSecondPacket = ms.Length - len1;
        // Should be more than just the SimpleBlock data (100 bytes + block header)
        // because a new cluster element was started
        Assert.That(bytesAfterSecondPacket, Is.GreaterThan(120));
    }

    [Test]
    public void WebmFormatUsesWebmDocType()
    {
        using var muxer = ContainerFactory.CreateMuxer("webm")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "webm", OutputStream = ms });
        muxer.AddVideoStream(
            new VideoCodecParameters { Width = 640, Height = 480, PixelFormat = VideoPixelFormat.Yuv420P },
            MediaCodecId.Vp9);
        muxer.WriteHeader();

        // Check that "webm" appears in the EBML header
        var data = ms.ToArray();
        var webmFound = false;
        for (var i = 0; i < data.Length - 4; i++)
        {
            if (data[i] == 'w' && data[i + 1] == 'e' && data[i + 2] == 'b' && data[i + 3] == 'm')
            {
                webmFound = true;
                break;
            }
        }

        Assert.That(webmFound, Is.True);
    }

    [Test]
    public void DisposeReleasesResources()
    {
        var muxer = ContainerFactory.CreateMuxer("matroska")!;
        using var ms = new MemoryStream();
        muxer.Open(new MuxerParameters { FormatName = "matroska", OutputStream = ms });
        muxer.Dispose();
        Assert.That(muxer.IsOpen, Is.False);
    }
}
