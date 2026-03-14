#pragma warning disable CA1861, MA0051

using System.Buffers.Binary;
using Atom.IO;
using Atom.Media;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты FLAC декодера.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class FlacDecoderTests
{
    #region Initialization

    [TestCase(TestName = "FlacDecoder: Создание декодера")]
    public void CreateDecoder()
    {
        using var decoder = new FlacDecoder();
        Assert.That(decoder.CodecId, Is.EqualTo(MediaCodecId.Flac));
        Assert.That(decoder.Name, Is.EqualTo("FLAC Audio"));
        Assert.That(decoder.Capabilities, Is.EqualTo(CodecCapabilities.Decode | CodecCapabilities.Encode | CodecCapabilities.Lossless));
    }

    [TestCase(TestName = "FlacDecoder: Инициализация декодера — валидные параметры")]
    public void InitializeDecoderValid()
    {
        using var decoder = new FlacDecoder();

        var result = decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "FlacDecoder: Инициализация декодера — невалидные параметры")]
    public void InitializeDecoderInvalid()
    {
        using var decoder = new FlacDecoder();

        var result = decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 0,
            ChannelCount = 0,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "FlacDecoder: InitializeEncoder с валидными параметрами → Success")]
    public void InitializeEncoderValid()
    {
        using var codec = new FlacDecoder();

        var result = codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    #endregion

    #region Error Handling

    [TestCase(TestName = "FlacDecoder: Decode без инициализации → NotInitialized")]
    public void DecodeWithoutInit()
    {
        using var decoder = new FlacDecoder();
        Span<byte> buf = stackalloc byte[4096];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = 256,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode([0xFF, 0xF8, 0x00, 0x00], ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.NotInitialized));
    }

    [TestCase(TestName = "FlacDecoder: Decode слишком коротких данных → NeedMoreData")]
    public void DecodeShortData()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> buf = stackalloc byte[4096];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = 256,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode([0xFF, 0xF8], ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.NeedMoreData));
    }

    [TestCase(TestName = "FlacDecoder: Decode невалидного sync → InvalidData")]
    public void DecodeInvalidSync()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> buf = stackalloc byte[4096];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = 256,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode([0x00, 0x00, 0x00, 0x00, 0x00, 0x00], ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "FlacDecoder: Dispose → ObjectDisposedException")]
    public void DecodeAfterDispose()
    {
        var decoder = new FlacDecoder();
        decoder.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            Span<byte> buf = stackalloc byte[4096];
            var frame = new AudioFrame(buf, new AudioFrameInfo
            {
                SampleCount = 256,
                ChannelCount = 1,
                SampleRate = 44100,
                SampleFormat = AudioSampleFormat.S16,
            });
            decoder.Decode([0xFF, 0xF8, 0x00, 0x00], ref frame);
        });
    }

    #endregion

    #region Flush & Reset

    [TestCase(TestName = "FlacDecoder: Flush → EndOfStream")]
    public void FlushReturnsEndOfStream()
    {
        using var decoder = new FlacDecoder();
        Span<byte> buf = stackalloc byte[256];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = 1,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(decoder.Flush(ref frame), Is.EqualTo(CodecResult.EndOfStream));
    }

    [TestCase(TestName = "FlacDecoder: Reset не бросает исключений")]
    public void ResetDoesNotThrow()
    {
        using var decoder = new FlacDecoder();
        Assert.DoesNotThrow(decoder.Reset);
    }

    #endregion

    #region Constant Subframe Decode

    [TestCase(TestName = "FlacDecoder: Декодирование constant subframe (моно 16-bit)")]
    public void DecodeConstantMono16()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int blockSize = 256;
        const int sampleValue = 1000;
        var packet = BuildConstantFrame(blockSize, 1, 16, 0, sampleValue);

        Span<byte> outputBuf = stackalloc byte[blockSize * 2];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Verify all samples are equal to sampleValue
        for (var i = 0; i < blockSize; i++)
        {
            var sample = BitConverter.ToInt16(outputBuf.Slice(i * 2, 2));
            Assert.That(sample, Is.EqualTo(sampleValue), $"Sample {i} mismatch");
        }
    }

    [TestCase(TestName = "FlacDecoder: Декодирование constant subframe (моно 8-bit)")]
    public void DecodeConstantMono8()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.U8,
        });

        const int blockSize = 128;
        const int sampleValue = 42;
        var packet = BuildConstantFrame(blockSize, 1, 8, 0, sampleValue);

        Span<byte> outputBuf = stackalloc byte[blockSize];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.U8,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // U8: signed sample + 128
        var expected = (byte)(sampleValue + 128);

        for (var i = 0; i < blockSize; i++)
            Assert.That(outputBuf[i], Is.EqualTo(expected), $"Sample {i} mismatch");
    }

    #endregion

    #region Verbatim Subframe Decode

    [TestCase(TestName = "FlacDecoder: Декодирование verbatim subframe (моно 16-bit)")]
    public void DecodeVerbatimMono16()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int blockSize = 8;
        int[] samples = [100, -200, 300, -400, 500, -600, 700, -800];
        var packet = BuildVerbatimFrame(blockSize, 1, 16, 0, samples);

        Span<byte> outputBuf = stackalloc byte[blockSize * 2];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < blockSize; i++)
        {
            var sample = BitConverter.ToInt16(outputBuf.Slice(i * 2, 2));
            Assert.That(sample, Is.EqualTo(samples[i]), $"Sample {i} mismatch");
        }
    }

    #endregion

    #region Fixed Prediction Decode

    [TestCase(TestName = "FlacDecoder: Декодирование fixed order-0 subframe")]
    public void DecodeFixedOrder0()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        // Order 0: residuals ARE the samples
        const int blockSize = 4;
        int[] samples = [10, 20, 30, 40];
        var packet = BuildFixedFrame(blockSize, 1, 16, 0, order: 0, samples);

        Span<byte> outputBuf = stackalloc byte[blockSize * 2];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < blockSize; i++)
        {
            var sample = BitConverter.ToInt16(outputBuf.Slice(i * 2, 2));
            Assert.That(sample, Is.EqualTo(samples[i]), $"Sample {i} mismatch");
        }
    }

    [TestCase(TestName = "FlacDecoder: Декодирование fixed order-1 subframe")]
    public void DecodeFixedOrder1()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        // Order 1: output[i] = residual[i] + output[i-1]
        // Warmup: [100], residuals: [10, 10, 10]
        // Expected: [100, 110, 120, 130]
        const int blockSize = 4;
        int[] expected = [100, 110, 120, 130];
        var packet = BuildFixedFrame(blockSize, 1, 16, 0, order: 1, expected);

        Span<byte> outputBuf = stackalloc byte[blockSize * 2];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < blockSize; i++)
        {
            var sample = BitConverter.ToInt16(outputBuf.Slice(i * 2, 2));
            Assert.That(sample, Is.EqualTo(expected[i]), $"Sample {i} mismatch");
        }
    }

    #endregion

    #region Stereo Channel Decorrelation

    [TestCase(TestName = "FlacDecoder: Декодирование стерео independent channels")]
    public void DecodeStereoIndependent()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int blockSize = 4;
        int[] leftSamples = [100, 200, 300, 400];
        int[] rightSamples = [500, 600, 700, 800];
        var packet = BuildStereoConstantFrame(blockSize, channelAssignment: 1, leftSamples, rightSamples);

        Span<byte> outputBuf = stackalloc byte[blockSize * 2 * 2]; // 2 channels, 2 bytes per sample
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 2,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Interleaved: L R L R L R L R
        for (var i = 0; i < blockSize; i++)
        {
            var left = BitConverter.ToInt16(outputBuf.Slice(i * 4, 2));
            var right = BitConverter.ToInt16(outputBuf.Slice((i * 4) + 2, 2));
            Assert.That(left, Is.EqualTo(leftSamples[i]), $"Left sample {i}");
            Assert.That(right, Is.EqualTo(rightSamples[i]), $"Right sample {i}");
        }
    }

    [TestCase(TestName = "FlacDecoder: Декодирование стерео left/side decorrelation")]
    public void DecodeStereoLeftSide()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int blockSize = 4;
        // Left/side: channel 0 = left, channel 1 = side (left - right)
        // Final: left stays, right = left - side
        int[] leftExpected = [1000, 2000, 3000, 4000];
        int[] rightExpected = [500, 1000, 1500, 2000];
        // side = left - right
        int[] sideSamples = [500, 1000, 1500, 2000];

        var packet = BuildStereoVerbatimFrame(blockSize, channelAssignment: 8, leftExpected, sideSamples);

        Span<byte> outputBuf = stackalloc byte[blockSize * 2 * 2];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 2,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < blockSize; i++)
        {
            var left = BitConverter.ToInt16(outputBuf.Slice(i * 4, 2));
            var right = BitConverter.ToInt16(outputBuf.Slice((i * 4) + 2, 2));
            Assert.That(left, Is.EqualTo(leftExpected[i]), $"Left sample {i}");
            Assert.That(right, Is.EqualTo(rightExpected[i]), $"Right sample {i}");
        }
    }

    #endregion

    #region Async Decode

    [TestCase(TestName = "FlacDecoder: Асинхронное декодирование")]
    public async Task DecodeAsync()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int blockSize = 256;
        const int sampleValue = 500;
        var packet = BuildConstantFrame(blockSize, 1, 16, 0, sampleValue);

        using var buffer = new AudioFrameBuffer(blockSize, 1, 44100, AudioSampleFormat.S16);

        var result = await decoder.DecodeAsync(packet, buffer);
        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    #endregion

    #region Codec Registry

    [TestCase(TestName = "FlacDecoder: Регистрация в CodecRegistry")]
    public void CodecRegistered()
    {
        Assert.That(CodecRegistry.IsAudioCodecRegistered(MediaCodecId.Flac));

        using var codec = CodecRegistry.CreateAudioCodec(MediaCodecId.Flac);
        Assert.That(codec, Is.Not.Null);
        Assert.That(codec, Is.TypeOf<FlacDecoder>());
    }

    #endregion

    #region Round-Trip: Demuxer → Decoder

    [TestCase(TestName = "FlacDecoder: Round-trip FlacDemuxer → FlacDecoder")]
    public void RoundTripDemuxerToDecoder()
    {
        const int sampleRate = 44100;
        const int channels = 1;
        const int bps = 16;
        const int blockSize = 256;
        const int sampleValue = 500;

        // Build a valid FLAC file with proper constant subframes
        var flacFile = BuildFlacFile(sampleRate, channels, bps, blockSize, sampleValue);

        // Open with demuxer
        using var demuxer = ContainerFactory.CreateDemuxer("flac")!;
        using var ms = new MemoryStream(flacFile);
        demuxer.Open(ms);

        Assert.That(demuxer.Streams, Has.Count.EqualTo(1));

        var stream = demuxer.Streams[0];
        Assert.That(stream.CodecId, Is.EqualTo(MediaCodecId.Flac));

        // Initialize decoder with demuxer's stream parameters
        using var decoder = new FlacDecoder();
        var initResult = decoder.InitializeDecoder(stream.AudioParameters!.Value);
        Assert.That(initResult, Is.EqualTo(CodecResult.Success));

        // Read packet from demuxer
        using var packet = new MediaPacketBuffer();
        var readResult = demuxer.ReadPacket(packet);
        Assert.That(readResult, Is.EqualTo(ContainerResult.Success));
        Assert.That(packet.Size, Is.GreaterThan(0));

        // Decode the packet
        Span<byte> outputBuf = stackalloc byte[blockSize * channels * (bps / 8)];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = channels,
            SampleRate = sampleRate,
            SampleFormat = AudioSampleFormat.S16,
        });

        var decodeResult = decoder.Decode(packet.GetData(), ref frame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success));

        // Verify all samples match expected value
        for (var i = 0; i < blockSize; i++)
        {
            var sample = BitConverter.ToInt16(outputBuf.Slice(i * 2, 2));
            Assert.That(sample, Is.EqualTo(sampleValue), $"Sample {i} mismatch in round-trip");
        }
    }

    #endregion

    #region 32-bit Decode

    [TestCase(TestName = "FlacDecoder: Декодирование constant subframe (моно 24-bit → S32)")]
    public void DecodeConstantMono24()
    {
        using var decoder = new FlacDecoder();

        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 96000,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S32,
        });

        const int blockSize = 64;
        const int sampleValue = 100000;
        var packet = BuildConstantFrame(blockSize, 1, 24, 0, sampleValue);

        Span<byte> outputBuf = stackalloc byte[blockSize * 4];
        var frame = new AudioFrame(outputBuf, new AudioFrameInfo
        {
            SampleCount = blockSize,
            ChannelCount = 1,
            SampleRate = 96000,
            SampleFormat = AudioSampleFormat.S32,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < blockSize; i++)
        {
            var sample = BitConverter.ToInt32(outputBuf.Slice(i * 4, 4));
            Assert.That(sample, Is.EqualTo(sampleValue), $"Sample {i} mismatch");
        }
    }

    #endregion

    #region Encode

    [TestCase(TestName = "FlacDecoder: Encode без InitializeEncoder → NotInitialized")]
    public void EncodeWithoutInit()
    {
        using var codec = new FlacDecoder();

        Span<byte> output = stackalloc byte[1024];
        Span<byte> input = stackalloc byte[256];
        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = 64,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.NotInitialized));
        Assert.That(written, Is.Zero);
    }

    [TestCase(TestName = "FlacDecoder: Encode после InitializeDecoder → NotInitialized")]
    public void EncodeAfterInitDecoder()
    {
        using var codec = new FlacDecoder();
        codec.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> output = stackalloc byte[4096];
        Span<byte> input = stackalloc byte[128];
        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = 64,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.NotInitialized));
        Assert.That(written, Is.Zero);
    }

    [TestCase(TestName = "FlacDecoder: Encode буфер слишком мал → OutputBufferTooSmall")]
    public void EncodeOutputTooSmall()
    {
        using var codec = new FlacDecoder();
        codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> output = stackalloc byte[4]; // too small
        Span<byte> input = stackalloc byte[128];
        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = 64,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.OutputBufferTooSmall));
        Assert.That(written, Is.Zero);
    }

    [TestCase(TestName = "FlacDecoder: Encode constant моно 16-bit")]
    public void EncodeConstantMono16()
    {
        using var codec = new FlacDecoder();
        codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 64;
        const short sampleValue = 1000;

        Span<byte> input = stackalloc byte[sampleCount * 2];

        for (var i = 0; i < sampleCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(input.Slice(i * 2, 2), sampleValue);

        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> output = stackalloc byte[4096];
        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(written, Is.GreaterThan(0));

        // Verify by decoding
        using var decoder = new FlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> pcm = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(pcm, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var decResult = decoder.Decode(output[..written], ref frame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2));
            Assert.That(decoded, Is.EqualTo(sampleValue), $"Sample {i}");
        }
    }

    [TestCase(TestName = "FlacDecoder: Encode linear ramp моно 16-bit (FIXED prediction)")]
    public void EncodeLinearRampMono16()
    {
        using var codec = new FlacDecoder();
        codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 128;
        Span<byte> input = stackalloc byte[sampleCount * 2];

        for (var i = 0; i < sampleCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(input.Slice(i * 2, 2), (short)(i * 10));

        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> output = stackalloc byte[8192];
        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(written, Is.GreaterThan(0));

        // Verify by decoding
        using var decoder = new FlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> pcm = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(pcm, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var decResult = decoder.Decode(output[..written], ref frame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2));
            Assert.That(decoded, Is.EqualTo((short)(i * 10)), $"Sample {i}");
        }
    }

    [TestCase(TestName = "FlacDecoder: Encode стерео 16-bit")]
    public void EncodeStereo16()
    {
        using var codec = new FlacDecoder();
        codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 64;
        Span<byte> input = stackalloc byte[sampleCount * 2 * 2]; // 2 channels * 2 bytes

        for (var i = 0; i < sampleCount; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(input.Slice((i * 2) * 2, 2), (short)(i * 5));       // left
            BinaryPrimitives.WriteInt16LittleEndian(input.Slice(((i * 2) + 1) * 2, 2), (short)(i * 3)); // right
        }

        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 2,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> output = stackalloc byte[8192];
        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(written, Is.GreaterThan(0));

        // Verify by decoding
        using var decoder = new FlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> pcm = stackalloc byte[sampleCount * 2 * 2];
        var frame = new AudioFrame(pcm, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 2,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var decResult = decoder.Decode(output[..written], ref frame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var left = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice((i * 2) * 2, 2));
            var right = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(((i * 2) + 1) * 2, 2));
            Assert.That(left, Is.EqualTo((short)(i * 5)), $"Left sample {i}");
            Assert.That(right, Is.EqualTo((short)(i * 3)), $"Right sample {i}");
        }
    }

    [TestCase(TestName = "FlacDecoder: Encode моно 8-bit (U8)")]
    public void EncodeMonoU8()
    {
        using var codec = new FlacDecoder();
        codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 22050,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.U8,
        });

        const int sampleCount = 32;
        Span<byte> input = stackalloc byte[sampleCount];

        for (var i = 0; i < sampleCount; i++)
            input[i] = 200; // constant U8 value

        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 22050,
            SampleFormat = AudioSampleFormat.U8,
        });

        Span<byte> output = stackalloc byte[4096];
        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Decode and verify
        using var decoder = new FlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 22050,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.U8,
        });

        Span<byte> pcm = stackalloc byte[sampleCount];
        var frame = new AudioFrame(pcm, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 22050,
            SampleFormat = AudioSampleFormat.U8,
        });

        var decResult = decoder.Decode(output[..written], ref frame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
            Assert.That(pcm[i], Is.EqualTo(200), $"Sample {i}");
    }

    [TestCase(TestName = "FlacDecoder: Encode capabilities включает Encode")]
    public void CapabilitiesIncludeEncode()
    {
        using var codec = new FlacDecoder();
        Assert.That(codec.Capabilities.HasFlag(CodecCapabilities.Encode), Is.True);
        Assert.That(codec.Capabilities.HasFlag(CodecCapabilities.Decode), Is.True);
        Assert.That(codec.Capabilities.HasFlag(CodecCapabilities.Lossless), Is.True);
    }

    [TestCase(TestName = "FlacDecoder: EncodeAsync моно 16-bit")]
    public async Task EncodeAsync()
    {
        using var codec = new FlacDecoder();
        codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 64;
        using var buffer = new AudioFrameBuffer();
        buffer.Allocate(sampleCount, channelCount: 1, sampleRate: 44100, format: AudioSampleFormat.S16);

        var frame = buffer.AsFrame();
        var data = frame.InterleavedData;

        for (var i = 0; i < sampleCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(data.Slice(i * 2, 2), (short)500);

        var output = new byte[4096];
        var (result, written) = await codec.EncodeAsync(buffer, output);
        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(written, Is.GreaterThan(0));
    }

    [TestCase(TestName = "FlacDecoder: InitializeEncoder невалидные параметры → InvalidData")]
    public void InitializeEncoderInvalid()
    {
        using var codec = new FlacDecoder();

        var result = codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 0,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "FlacDecoder: Encode негативные семплы моно 16-bit")]
    public void EncodeNegativeSamples()
    {
        using var codec = new FlacDecoder();
        codec.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 64;
        Span<byte> input = stackalloc byte[sampleCount * 2];

        for (var i = 0; i < sampleCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(input.Slice(i * 2, 2), (short)(-500 - i));

        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> output = stackalloc byte[8192];
        var result = codec.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Verify round-trip
        using var decoder = new FlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Span<byte> pcm = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(pcm, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var decResult = decoder.Decode(output[..written], ref frame);
        Assert.That(decResult, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2));
            Assert.That(decoded, Is.EqualTo((short)(-500 - i)), $"Sample {i}");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Строит FLAC фрейм с CONSTANT subframe.
    /// </summary>
    private static byte[] BuildConstantFrame(int blockSize, int channels, int bps, int channelAssignment, int sampleValue)
    {
        Span<byte> buf = stackalloc byte[256];
        var writer = new BitWriter(buf);

        WriteFrameHeader(ref writer, blockSize, channelAssignment, bps);

        for (var ch = 0; ch < channels; ch++)
        {
            var subframeBps = GetTestSubframeBps(bps, channelAssignment, ch);
            writer.WriteBits(0, 1); // padding
            writer.WriteBits(0, 6); // CONSTANT type
            writer.WriteBits(0, 1); // no wasted bits
            WriteSignedBits(ref writer, sampleValue, subframeBps);
        }

        writer.AlignToByte();
        writer.WriteBits(0, 16); // CRC-16 placeholder
        writer.Flush();

        return buf[..writer.BytesWritten].ToArray();
    }

    /// <summary>
    /// Строит FLAC фрейм с VERBATIM subframe.
    /// </summary>
    private static byte[] BuildVerbatimFrame(int blockSize, int channels, int bps, int channelAssignment, int[] samples)
    {
        var bufSize = 64 + (blockSize * channels * ((bps + 7) / 8)) + 64;
        var buf = new byte[bufSize];
        var writer = new BitWriter(buf);

        WriteFrameHeader(ref writer, blockSize, channelAssignment, bps);

        for (var ch = 0; ch < channels; ch++)
        {
            var subframeBps = GetTestSubframeBps(bps, channelAssignment, ch);
            writer.WriteBits(0, 1); // padding
            writer.WriteBits(1, 6); // VERBATIM type
            writer.WriteBits(0, 1); // no wasted bits

            for (var i = 0; i < blockSize; i++)
                WriteSignedBits(ref writer, samples[i], subframeBps);
        }

        writer.AlignToByte();
        writer.WriteBits(0, 16); // CRC-16
        writer.Flush();

        return buf[..writer.BytesWritten].ToArray();
    }

    /// <summary>
    /// Строит FLAC фрейм с FIXED subframe.
    /// В expected — ожидаемые выходные семплы, из них вычисляются residuals.
    /// </summary>
    private static byte[] BuildFixedFrame(int blockSize, int channels, int bps, int channelAssignment, int order, int[] expected)
    {
        // Compute residuals from expected output
        var residuals = new int[blockSize];

        for (var i = order; i < blockSize; i++)
        {
            residuals[i] = order switch
            {
                0 => expected[i],
                1 => expected[i] - expected[i - 1],
                2 => expected[i] - (2 * expected[i - 1]) + expected[i - 2],
                _ => expected[i] - expected[i - 1],
            };
        }

        var bufSize = 128 + (blockSize * channels * ((bps + 7) / 8)) + 128;
        var buf = new byte[bufSize];
        var writer = new BitWriter(buf);

        WriteFrameHeader(ref writer, blockSize, channelAssignment, bps);

        for (var ch = 0; ch < channels; ch++)
        {
            var subframeBps = GetTestSubframeBps(bps, channelAssignment, ch);
            writer.WriteBits(0, 1); // padding
            writer.WriteBits((uint)(8 + order), 6); // FIXED type (8 + order)
            writer.WriteBits(0, 1); // no wasted bits

            // Warmup samples
            for (var i = 0; i < order; i++)
                WriteSignedBits(ref writer, expected[i], subframeBps);

            // Residual: Rice method 0, partition order 0
            WriteRiceResidual(ref writer, residuals, blockSize, order);
        }

        writer.AlignToByte();
        writer.WriteBits(0, 16); // CRC-16
        writer.Flush();

        return buf[..writer.BytesWritten].ToArray();
    }

    /// <summary>
    /// Строит стерео FLAC фрейм с CONSTANT subframe для каждого канала (verbatim).
    /// channelAssignment: 0-7 = independent (channels = assignment+1).
    /// </summary>
    private static byte[] BuildStereoConstantFrame(int blockSize, int channelAssignment, int[] leftSamples, int[] rightSamples)
    {
        const int bps = 16;
        var bufSize = 64 + (blockSize * 2 * 2) + 64;
        var buf = new byte[bufSize];
        var writer = new BitWriter(buf);

        WriteFrameHeader(ref writer, blockSize, channelAssignment, bps);

        // Channel 0 (left) — verbatim
        writer.WriteBits(0, 1);
        writer.WriteBits(1, 6); // VERBATIM
        writer.WriteBits(0, 1);

        for (var i = 0; i < blockSize; i++)
            WriteSignedBits(ref writer, leftSamples[i], bps);

        // Channel 1 (right) — verbatim
        writer.WriteBits(0, 1);
        writer.WriteBits(1, 6); // VERBATIM
        writer.WriteBits(0, 1);

        for (var i = 0; i < blockSize; i++)
            WriteSignedBits(ref writer, rightSamples[i], bps);

        writer.AlignToByte();
        writer.WriteBits(0, 16); // CRC-16
        writer.Flush();

        return buf[..writer.BytesWritten].ToArray();
    }

    /// <summary>
    /// Строит стерео FLAC фрейм с VERBATIM subframe для decorrelation тестов.
    /// channelAssignment 8 = left/side, 9 = side/right, 10 = mid/side.
    /// </summary>
    private static byte[] BuildStereoVerbatimFrame(int blockSize, int channelAssignment, int[] ch0Samples, int[] ch1Samples)
    {
        const int bps = 16;
        var bufSize = 64 + (blockSize * 2 * 4) + 64;
        var buf = new byte[bufSize];
        var writer = new BitWriter(buf);

        WriteFrameHeader(ref writer, blockSize, channelAssignment, bps);

        // Channel 0 — verbatim
        var ch0Bps = GetTestSubframeBps(bps, channelAssignment, 0);
        writer.WriteBits(0, 1);
        writer.WriteBits(1, 6); // VERBATIM
        writer.WriteBits(0, 1);

        for (var i = 0; i < blockSize; i++)
            WriteSignedBits(ref writer, ch0Samples[i], ch0Bps);

        // Channel 1 — verbatim
        var ch1Bps = GetTestSubframeBps(bps, channelAssignment, 1);
        writer.WriteBits(0, 1);
        writer.WriteBits(1, 6); // VERBATIM
        writer.WriteBits(0, 1);

        for (var i = 0; i < blockSize; i++)
            WriteSignedBits(ref writer, ch1Samples[i], ch1Bps);

        writer.AlignToByte();
        writer.WriteBits(0, 16); // CRC-16
        writer.Flush();

        return buf[..writer.BytesWritten].ToArray();
    }

    private static void WriteFrameHeader(ref BitWriter writer, int blockSize, int channelAssignment, int bps)
    {
        // Sync: 14 bits (0x3FFE)
        writer.WriteBits(0x3FFE, 14);
        writer.WriteBits(0, 1); // reserved
        writer.WriteBits(0, 1); // blocking strategy (fixed)

        // Block size code
        var blockSizeCode = GetBlockSizeCode(blockSize);
        writer.WriteBits((uint)blockSizeCode, 4);

        // Sample rate code (44100 = 9)
        writer.WriteBits(9, 4);

        // Channel assignment
        writer.WriteBits((uint)channelAssignment, 4);

        // Sample size code
        var sampleSizeCode = bps switch
        {
            8 => 1,
            16 => 4,
            24 => 6,
            32 => 7,
            _ => 0,
        };
        writer.WriteBits((uint)sampleSizeCode, 3);
        writer.WriteBits(0, 1); // reserved

        // Frame number (UTF-8 coded, frame 0)
        writer.WriteBits(0, 8);

        // Extra block size bytes (for codes 6 and 7)
        if (blockSizeCode == 6)
            writer.WriteBits((uint)(blockSize - 1), 8);
        else if (blockSizeCode == 7)
            writer.WriteBits((uint)(blockSize - 1), 16);

        // CRC-8 placeholder
        writer.WriteBits(0, 8);
    }

    private static int GetBlockSizeCode(int blockSize) => blockSize switch
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
        <= 256 => 6, // 8-bit extra
        _ => 7, // 16-bit extra
    };

    private static int GetTestSubframeBps(int bps, int channelAssignment, int channel) =>
        channelAssignment switch
        {
            8 when channel == 1 => bps + 1,
            9 when channel == 0 => bps + 1,
            10 when channel == 1 => bps + 1,
            _ => bps,
        };

    private static void WriteSignedBits(ref BitWriter writer, int value, int bits)
    {
        var mask = (uint)((1L << bits) - 1);
        writer.WriteBits((uint)value & mask, bits);
    }

    private static void WriteRiceResidual(ref BitWriter writer, int[] residuals, int blockSize, int predictorOrder)
    {
        // Rice coding method 0, partition order 0 (1 partition)
        writer.WriteBits(0, 2); // method
        writer.WriteBits(0, 4); // partition order

        // Use escape code (raw bits) for simplicity — always works
        writer.WriteBits(15, 4); // escape: rice param = 15

        // Raw bits count (enough to hold any 16-bit sample)
        writer.WriteBits(16, 5);

        var count = blockSize - predictorOrder;

        for (var i = 0; i < count; i++)
            WriteSignedBits(ref writer, residuals[predictorOrder + i], 16);
    }

    /// <summary>
    /// Строит полный FLAC файл (fLaC magic + STREAMINFO + 1 фрейм с constant subframe).
    /// </summary>
    private static byte[] BuildFlacFile(int sampleRate, int channels, int bps, int blockSize, int sampleValue)
    {
        using var ms = new MemoryStream();

        // "fLaC" magic
        ms.Write("fLaC"u8);

        // STREAMINFO metadata block (last block)
        var streamInfo = BuildStreamInfoForRoundTrip(sampleRate, channels, bps, blockSize);
        ms.WriteByte(0x80); // last=1, type=0 (STREAMINFO)
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte((byte)streamInfo.Length);
        ms.Write(streamInfo);

        // Write one valid FLAC frame with constant subframe
        var frameData = BuildConstantFrame(blockSize, channels, bps, channels <= 1 ? 0 : 1, sampleValue);
        ms.Write(frameData);

        return ms.ToArray();
    }

    private static byte[] BuildStreamInfoForRoundTrip(int sampleRate, int channels, int bps, long totalSamples)
    {
        var info = new byte[34];

        // Block sizes
        info[0] = (byte)(4096 >> 8);
        info[1] = (byte)(4096 & 0xFF);
        info[2] = (byte)(4096 >> 8);
        info[3] = (byte)(4096 & 0xFF);

        // Sample rate (20 bits) | channels-1 (3 bits) | bps-1 (5 bits) | total samples upper 4 bits
        var channelsMinus1 = channels - 1;
        var bpsMinus1 = bps - 1;

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

    #endregion
}
