#pragma warning disable CA1861, MA0051

using System.Buffers.Binary;
using System.Numerics;
using Atom.IO;
using Atom.Media;

namespace Atom.Media.Tests;

/// <summary>
/// Тесты ALAC декодера.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class AlacDecoderTests
{
    #region Initialization

    [TestCase(TestName = "AlacDecoder: Создание декодера")]
    public void CreateDecoder()
    {
        using var decoder = new AlacDecoder();
        Assert.That(decoder.CodecId, Is.EqualTo(MediaCodecId.Alac));
        Assert.That(decoder.Name, Is.EqualTo("ALAC Audio"));
        Assert.That(decoder.Capabilities, Is.EqualTo(CodecCapabilities.Decode | CodecCapabilities.Lossless));
    }

    [TestCase(TestName = "AlacDecoder: Инициализация с валидными параметрами")]
    public void InitializeDecoderValid()
    {
        using var decoder = new AlacDecoder();
        var result = decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "AlacDecoder: Инициализация с magic cookie")]
    public void InitializeDecoderWithMagicCookie()
    {
        using var decoder = new AlacDecoder();
        var cookie = BuildMagicCookie(sampleRate: 44100, channels: 2, bitDepth: 16, frameLength: 4096);

        var result = decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
            ExtraData = cookie,
        });

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "AlacDecoder: Инициализация с невалидными параметрами → InvalidData")]
    public void InitializeDecoderInvalid()
    {
        using var decoder = new AlacDecoder();
        var result = decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 0,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "AlacDecoder: Encode возвращает UnsupportedFormat")]
    public void EncodeUnsupported()
    {
        using var decoder = new AlacDecoder();
        var result = decoder.InitializeEncoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(result, Is.EqualTo(CodecResult.UnsupportedFormat));
    }

    #endregion

    #region Error Handling

    [TestCase(TestName = "AlacDecoder: Decode без инициализации → NotInitialized")]
    public void DecodeWithoutInit()
    {
        using var decoder = new AlacDecoder();
        Span<byte> buf = stackalloc byte[4096];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = 256,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode([0x00, 0x00, 0x00, 0x00], ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.NotInitialized));
    }

    [TestCase(TestName = "AlacDecoder: Decode с коротким пакетом → NeedMoreData")]
    public void DecodeShortPacket()
    {
        using var decoder = new AlacDecoder();
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

        var result = decoder.Decode([0x00, 0x00], ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.NeedMoreData));
    }

    [TestCase(TestName = "AlacDecoder: Decode после Dispose → ObjectDisposedException")]
    public void DecodeAfterDispose()
    {
        var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });
        decoder.Dispose();

        var ex = false;

        try
        {
            Span<byte> buf = stackalloc byte[4096];
            var frame = new AudioFrame(buf, new AudioFrameInfo
            {
                SampleCount = 256,
                ChannelCount = 1,
                SampleRate = 44100,
                SampleFormat = AudioSampleFormat.S16,
            });
            decoder.Decode([0x00, 0x00, 0x00, 0x00], ref frame);
        }
        catch (ObjectDisposedException)
        {
            ex = true;
        }

        Assert.That(ex, Is.True);
    }

    [TestCase(TestName = "AlacDecoder: Decode невалидный тег → InvalidData")]
    public void DecodeInvalidTag()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        // tag=3 (invalid), rest doesn't matter
        var packet = BuildAlacFrame(tag: 3, sampleCount: 16, channelCount: 1, bitDepth: 16, uncompressed: true, samples: [0]);
        Span<byte> buf = stackalloc byte[4096];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = 16,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    #endregion

    #region Flush, Reset

    [TestCase(TestName = "AlacDecoder: Flush → EndOfStream")]
    public void FlushReturnsEndOfStream()
    {
        using var decoder = new AlacDecoder();
        Span<byte> buf = stackalloc byte[64];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = 1,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        Assert.That(decoder.Flush(ref frame), Is.EqualTo(CodecResult.EndOfStream));
    }

    [TestCase(TestName = "AlacDecoder: Reset не бросает исключений")]
    public void ResetDoesNotThrow()
    {
        using var decoder = new AlacDecoder();
        Assert.DoesNotThrow(() => decoder.Reset());
    }

    #endregion

    #region Uncompressed Decoding

    [TestCase(TestName = "AlacDecoder: Декодирование несжатого моно фрейма 16-bit")]
    public void DecodeUncompressedMono16()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 16;
        var samples = new int[sampleCount];

        for (var i = 0; i < sampleCount; i++)
            samples[i] = (i * 100) - 800;

        var packet = BuildAlacFrame(tag: 0, sampleCount: sampleCount, channelCount: 1,
            bitDepth: 16, uncompressed: true, samples: samples, hasSize: true);

        Span<byte> buf = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(i * 2, 2));
            Assert.That(decoded, Is.EqualTo((short)samples[i]), $"Sample {i}");
        }
    }

    [TestCase(TestName = "AlacDecoder: Декодирование несжатого стерео фрейма 16-bit")]
    public void DecodeUncompressedStereo16()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 2,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 8;
        var left = new int[sampleCount];
        var right = new int[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            left[i] = i * 50;
            right[i] = -i * 30;
        }

        var packet = BuildAlacStereoFrame(tag: 1, sampleCount: sampleCount,
            bitDepth: 16, uncompressed: true, left: left, right: right, hasSize: true);

        Span<byte> buf = stackalloc byte[sampleCount * 2 * 2];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 2,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var l = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice((i * 2) * 2, 2));
            var r = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(((i * 2) + 1) * 2, 2));
            Assert.That(l, Is.EqualTo((short)left[i]), $"Left sample {i}");
            Assert.That(r, Is.EqualTo((short)right[i]), $"Right sample {i}");
        }
    }

    [TestCase(TestName = "AlacDecoder: Декодирование несжатого фрейма с hasSize")]
    public void DecodeUncompressedWithHasSize()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 4;
        var samples = new int[] { 100, -200, 300, -400 };

        var packet = BuildAlacFrame(tag: 0, sampleCount: sampleCount, channelCount: 1,
            bitDepth: 16, uncompressed: true, samples: samples, hasSize: true);

        Span<byte> buf = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(i * 2, 2));
            Assert.That(decoded, Is.EqualTo((short)samples[i]), $"Sample {i}");
        }
    }

    #endregion

    #region Compressed Decoding

    [TestCase(TestName = "AlacDecoder: Декодирование сжатого моно фрейма (all zeros)")]
    public void DecodeCompressedMonoAllZeros()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 16;
        var packet = BuildCompressedMonoFrame(sampleCount: sampleCount, bitDepth: 16,
            residuals: new int[sampleCount], numCoeffs: 0, denShift: 0, coefficients: []);

        Span<byte> buf = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(i * 2, 2));
            Assert.That(decoded, Is.Zero, $"Sample {i}");
        }
    }

    [TestCase(TestName = "AlacDecoder: Декодирование сжатого моно фрейма (constant value)")]
    public void DecodeCompressedMonoConstant()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 8;
        // Without LPC: residuals ARE the samples (no prediction)
        var residuals = new[] { 500, 0, 0, 0, 0, 0, 0, 0 };

        var packet = BuildCompressedMonoFrame(sampleCount: sampleCount, bitDepth: 16,
            residuals: residuals, numCoeffs: 0, denShift: 0, coefficients: []);

        Span<byte> buf = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Sample 0 = 500, rest = 0 (no prediction applied)
        var s0 = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(0, 2));
        Assert.That(s0, Is.EqualTo(500));

        for (var i = 1; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(i * 2, 2));
            Assert.That(decoded, Is.Zero, $"Sample {i}");
        }
    }

    [TestCase(TestName = "AlacDecoder: Декодирование сжатого фрейма с негативными значениями")]
    public void DecodeCompressedNegativeResiduals()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 4;
        var residuals = new[] { -100, 50, -25, 10 };

        var packet = BuildCompressedMonoFrame(sampleCount: sampleCount, bitDepth: 16,
            residuals: residuals, numCoeffs: 0, denShift: 0, coefficients: []);

        Span<byte> buf = stackalloc byte[sampleCount * 2];
        var frame = new AudioFrame(buf, new AudioFrameInfo
        {
            SampleCount = sampleCount,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Decode(packet, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        for (var i = 0; i < sampleCount; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(i * 2, 2));
            Assert.That(decoded, Is.EqualTo((short)residuals[i]), $"Sample {i}");
        }
    }

    #endregion

    #region Async

    [TestCase(TestName = "AlacDecoder: DecodeAsync несжатый фрейм")]
    public async Task DecodeAsyncUncompressed()
    {
        using var decoder = new AlacDecoder();
        decoder.InitializeDecoder(new AudioCodecParameters
        {
            SampleRate = 44100,
            ChannelCount = 1,
            SampleFormat = AudioSampleFormat.S16,
        });

        const int sampleCount = 8;
        var samples = new int[sampleCount];

        for (var i = 0; i < sampleCount; i++)
            samples[i] = i * 10;

        var packet = BuildAlacFrame(tag: 0, sampleCount: sampleCount, channelCount: 1,
            bitDepth: 16, uncompressed: true, samples: samples, hasSize: true);

        using var buffer = new AudioFrameBuffer();
        buffer.Allocate(sampleCount, channelCount: 1, sampleRate: 44100, format: AudioSampleFormat.S16);

        var result = await decoder.DecodeAsync(packet, buffer);
        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    #endregion

    #region Registry

    [TestCase(TestName = "AlacDecoder: Зарегистрирован в CodecRegistry")]
    public void CodecRegistered()
    {
        Assert.That(CodecRegistry.IsAudioCodecRegistered(MediaCodecId.Alac), Is.True);

        using var codec = CodecRegistry.CreateAudioCodec(MediaCodecId.Alac);
        Assert.That(codec, Is.Not.Null);
        Assert.That(codec, Is.InstanceOf<AlacDecoder>());
    }

    #endregion

    #region Encode

    [TestCase(TestName = "AlacDecoder: Encode возвращает UnsupportedFormat")]
    public void EncodeReturnsUnsupported()
    {
        using var decoder = new AlacDecoder();

        Span<byte> output = stackalloc byte[1024];
        Span<byte> input = stackalloc byte[128];
        var roFrame = new ReadOnlyAudioFrame(input, new AudioFrameInfo
        {
            SampleCount = 32,
            ChannelCount = 1,
            SampleRate = 44100,
            SampleFormat = AudioSampleFormat.S16,
        });

        var result = decoder.Encode(roFrame, output, out var written);
        Assert.That(result, Is.EqualTo(CodecResult.UnsupportedFormat));
        Assert.That(written, Is.Zero);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Строит magic cookie (ALACSpecificConfig) — 24 байта.
    /// </summary>
    private static byte[] BuildMagicCookie(int sampleRate, int channels, int bitDepth, int frameLength)
    {
        var cookie = new byte[24];
        BinaryPrimitives.WriteInt32BigEndian(cookie, frameLength);
        cookie[4] = 0; // compatible version
        cookie[5] = (byte)bitDepth;
        cookie[6] = 40; // rice_historymult
        cookie[7] = 10; // rice_initialhistory
        cookie[8] = 14; // rice_kmodifier
        cookie[9] = (byte)channels;
        BinaryPrimitives.WriteUInt16BigEndian(cookie.AsSpan(10, 2), 255); // maxRun
        BinaryPrimitives.WriteInt32BigEndian(cookie.AsSpan(16, 4), 0); // avgBitRate
        BinaryPrimitives.WriteInt32BigEndian(cookie.AsSpan(20, 4), sampleRate);
        return cookie;
    }

    /// <summary>
    /// Строит несжатый ALAC фрейм (моно) — uncompressed=1, samples в big-endian.
    /// </summary>
    private static byte[] BuildAlacFrame(int tag, int sampleCount, int channelCount,
        int bitDepth, bool uncompressed, int[] samples, bool hasSize = false)
    {
        var buf = new byte[2048];
        var writer = new BitWriter(buf);

        // Element header
        writer.WriteBits((uint)tag, 3);
        writer.WriteBits(0, 4); // element instance tag
        writer.WriteBits(0, 12); // unused

        // Header byte
        writer.WriteBits(0, 1); // unused
        writer.WriteBits(hasSize ? 1U : 0U, 1);
        writer.WriteBits(uncompressed ? 1U : 0U, 1);
        writer.WriteBits(0, 1); // unused

        if (hasSize)
            writer.WriteBits((uint)sampleCount, 32);

        if (uncompressed)
        {
            writer.WriteBits(0, 16); // shiftBits

            // Write samples (interleaved for multi-channel)
            for (var i = 0; i < sampleCount; i++)
            {
                for (var ch = 0; ch < channelCount; ch++)
                {
                    var sample = samples[Math.Min(i, samples.Length - 1)];
                    WriteSigned(ref writer, sample, bitDepth);
                }
            }
        }

        writer.AlignToByte();
        // End tag
        writer.WriteBits(7, 3); // ID_END
        writer.WriteBits(0, 5); // padding
        writer.Flush();

        var totalBytes = writer.BytesWritten;
        var result = new byte[totalBytes];
        Array.Copy(buf, result, totalBytes);
        return result;
    }

    /// <summary>
    /// Строит несжатый ALAC стерео фрейм.
    /// </summary>
    private static byte[] BuildAlacStereoFrame(int tag, int sampleCount,
        int bitDepth, bool uncompressed, int[] left, int[] right, bool hasSize = false)
    {
        var buf = new byte[4096];
        var writer = new BitWriter(buf);

        writer.WriteBits((uint)tag, 3);
        writer.WriteBits(0, 4);
        writer.WriteBits(0, 12);

        writer.WriteBits(0, 1);
        writer.WriteBits(hasSize ? 1U : 0U, 1);
        writer.WriteBits(uncompressed ? 1U : 0U, 1);
        writer.WriteBits(0, 1);

        if (hasSize)
            writer.WriteBits((uint)sampleCount, 32);

        if (uncompressed)
        {
            writer.WriteBits(0, 16); // shiftBits

            for (var i = 0; i < sampleCount; i++)
            {
                WriteSigned(ref writer, left[i], bitDepth);
                WriteSigned(ref writer, right[i], bitDepth);
            }
        }

        writer.AlignToByte();
        writer.WriteBits(7, 3);
        writer.WriteBits(0, 5);
        writer.Flush();

        var totalBytes = writer.BytesWritten;
        var result = new byte[totalBytes];
        Array.Copy(buf, result, totalBytes);
        return result;
    }

    /// <summary>
    /// Строит сжатый ALAC моно фрейм с Rice-кодированными residuals.
    /// </summary>
    private static byte[] BuildCompressedMonoFrame(int sampleCount, int bitDepth,
        int[] residuals, int numCoeffs, int denShift, int[] coefficients)
    {
        var buf = new byte[8192];
        var writer = new BitWriter(buf);

        // SCE header
        writer.WriteBits(0, 3); // tag = SCE
        writer.WriteBits(0, 4);
        writer.WriteBits(0, 12);

        // Header byte: hasSize=1, uncompressed=0
        writer.WriteBits(0, 1);
        writer.WriteBits(1, 1); // hasSize = true
        writer.WriteBits(0, 1); // compressed
        writer.WriteBits(0, 1);
        writer.WriteBits((uint)sampleCount, 32);

        // Prediction params for 1 channel
        writer.WriteBits(0, 4); // mode
        writer.WriteBits((uint)denShift, 5);
        writer.WriteBits(0, 3); // pbFactor
        writer.WriteBits((uint)numCoeffs, 5);

        for (var j = 0; j < numCoeffs; j++)
            WriteSigned(ref writer, coefficients[j], 16);

        writer.WriteBits(0, 16); // shiftBits

        // Rice-encode residuals using adaptive algorithm matching the decoder
        WriteRiceResiduals(ref writer, residuals, sampleCount, bitDepth);

        writer.AlignToByte();
        writer.WriteBits(7, 3);
        writer.WriteBits(0, 5);
        writer.Flush();

        var totalBytes = writer.BytesWritten;
        var result = new byte[totalBytes];
        Array.Copy(buf, result, totalBytes);
        return result;
    }

    /// <summary>
    /// Кодирует residuals адаптивным ALAC Rice, в точности повторяя логику декодера.
    /// </summary>
    private static void WriteRiceResiduals(ref BitWriter writer, int[] residuals, int count, int bitDepth,
        int initialHistory = 10, int historyMult = 40, int kModifier = 14)
    {
        const int riceLimit = 9;
        var history = initialHistory;
        var signModifier = 0;
        var i = 0;

        while (i < count)
        {
            var log = 31 - BitOperations.LeadingZeroCount((uint)((history >> 9) + 3));
            var k = Math.Min(log, kModifier);

            var zigzag = residuals[i] >= 0 ? 2 * residuals[i] : (-2 * residuals[i]) - 1;
            var riceValue = zigzag - signModifier;
            signModifier = 0;

            WriteRiceValue(ref writer, riceValue, k, riceLimit, bitDepth);

            history = zigzag > 0xFFFF
                ? 0xFFFF
                : history + ((zigzag * historyMult) - ((history * historyMult) >> 9));

            i++;

            if (history < 128 && i < count)
            {
                var kz = Math.Min(7, BitOperations.LeadingZeroCount((uint)history) - 24);

                if (kz < 0)
                    kz = 0;

                var runStart = i;

                while (i < count && residuals[i] == 0)
                    i++;

                var run = i - runStart;
                WriteRiceValue(ref writer, run, kz, riceLimit, 16);

                if (run < 0xFFFF)
                    signModifier = 1;

                history = 0;
            }
        }
    }

    private static void WriteRiceValue(ref BitWriter writer, int value, int k, int riceLimit, int maxBits)
    {
        var quotient = value >> k;

        if (k > 0)
            writer.WriteBits((uint)(value & ((1 << k) - 1)), k);

        if (quotient >= riceLimit)
        {
            for (var j = 0; j < riceLimit; j++)
                writer.WriteBit(bit: false);

            writer.WriteBit(bit: true);
            writer.WriteBits((uint)value & ((1U << maxBits) - 1), maxBits);
        }
        else
        {
            for (var j = 0; j < quotient; j++)
                writer.WriteBit(bit: false);

            writer.WriteBit(bit: true);
        }
    }

    private static void WriteSigned(ref BitWriter writer, int value, int bits)
    {
        if (bits >= 32)
        {
            writer.WriteBits((uint)value, 32);
            return;
        }

        writer.WriteBits((uint)value & ((1U << bits) - 1), bits);
    }

    #endregion
}
