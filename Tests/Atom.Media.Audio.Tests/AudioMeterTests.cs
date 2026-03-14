using System.Runtime.InteropServices;
using Atom.Media.Audio;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class AudioMeterTests(ILogger logger) : BenchmarkTests<AudioMeterTests>(logger)
{
    public AudioMeterTests() : this(ConsoleLogger.Unicode) { }

    // ═══════════════════════════════════════════════════════════════
    // Peak
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Уровнемер: пик тишины = 0")]
    public void PeakSilence()
    {
        var silence = new byte[16]; // 4 семпла F32 = 0.0f
        var peak = AudioMeter.MeasurePeak(silence, AudioSampleFormat.F32, 1);
        Assert.That(peak, Is.Zero);
    }

    [TestCase(TestName = "Уровнемер: пик полной шкалы = 1.0")]
    public void PeakFullScale()
    {
        var data = new byte[4];
        MemoryMarshal.Write(data, 1.0f);

        var peak = AudioMeter.MeasurePeak(data, AudioSampleFormat.F32, 1);
        Assert.That(peak, Is.EqualTo(1.0f));
    }

    [TestCase(TestName = "Уровнемер: пик отрицательного семпла")]
    public void PeakNegativeSample()
    {
        var data = new byte[4];
        MemoryMarshal.Write(data, -0.75f);

        var peak = AudioMeter.MeasurePeak(data, AudioSampleFormat.F32, 1);
        Assert.That(peak, Is.EqualTo(0.75f).Within(0.001f));
    }

    [TestCase(TestName = "Уровнемер: пик нескольких семплов")]
    public void PeakMultipleSamples()
    {
        float[] samples = [0.1f, -0.5f, 0.3f, 0.8f];
        var data = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

        var peak = AudioMeter.MeasurePeak(data, AudioSampleFormat.F32, 1);
        Assert.That(peak, Is.EqualTo(0.8f).Within(0.001f));
    }

    [TestCase(TestName = "Уровнемер: пик S16")]
    public void PeakS16()
    {
        // S16 максимум = 32767, нормализовано ≈ 1.0
        byte[] data = [0xFF, 0x7F]; // 32767 little-endian

        var peak = AudioMeter.MeasurePeak(data, AudioSampleFormat.S16, 1);
        Assert.That(peak, Is.GreaterThan(0.99f));
    }

    [TestCase(TestName = "Уровнемер: пик поканально стерео")]
    public void PeakPerChannelStereo()
    {
        // L=0.3, R=0.9, L=0.1, R=0.5
        float[] samples = [0.3f, 0.9f, 0.1f, 0.5f];
        var data = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

        Span<float> perChannel = stackalloc float[2];
        AudioMeter.MeasurePeak(data, AudioSampleFormat.F32, 2, perChannel);

        Assert.That(perChannel[0], Is.EqualTo(0.3f).Within(0.001f)); // L peak
        Assert.That(perChannel[1], Is.EqualTo(0.9f).Within(0.001f)); // R peak
    }

    // ═══════════════════════════════════════════════════════════════
    // RMS
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Уровнемер: RMS тишины = 0")]
    public void RmsSilence()
    {
        var silence = new byte[16];
        var rms = AudioMeter.MeasureRms(silence, AudioSampleFormat.F32, 1);
        Assert.That(rms, Is.Zero);
    }

    [TestCase(TestName = "Уровнемер: RMS полной шкалы = 1.0")]
    public void RmsFullScale()
    {
        // все семплы = 1.0f → RMS = 1.0
        float[] samples = [1.0f, 1.0f, 1.0f, 1.0f];
        var data = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

        var rms = AudioMeter.MeasureRms(data, AudioSampleFormat.F32, 1);
        Assert.That(rms, Is.EqualTo(1.0f).Within(0.001f));
    }

    [TestCase(TestName = "Уровнемер: RMS синуса ≈ 0.707")]
    public void RmsSineWave()
    {
        // Генерируем синус: 1000 семплов, амплитуда 1.0
        const int count = 1000;
        var samples = new float[count];

        for (var i = 0; i < count; i++)
        {
            samples[i] = MathF.Sin(2.0f * MathF.PI * i / count);
        }

        var data = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        var rms = AudioMeter.MeasureRms(data, AudioSampleFormat.F32, 1);
        Assert.That(rms, Is.EqualTo(0.707f).Within(0.01f));
    }

    [TestCase(TestName = "Уровнемер: RMS поканально стерео")]
    public void RmsPerChannelStereo()
    {
        // L=0.5, R=1.0, L=0.5, R=1.0 → L_RMS=0.5, R_RMS=1.0
        float[] samples = [0.5f, 1.0f, 0.5f, 1.0f];
        var data = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

        Span<float> perChannel = stackalloc float[2];
        AudioMeter.MeasureRms(data, AudioSampleFormat.F32, 2, perChannel);

        Assert.That(perChannel[0], Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(perChannel[1], Is.EqualTo(1.0f).Within(0.001f));
    }

    [TestCase(TestName = "Уровнемер: RMS пустого буфера = 0")]
    public void RmsEmptyBuffer()
    {
        var rms = AudioMeter.MeasureRms([], AudioSampleFormat.F32, 1);
        Assert.That(rms, Is.Zero);
    }

    // ═══════════════════════════════════════════════════════════════
    // dB конвертация
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Уровнемер: 0 → -∞ dB")]
    public void ToDecibelsSilence()
    {
        var db = AudioMeter.ToDecibels(0.0f);
        Assert.That(float.IsNegativeInfinity(db), Is.True);
    }

    [TestCase(TestName = "Уровнемер: 1.0 → 0 dBFS")]
    public void ToDecibelsFullScale()
    {
        var db = AudioMeter.ToDecibels(1.0f);
        Assert.That(db, Is.Zero.Within(0.01f));
    }

    [TestCase(TestName = "Уровнемер: 0.5 → ≈ -6 dBFS")]
    public void ToDecibelsHalf()
    {
        var db = AudioMeter.ToDecibels(0.5f);
        Assert.That(db, Is.EqualTo(-6.02f).Within(0.1f));
    }
}
