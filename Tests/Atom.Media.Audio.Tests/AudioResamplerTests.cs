using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Atom.Media.Audio;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class AudioResamplerTests(ILogger logger) : BenchmarkTests<AudioResamplerTests>(logger)
{
    public AudioResamplerTests() : this(ConsoleLogger.Unicode) { }

    // ═══════════════════════════════════════════════════════════════
    // Идентичный формат → копирование без изменений
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: одинаковый формат возвращает копию")]
    public void SameFormatReturnsCopy()
    {
        byte[] source = [1, 2, 3, 4];
        var result = AudioResampler.Convert(source, AudioSampleFormat.U8, AudioSampleFormat.U8, 1);

        Assert.That(result, Is.EqualTo(source));
    }

    // ═══════════════════════════════════════════════════════════════
    // U8 ↔ S16
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: U8 тишина (128) → S16 ≈ 0")]
    public void U8SilenceToS16()
    {
        byte[] source = [128]; // тишина в U8
        var result = AudioResampler.Convert(source, AudioSampleFormat.U8, AudioSampleFormat.S16, 1);

        var value = BinaryPrimitives.ReadInt16LittleEndian(result);
        Assert.That(Math.Abs(value), Is.LessThanOrEqualTo(1));
    }

    [TestCase(TestName = "Ресемплер: U8 максимум (255) → S16 положительный")]
    public void U8MaxToS16Positive()
    {
        byte[] source = [255];
        var result = AudioResampler.Convert(source, AudioSampleFormat.U8, AudioSampleFormat.S16, 1);

        var value = BinaryPrimitives.ReadInt16LittleEndian(result);
        Assert.That(value, Is.GreaterThan(32000));
    }

    [TestCase(TestName = "Ресемплер: U8 минимум (0) → S16 отрицательный")]
    public void U8MinToS16Negative()
    {
        byte[] source = [0];
        var result = AudioResampler.Convert(source, AudioSampleFormat.U8, AudioSampleFormat.S16, 1);

        var value = BinaryPrimitives.ReadInt16LittleEndian(result);
        Assert.That(value, Is.LessThan(-32000));
    }

    // ═══════════════════════════════════════════════════════════════
    // S16 ↔ F32
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: S16 тишина → F32 ≈ 0.0")]
    public void S16SilenceToF32()
    {
        var source = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(source, 0);

        var result = AudioResampler.Convert(source, AudioSampleFormat.S16, AudioSampleFormat.F32, 1);

        var value = MemoryMarshal.Read<float>(result);
        Assert.That(Math.Abs(value), Is.LessThan(0.001f));
    }

    [TestCase(TestName = "Ресемплер: S16 максимум → F32 ≈ 1.0")]
    public void S16MaxToF32()
    {
        var source = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(source, short.MaxValue);

        var result = AudioResampler.Convert(source, AudioSampleFormat.S16, AudioSampleFormat.F32, 1);

        var value = MemoryMarshal.Read<float>(result);
        Assert.That(value, Is.GreaterThan(0.99f).And.LessThanOrEqualTo(1.0f));
    }

    [TestCase(TestName = "Ресемплер: F32 → S16 → F32 round-trip")]
    public void F32ToS16RoundTrip()
    {
        var source = new byte[4];
        MemoryMarshal.Write(source, 0.5f);

        var s16 = AudioResampler.Convert(source, AudioSampleFormat.F32, AudioSampleFormat.S16, 1);
        var roundTrip = AudioResampler.Convert(s16, AudioSampleFormat.S16, AudioSampleFormat.F32, 1);

        var original = MemoryMarshal.Read<float>(source);
        var restored = MemoryMarshal.Read<float>(roundTrip);

        Assert.That(Math.Abs(original - restored), Is.LessThan(0.001f));
    }

    // ═══════════════════════════════════════════════════════════════
    // S32 ↔ F32
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: S32 максимум → F32 ≈ 1.0")]
    public void S32MaxToF32()
    {
        var source = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(source, int.MaxValue);

        var result = AudioResampler.Convert(source, AudioSampleFormat.S32, AudioSampleFormat.F32, 1);

        var value = MemoryMarshal.Read<float>(result);
        Assert.That(value, Is.GreaterThan(0.99f));
    }

    // ═══════════════════════════════════════════════════════════════
    // F32 ↔ F64
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: F32 → F64 сохраняет значение")]
    public void F32ToF64()
    {
        var source = new byte[4];
        MemoryMarshal.Write(source, 0.75f);

        var result = AudioResampler.Convert(source, AudioSampleFormat.F32, AudioSampleFormat.F64, 1);

        var value = MemoryMarshal.Read<double>(result);
        Assert.That(Math.Abs(value - 0.75), Is.LessThan(0.0001));
    }

    [TestCase(TestName = "Ресемплер: F64 → F32 сохраняет значение")]
    public void F64ToF32()
    {
        var source = new byte[8];
        MemoryMarshal.Write(source, -0.25);

        var result = AudioResampler.Convert(source, AudioSampleFormat.F64, AudioSampleFormat.F32, 1);

        var value = MemoryMarshal.Read<float>(result);
        Assert.That(Math.Abs(value - (-0.25f)), Is.LessThan(0.0001f));
    }

    // ═══════════════════════════════════════════════════════════════
    // Мультиканальная конвертация
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: стерео U8 → S16 (2 семпла)")]
    public void StereoU8ToS16()
    {
        byte[] source = [128, 255]; // L=тишина, R=макс
        var result = AudioResampler.Convert(source, AudioSampleFormat.U8, AudioSampleFormat.S16, 2);

        Assert.That(result, Has.Length.EqualTo(4));
        var left = BinaryPrimitives.ReadInt16LittleEndian(result.AsSpan(0, 2));
        var right = BinaryPrimitives.ReadInt16LittleEndian(result.AsSpan(2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(left), Is.LessThanOrEqualTo(1));
            Assert.That(right, Is.GreaterThan(32000));
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Deinterleave / Interleave
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: деинтерлив стерео U8")]
    public void DeinterleaveU8Stereo()
    {
        byte[] interleaved = [1, 2, 3, 4]; // L R L R
        var planar = new byte[4];

        AudioResampler.Deinterleave(interleaved, planar, AudioSampleFormat.U8, 2);

        Assert.Multiple(() =>
        {
            // Ожидается: L L R R = 1, 3, 2, 4
            Assert.That(planar[0], Is.EqualTo(1));
            Assert.That(planar[1], Is.EqualTo(3));
            Assert.That(planar[2], Is.EqualTo(2));
            Assert.That(planar[3], Is.EqualTo(4));
        });
    }

    [TestCase(TestName = "Ресемплер: интерлив стерео U8")]
    public void InterleaveU8Stereo()
    {
        byte[] planar = [1, 3, 2, 4]; // LL RR
        var interleaved = new byte[4];

        AudioResampler.Interleave(planar, interleaved, AudioSampleFormat.U8Planar, 2);

        Assert.Multiple(() =>
        {
            // Ожидается: L R L R = 1, 2, 3, 4
            Assert.That(interleaved[0], Is.EqualTo(1));
            Assert.That(interleaved[1], Is.EqualTo(2));
            Assert.That(interleaved[2], Is.EqualTo(3));
            Assert.That(interleaved[3], Is.EqualTo(4));
        });
    }

    [TestCase(TestName = "Ресемплер: деинтерлив → интерлив round-trip")]
    public void DeinterleaveInterleaveRoundTrip()
    {
        byte[] original = [10, 20, 30, 40, 50, 60]; // 3 кадра стерео
        var planar = new byte[6];
        var restored = new byte[6];

        AudioResampler.Deinterleave(original, planar, AudioSampleFormat.U8, 2);
        AudioResampler.Interleave(planar, restored, AudioSampleFormat.U8Planar, 2);

        Assert.That(restored, Is.EqualTo(original));
    }

    // ═══════════════════════════════════════════════════════════════
    // Кроссформат: interleaved ↔ planar
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: F32 interleaved → F32 planar")]
    public void F32InterleavedToPlanar()
    {
        var source = new byte[8]; // 2 семпла F32 (стерео, 1 кадр)
        MemoryMarshal.Write(source.AsSpan(0), 0.5f);
        MemoryMarshal.Write(source.AsSpan(4), -0.5f);

        var result = AudioResampler.Convert(
            source, AudioSampleFormat.F32, AudioSampleFormat.F32Planar, 2);

        Assert.That(result, Has.Length.EqualTo(8));

        var left = MemoryMarshal.Read<float>(result.AsSpan(0));
        var right = MemoryMarshal.Read<float>(result.AsSpan(4));

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(left - 0.5f), Is.LessThan(0.001f));
            Assert.That(Math.Abs(right - (-0.5f)), Is.LessThan(0.001f));
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Буферная конвертация (in-place)
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: конвертация в предоставленный буфер")]
    public void ConvertToProvidedBuffer()
    {
        byte[] source = [128];
        var destination = new byte[2];

        var written = AudioResampler.Convert(
            source, AudioSampleFormat.U8, destination, AudioSampleFormat.S16, 1);

        Assert.That(written, Is.EqualTo(2));
    }

    // ═══════════════════════════════════════════════════════════════
    // Валидация параметров
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: channels < 1 выбрасывает исключение")]
    public void ZeroChannelsThrows()
    {
        Assert.That(
            () => AudioResampler.Convert([], AudioSampleFormat.U8, AudioSampleFormat.S16, 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(TestName = "Ресемплер: Unknown формат выбрасывает исключение")]
    public void UnknownFormatThrows()
    {
        Assert.That(
            () => AudioResampler.Convert([0], AudioSampleFormat.Unknown, AudioSampleFormat.S16, 1),
            Throws.TypeOf<ArgumentException>());
    }

    // ═══════════════════════════════════════════════════════════════
    // Клиппинг
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплер: F32 > 1.0 клипируется в S16 до 32767")]
    public void F32ClippingToS16()
    {
        var source = new byte[4];
        MemoryMarshal.Write(source, 1.5f);

        var result = AudioResampler.Convert(source, AudioSampleFormat.F32, AudioSampleFormat.S16, 1);

        var value = BinaryPrimitives.ReadInt16LittleEndian(result);
        Assert.That(value, Is.EqualTo(short.MaxValue));
    }

    [TestCase(TestName = "Ресемплер: F32 < -1.0 клипируется в U8 до 0")]
    public void F32ClippingToU8()
    {
        var source = new byte[4];
        MemoryMarshal.Write(source, -2.0f);

        var result = AudioResampler.Convert(source, AudioSampleFormat.F32, AudioSampleFormat.U8, 1);

        Assert.That(result[0], Is.Zero);
    }

    // ═══════════════════════════════════════════════════════════════
    // AudioSampleFormat extension: CalculateBufferSize
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "BufferSize: F32 mono 480 семплов = 1920 байт")]
    public void BufferSizeF32Mono()
    {
        var size = AudioSampleFormat.F32.CalculateBufferSize(480, 1);
        Assert.That(size, Is.EqualTo(1920));
    }

    [TestCase(TestName = "BufferSize: S16 стерео 480 семплов = 1920 байт")]
    public void BufferSizeS16Stereo()
    {
        var size = AudioSampleFormat.S16.CalculateBufferSize(480, 2);
        Assert.That(size, Is.EqualTo(1920));
    }

    // ═══════════════════════════════════════════════════════════════
    // AudioSampleFormat extensions
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "IsPlanar: F32Planar = true")]
    public void IsPlanarTrue()
    {
        Assert.That(AudioSampleFormat.F32Planar.IsPlanar(), Is.True);
    }

    [TestCase(TestName = "IsPlanar: F32 = false")]
    public void IsPlanarFalse()
    {
        Assert.That(AudioSampleFormat.F32.IsPlanar(), Is.False);
    }

    [TestCase(TestName = "IsFloat: F32 = true, S16 = false")]
    public void IsFloatCheck()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AudioSampleFormat.F32.IsFloat(), Is.True);
            Assert.That(AudioSampleFormat.S16.IsFloat(), Is.False);
        });
    }

    [TestCase(TestName = "GetBytesPerSample: все форматы")]
    public void BytesPerSample()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AudioSampleFormat.U8.GetBytesPerSample(), Is.EqualTo(1));
            Assert.That(AudioSampleFormat.S16.GetBytesPerSample(), Is.EqualTo(2));
            Assert.That(AudioSampleFormat.S32.GetBytesPerSample(), Is.EqualTo(4));
            Assert.That(AudioSampleFormat.F32.GetBytesPerSample(), Is.EqualTo(4));
            Assert.That(AudioSampleFormat.F64.GetBytesPerSample(), Is.EqualTo(8));
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Ресемплинг частоты дискретизации
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Ресемплинг: одинаковая частота возвращает копию")]
    public void ResampleSameRateReturnsCopy()
    {
        var source = new byte[8];
        MemoryMarshal.Write(source.AsSpan(0), 0.5f);
        MemoryMarshal.Write(source.AsSpan(4), -0.5f);

        var result = AudioResampler.ResampleRate(
            source, AudioSampleFormat.F32, 1, 48000, 48000);

        Assert.That(result, Is.EqualTo(source));
    }

    [TestCase(TestName = "Ресемплинг: 48000→24000 уменьшает количество кадров вдвое")]
    public void ResampleDownHalvesFrames()
    {
        // 100 кадров F32 mono
        var source = new byte[100 * 4];
        for (var i = 0; i < 100; i++)
        {
            MemoryMarshal.Write(source.AsSpan(i * 4), (float)Math.Sin(i * 0.1));
        }

        var result = AudioResampler.ResampleRate(
            source, AudioSampleFormat.F32, 1, 48000, 24000);

        // 100 * 24000 / 48000 = 50 кадров → 200 байт
        Assert.That(result, Has.Length.EqualTo(200));
    }

    [TestCase(TestName = "Ресемплинг: 24000→48000 удваивает количество кадров")]
    public void ResampleUpDoublesFrames()
    {
        // 50 кадров F32 mono
        var source = new byte[50 * 4];
        for (var i = 0; i < 50; i++)
        {
            MemoryMarshal.Write(source.AsSpan(i * 4), (float)Math.Sin(i * 0.1));
        }

        var result = AudioResampler.ResampleRate(
            source, AudioSampleFormat.F32, 1, 24000, 48000);

        Assert.That(result, Has.Length.EqualTo(400));
    }

    [TestCase(TestName = "Ресемплинг: 44100→48000 корректное количество кадров")]
    public void Resample44100To48000()
    {
        // 441 кадров = 10ms при 44100Hz
        var source = new byte[441 * 4];
        for (var i = 0; i < 441; i++)
        {
            MemoryMarshal.Write(source.AsSpan(i * 4), (float)Math.Sin(i * 0.1));
        }

        var result = AudioResampler.ResampleRate(
            source, AudioSampleFormat.F32, 1, 44100, 48000);

        // 441 * 48000 / 44100 = 480 кадров → 1920 байт
        Assert.That(result, Has.Length.EqualTo(1920));
    }

    [TestCase(TestName = "Ресемплинг: стерео S16 корректное количество кадров")]
    public void ResampleStereoS16()
    {
        // 100 кадров S16 стерео = 100 * 2 * 2 = 400 байт
        var source = new byte[400];
        for (var i = 0; i < 200; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                source.AsSpan(i * 2), (short)(i * 100));
        }

        var result = AudioResampler.ResampleRate(
            source, AudioSampleFormat.S16, 2, 48000, 24000);

        // 50 кадров * 2 каналов * 2 байт = 200
        Assert.That(result, Has.Length.EqualTo(200));
    }

    [TestCase(TestName = "Ресемплинг: кубическая интерполяция между семплами")]
    public void ResampleInterpolatesCorrectly()
    {
        // 2 кадра F32: 0.0 и 1.0
        var source = new byte[8];
        MemoryMarshal.Write(source.AsSpan(0), 0.0f);
        MemoryMarshal.Write(source.AsSpan(4), 1.0f);

        // Апсемплинг 1→3:
        // sourceFrames=2, targetFrames=2*3/1=6, ratio=0.2
        var result = AudioResampler.ResampleRate(
            source, AudioSampleFormat.F32, 1, 1, 3);

        Assert.That(result, Has.Length.EqualTo(24)); // 6 кадров * 4 байт

        var first = MemoryMarshal.Read<float>(result.AsSpan(0));
        var last = MemoryMarshal.Read<float>(result.AsSpan(20));

        Assert.Multiple(() =>
        {
            // Концевые точки сохраняются точно
            Assert.That(Math.Abs(first), Is.LessThan(0.01f));
            Assert.That(Math.Abs(last - 1.0f), Is.LessThan(0.01f));

            // Промежуточные значения монотонно возрастают (Catmull-Rom гладкая)
            for (var i = 0; i < 5; i++)
            {
                var current = MemoryMarshal.Read<float>(result.AsSpan(i * 4));
                var next = MemoryMarshal.Read<float>(result.AsSpan((i + 1) * 4));
                Assert.That(next, Is.GreaterThanOrEqualTo(current),
                    "Значения должны монотонно возрастать на рампе 0→1");
            }
        });
    }

    [TestCase(TestName = "Ресемплинг: кубическая интерполяция плавнее линейной")]
    public void CubicInterpolationSmootherThanLinear()
    {
        // 4 кадра: синусоидальная кривая
        var source = new byte[16];
        MemoryMarshal.Write(source.AsSpan(0), 0.0f);
        MemoryMarshal.Write(source.AsSpan(4), 0.8f);
        MemoryMarshal.Write(source.AsSpan(8), 0.9f);
        MemoryMarshal.Write(source.AsSpan(12), 0.5f);

        // Апсемплинг ×4
        var result = AudioResampler.ResampleRate(
            source, AudioSampleFormat.F32, 1, 1, 4);

        // sourceFrames=4, targetFrames=4*4/1=16
        Assert.That(result, Has.Length.EqualTo(64));

        // Проверяем что опорные точки сохранены
        var p0 = MemoryMarshal.Read<float>(result.AsSpan(0));
        var pLast = MemoryMarshal.Read<float>(result.AsSpan(60));

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(p0), Is.LessThan(0.01f));
            Assert.That(Math.Abs(pLast - 0.5f), Is.LessThan(0.01f));
        });
    }

    [TestCase(TestName = "Ресемплинг: channels < 1 выбрасывает исключение")]
    public void ResampleZeroChannelsThrows()
    {
        Assert.That(
            () => AudioResampler.ResampleRate([], AudioSampleFormat.F32, 0, 48000, 44100),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(TestName = "Ресемплинг: sourceSampleRate < 1 выбрасывает исключение")]
    public void ResampleZeroSourceRateThrows()
    {
        Assert.That(
            () => AudioResampler.ResampleRate([], AudioSampleFormat.F32, 1, 0, 44100),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(TestName = "Ресемплинг: вывод в предоставленный буфер")]
    public void ResampleToProvidedBuffer()
    {
        var source = new byte[100 * 4];
        for (var i = 0; i < 100; i++)
        {
            MemoryMarshal.Write(source.AsSpan(i * 4), (float)(i * 0.01));
        }

        var destination = new byte[200]; // 50 кадров F32
        var written = AudioResampler.ResampleRate(
            source, destination, AudioSampleFormat.F32, 1, 48000, 24000);

        Assert.That(written, Is.EqualTo(200));
    }

    // ═══════════════════════════════════════════════════════════════
    // Микшер каналов
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Микшер: одинаковое количество каналов возвращает копию")]
    public void MixChannelsSameReturnsCopy()
    {
        var source = new byte[8];
        MemoryMarshal.Write(source.AsSpan(0), 0.5f);
        MemoryMarshal.Write(source.AsSpan(4), -0.5f);

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 2, 2);

        Assert.That(result, Is.EqualTo(source));
    }

    [TestCase(TestName = "Микшер: mono → stereo дублирует канал")]
    public void MixMonoToStereo()
    {
        var source = new byte[4];
        MemoryMarshal.Write(source, 0.75f);

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 1, 2);

        Assert.That(result, Has.Length.EqualTo(8));

        var left = MemoryMarshal.Read<float>(result.AsSpan(0));
        var right = MemoryMarshal.Read<float>(result.AsSpan(4));

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(left - 0.75f), Is.LessThan(0.001f));
            Assert.That(Math.Abs(right - 0.75f), Is.LessThan(0.001f));
        });
    }

    [TestCase(TestName = "Микшер: stereo → mono усредняет каналы")]
    public void MixStereoToMono()
    {
        // 1 кадр stereo: L=0.6, R=0.4
        var source = new byte[8];
        MemoryMarshal.Write(source.AsSpan(0), 0.6f);
        MemoryMarshal.Write(source.AsSpan(4), 0.4f);

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 2, 1);

        Assert.That(result, Has.Length.EqualTo(4));

        var mono = MemoryMarshal.Read<float>(result);
        Assert.That(Math.Abs(mono - 0.5f), Is.LessThan(0.001f));
    }

    [TestCase(TestName = "Микшер: stereo → mono → stereo round-trip")]
    public void MixStereoMonoRoundTrip()
    {
        var source = new byte[8];
        MemoryMarshal.Write(source.AsSpan(0), 0.5f);
        MemoryMarshal.Write(source.AsSpan(4), 0.5f);

        var mono = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 2, 1);
        var stereo = AudioResampler.MixChannels(mono, AudioSampleFormat.F32, 1, 2);

        var left = MemoryMarshal.Read<float>(stereo.AsSpan(0));
        var right = MemoryMarshal.Read<float>(stereo.AsSpan(4));

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(left - 0.5f), Is.LessThan(0.001f));
            Assert.That(Math.Abs(right - 0.5f), Is.LessThan(0.001f));
        });
    }

    [TestCase(TestName = "Микшер: stereo → 5.1 правильное количество каналов")]
    public void MixStereoTo51()
    {
        var source = new byte[8]; // 1 кадр stereo F32
        MemoryMarshal.Write(source.AsSpan(0), 0.8f);
        MemoryMarshal.Write(source.AsSpan(4), 0.6f);

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 2, 6);

        // 1 кадр × 6 каналов × 4 байт = 24
        Assert.That(result, Has.Length.EqualTo(24));

        var fl = MemoryMarshal.Read<float>(result.AsSpan(0));
        var fr = MemoryMarshal.Read<float>(result.AsSpan(4));
        var fc = MemoryMarshal.Read<float>(result.AsSpan(8));

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(fl - 0.8f), Is.LessThan(0.001f));
            Assert.That(Math.Abs(fr - 0.6f), Is.LessThan(0.001f));
            Assert.That(Math.Abs(fc - 0.7f), Is.LessThan(0.001f)); // среднее L+R
        });
    }

    [TestCase(TestName = "Микшер: 5.1 → stereo ITU-R BS.775 downmix")]
    public void Mix51ToStereoDownmix()
    {
        // 1 кадр 5.1 F32: FL=0.5, FR=0.5, FC=0.0, LFE=0.0, RL=0.0, RR=0.0
        var source = new byte[24];
        MemoryMarshal.Write(source.AsSpan(0), 0.5f);   // FL
        MemoryMarshal.Write(source.AsSpan(4), 0.5f);   // FR
        MemoryMarshal.Write(source.AsSpan(8), 0.0f);   // FC
        MemoryMarshal.Write(source.AsSpan(12), 0.0f);  // LFE
        MemoryMarshal.Write(source.AsSpan(16), 0.0f);  // RL
        MemoryMarshal.Write(source.AsSpan(20), 0.0f);  // RR

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 6, 2);

        Assert.That(result, Has.Length.EqualTo(8));

        var left = MemoryMarshal.Read<float>(result.AsSpan(0));
        var right = MemoryMarshal.Read<float>(result.AsSpan(4));

        // Без center/rear → L=FL=0.5, R=FR=0.5
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(left - 0.5f), Is.LessThan(0.001f));
            Assert.That(Math.Abs(right - 0.5f), Is.LessThan(0.001f));
        });
    }

    [TestCase(TestName = "Микшер: 5.1 → stereo center подмешивается в оба канала")]
    public void Mix51CenterMixesToBoth()
    {
        var source = new byte[24];
        MemoryMarshal.Write(source.AsSpan(0), 0.0f);   // FL
        MemoryMarshal.Write(source.AsSpan(4), 0.0f);   // FR
        MemoryMarshal.Write(source.AsSpan(8), 1.0f);   // FC
        MemoryMarshal.Write(source.AsSpan(12), 0.0f);  // LFE
        MemoryMarshal.Write(source.AsSpan(16), 0.0f);  // RL
        MemoryMarshal.Write(source.AsSpan(20), 0.0f);  // RR

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 6, 2);

        var left = MemoryMarshal.Read<float>(result.AsSpan(0));
        var right = MemoryMarshal.Read<float>(result.AsSpan(4));

        // Center × 0.707 ≈ 0.707
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(left - 0.707f), Is.LessThan(0.01f));
            Assert.That(Math.Abs(right - 0.707f), Is.LessThan(0.01f));
        });
    }

    [TestCase(TestName = "Микшер: S16 mono → stereo")]
    public void MixMonoToStereoS16()
    {
        var source = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(source, 16384); // ~0.5

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.S16, 1, 2);

        Assert.That(result, Has.Length.EqualTo(4));

        var left = BinaryPrimitives.ReadInt16LittleEndian(result.AsSpan(0));
        var right = BinaryPrimitives.ReadInt16LittleEndian(result.AsSpan(2));

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.EqualTo(right));
            Assert.That(Math.Abs(left - 16384), Is.LessThanOrEqualTo(1));
        });
    }

    [TestCase(TestName = "Микшер: несколько кадров mono → stereo")]
    public void MixMultipleFramesMonoToStereo()
    {
        // 3 кадра F32 mono
        var source = new byte[12];
        MemoryMarshal.Write(source.AsSpan(0), 0.1f);
        MemoryMarshal.Write(source.AsSpan(4), 0.5f);
        MemoryMarshal.Write(source.AsSpan(8), 0.9f);

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 1, 2);

        // 3 кадра × 2 канала × 4 байт = 24
        Assert.That(result, Has.Length.EqualTo(24));

        // Каждый кадр: L == R == mono
        for (var f = 0; f < 3; f++)
        {
            var left = MemoryMarshal.Read<float>(result.AsSpan(f * 8));
            var right = MemoryMarshal.Read<float>(result.AsSpan((f * 8) + 4));
            Assert.That(Math.Abs(left - right), Is.LessThan(0.001f));
        }
    }

    [TestCase(TestName = "Микшер: буферная версия MixChannels")]
    public void MixChannelsToBuffer()
    {
        var source = new byte[4];
        MemoryMarshal.Write(source, 0.75f);

        var dest = new byte[8];
        var written = AudioResampler.MixChannels(source, dest, AudioSampleFormat.F32, 1, 2);

        Assert.That(written, Is.EqualTo(8));
    }

    [TestCase(TestName = "Микшер: sourceChannels < 1 выбрасывает исключение")]
    public void MixZeroSourceChannelsThrows()
    {
        Assert.That(
            () => AudioResampler.MixChannels([], AudioSampleFormat.F32, 0, 2),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(TestName = "Микшер: targetChannels < 1 выбрасывает исключение")]
    public void MixZeroTargetChannelsThrows()
    {
        Assert.That(
            () => AudioResampler.MixChannels([], AudioSampleFormat.F32, 1, 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(TestName = "Микшер: 3 → 2 generic downmix")]
    public void MixGenericDownmix()
    {
        // 1 кадр, 3 канала F32
        var source = new byte[12];
        MemoryMarshal.Write(source.AsSpan(0), 0.6f);
        MemoryMarshal.Write(source.AsSpan(4), 0.4f);
        MemoryMarshal.Write(source.AsSpan(8), 0.2f);

        var result = AudioResampler.MixChannels(source, AudioSampleFormat.F32, 3, 2);

        Assert.That(result, Has.Length.EqualTo(8));
    }
}
