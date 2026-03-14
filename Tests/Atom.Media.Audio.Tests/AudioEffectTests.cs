using System.Runtime.InteropServices;
using Atom.Media.Audio;
using Atom.Media.Audio.Effects;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class AudioEffectTests(ILogger logger) : BenchmarkTests<AudioEffectTests>(logger)
{
    public AudioEffectTests() : this(ConsoleLogger.Unicode) { }

    // ═══════════════════════════════════════════════════════════════
    // AudioEffectChain
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Цепочка: пустая цепочка = passthrough")]
    public void ChainEmptyPassthrough()
    {
        var chain = new AudioEffectChain();
        float[] samples = [0.5f, -0.3f, 0.8f];
        float[] original = [.. samples];

        chain.Process(samples, 1, 48000);

        Assert.That(samples, Is.EqualTo(original));
    }

    [TestCase(TestName = "Цепочка: отключённая = passthrough")]
    public void ChainDisabledPassthrough()
    {
        var chain = new AudioEffectChain { IsEnabled = false };
        chain.Add(new NoiseGate { ThresholdDb = 0 }); // гейт заглушит всё

        float[] samples = [0.5f, -0.3f];
        float[] original = [.. samples];

        chain.Process(samples, 1, 48000);

        Assert.That(samples, Is.EqualTo(original));
    }

    [TestCase(TestName = "Цепочка: добавление и удаление эффектов")]
    public void ChainAddRemove()
    {
        var chain = new AudioEffectChain();
        var gate = new NoiseGate();

        chain.Add(gate);
        Assert.That(chain.Effects, Has.Count.EqualTo(1));

        chain.Remove(gate);
        Assert.That(chain.Effects, Is.Empty);
    }

    [TestCase(TestName = "Цепочка: Clear очищает список")]
    public void ChainClear()
    {
        var chain = new AudioEffectChain();
        chain.Add(new NoiseGate());
        chain.Add(new Compressor());

        chain.Clear();

        Assert.That(chain.Effects, Is.Empty);
    }

    [TestCase(TestName = "Цепочка: отключённый эффект пропускается")]
    public void ChainSkipsDisabledEffect()
    {
        var gate = new NoiseGate { ThresholdDb = 0, IsEnabled = false };
        var chain = new AudioEffectChain();
        chain.Add(gate);

        float[] samples = [0.5f];
        chain.Process(samples, 1, 48000);

        Assert.That(samples[0], Is.EqualTo(0.5f));
    }

    // ═══════════════════════════════════════════════════════════════
    // NoiseGate
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Noise Gate: тишина ниже порога заглушается")]
    public void NoiseGateSilenceBelowThreshold()
    {
        var gate = new NoiseGate
        {
            ThresholdDb = -20.0f,
            AttackMs = 0.01f,
            ReleaseMs = 0.01f,
            HoldMs = 0,
        };

        // Тихий сигнал (0.01 ≈ -40 dB) — ниже порога -20 dB
        var samples = new float[200];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.01f;

        // Прогоняем несколько раз для стабилизации гейта
        for (var pass = 0; pass < 5; pass++)
        {
            gate.Process(samples, 1, 48000);
        }

        // Гейт должен быть практически закрыт
        Assert.That(MathF.Abs(samples[^1]), Is.LessThan(0.005f));
    }

    [TestCase(TestName = "Noise Gate: сигнал выше порога проходит")]
    public void NoiseGateSignalAboveThreshold()
    {
        var gate = new NoiseGate
        {
            ThresholdDb = -40.0f,
            AttackMs = 0.01f,
            ReleaseMs = 50.0f,
            HoldMs = 0,
        };

        // Громкий сигнал (0.5 ≈ -6 dB) — выше порога -40 dB
        var samples = new float[200];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.5f;

        gate.Process(samples, 1, 48000);

        // Последний семпл должен быть близок к исходному
        Assert.That(samples[^1], Is.GreaterThan(0.4f));
    }

    [TestCase(TestName = "Noise Gate: Reset сбрасывает состояние")]
    public void NoiseGateReset()
    {
        var gate = new NoiseGate { ThresholdDb = -40.0f, AttackMs = 0.01f };
        float[] loud = [0.5f, 0.5f, 0.5f, 0.5f];
        gate.Process(loud, 1, 48000);

        gate.Reset();

        float[] quiet = [0.001f, 0.001f];
        gate.Process(quiet, 1, 48000);

        // После reset гейт закрыт — тихий сигнал подавлен
        Assert.That(MathF.Abs(quiet[^1]), Is.LessThan(0.001f));
    }

    // ═══════════════════════════════════════════════════════════════
    // Compressor
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Компрессор: сигнал ниже порога не сжимается")]
    public void CompressorBelowThresholdNoCompression()
    {
        var comp = new Compressor
        {
            ThresholdDb = -6.0f,
            Ratio = 4.0f,
            AttackMs = 0.01f,
            ReleaseMs = 0.01f,
        };

        // 0.1 ≈ -20 dB, ниже порога -6 dB
        float[] samples = [0.1f, 0.1f, 0.1f, 0.1f];

        comp.Process(samples, 1, 48000);

        Assert.That(samples[^1], Is.EqualTo(0.1f).Within(0.01f));
    }

    [TestCase(TestName = "Компрессор: сигнал выше порога сжимается")]
    public void CompressorAboveThresholdCompresses()
    {
        var comp = new Compressor
        {
            ThresholdDb = -20.0f,
            Ratio = 4.0f,
            AttackMs = 0.01f,
            ReleaseMs = 50.0f,
        };

        // 0.9 ≈ -1 dB, значительно выше порога -20 dB
        var samples = new float[500];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.9f;

        comp.Process(samples, 1, 48000);

        // После compress уровень должен уменьшиться
        Assert.That(samples[^1], Is.LessThan(0.85f));
    }

    [TestCase(TestName = "Компрессор: makeup gain увеличивает выходной уровень")]
    public void CompressorMakeupGain()
    {
        var comp = new Compressor
        {
            ThresholdDb = -40.0f,
            Ratio = 2.0f,
            AttackMs = 0.01f,
            ReleaseMs = 0.01f,
            MakeupGainDb = 6.0f,
        };

        float[] samples = [0.01f, 0.01f, 0.01f, 0.01f];
        comp.Process(samples, 1, 48000);

        // 0.01 ниже порога → без компрессии, но makeup ≈ 2x
        Assert.That(samples[^1], Is.GreaterThan(0.015f));
    }

    [TestCase(TestName = "Компрессор: Reset сбрасывает envelope")]
    public void CompressorReset()
    {
        var comp = new Compressor { ThresholdDb = -20.0f, Ratio = 4.0f };
        float[] loud = [0.9f, 0.9f, 0.9f, 0.9f];
        comp.Process(loud, 1, 48000);

        comp.Reset();

        float[] quiet = [0.1f, 0.1f];
        comp.Process(quiet, 1, 48000);

        // После reset envelope = 1.0, тихий сигнал без компрессии
        Assert.That(quiet[^1], Is.EqualTo(0.1f).Within(0.01f));
    }

    // ═══════════════════════════════════════════════════════════════
    // Equalizer
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Эквалайзер: 0 dB gain = passthrough")]
    public void EqualizerFlatResponse()
    {
        var eq = new Equalizer(new EqBand(1000.0f, 0.0f));

        // Процессим несколько фреймов чтобы фильтр стабилизировался
        var samples = new float[1024];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = MathF.Sin(2.0f * MathF.PI * 1000 * i / 48000);
        }

        var originalRms = ComputeRms(samples.AsSpan(512));
        eq.Process(samples, 1, 48000);
        var processedRms = ComputeRms(samples.AsSpan(512));

        // С 0 dB gain RMS должен остаться примерно тем же
        Assert.That(processedRms, Is.EqualTo(originalRms).Within(0.01f));
    }

    [TestCase(TestName = "Эквалайзер: boost увеличивает уровень на частоте")]
    public void EqualizerBoost()
    {
        var eq = new Equalizer(new EqBand(1000.0f, 12.0f, 1.0f));

        // Синус 1000 Hz
        var samples = new float[2048];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.1f * MathF.Sin(2.0f * MathF.PI * 1000 * i / 48000);
        }

        var originalRms = ComputeRms(samples.AsSpan(1024));
        eq.Process(samples, 1, 48000);
        var processedRms = ComputeRms(samples.AsSpan(1024));

        // 12 dB boost → уровень должен значительно вырасти
        Assert.That(processedRms, Is.GreaterThan(originalRms * 2.0f));
    }

    [TestCase(TestName = "Эквалайзер: cut уменьшает уровень на частоте")]
    public void EqualizerCut()
    {
        var eq = new Equalizer(new EqBand(1000.0f, -12.0f, 1.0f));

        var samples = new float[2048];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.5f * MathF.Sin(2.0f * MathF.PI * 1000 * i / 48000);
        }

        var originalRms = ComputeRms(samples.AsSpan(1024));
        eq.Process(samples, 1, 48000);
        var processedRms = ComputeRms(samples.AsSpan(1024));

        // -12 dB → уровень должен уменьшиться
        Assert.That(processedRms, Is.LessThan(originalRms * 0.5f));
    }

    [TestCase(TestName = "Эквалайзер: Reset очищает состояние")]
    public void EqualizerReset()
    {
        var eq = new Equalizer(new EqBand(1000.0f, 6.0f));
        float[] samples = [0.5f, 0.5f, 0.5f, 0.5f];
        eq.Process(samples, 1, 48000);

        eq.Reset();

        // После reset внутренние Z1/Z2 = 0
        float[] newSamples = [0.5f, 0.5f, 0.5f, 0.5f];
        float[] freshSamples = [0.5f, 0.5f, 0.5f, 0.5f];
        var freshEq = new Equalizer(new EqBand(1000.0f, 6.0f));

        eq.Process(newSamples, 1, 48000);
        freshEq.Process(freshSamples, 1, 48000);

        Assert.That(newSamples, Is.EqualTo(freshSamples));
    }

    [TestCase(TestName = "Эквалайзер: стерео обрабатывает оба канала")]
    public void EqualizerStereo()
    {
        var eq = new Equalizer(new EqBand(1000.0f, 6.0f));

        // L=0.3, R=0.5
        float[] samples = [0.3f, 0.5f, 0.3f, 0.5f, 0.3f, 0.5f, 0.3f, 0.5f];
        eq.Process(samples, 2, 48000);

        // Оба канала должны быть обработаны (не равны исходным)
        Assert.That(samples[0], Is.Not.EqualTo(0.3f));
        Assert.That(samples[1], Is.Not.EqualTo(0.5f));
    }

    [TestCase(TestName = "EqBand: значения по умолчанию")]
    public void EqBandDefaults()
    {
        var band = new EqBand(1000.0f, 6.0f);
        Assert.That(band.Frequency, Is.EqualTo(1000.0f));
        Assert.That(band.GainDb, Is.EqualTo(6.0f));
        Assert.That(band.Q, Is.EqualTo(0.707f));
    }

    [TestCase(TestName = "EqBand: пользовательский Q")]
    public void EqBandCustomQ()
    {
        var band = new EqBand(500.0f, -3.0f, 2.0f);
        Assert.That(band.Q, Is.EqualTo(2.0f));
    }

    private static float ComputeRms(Span<float> samples)
    {
        var sum = 0.0;

        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * (double)samples[i];
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }
}
