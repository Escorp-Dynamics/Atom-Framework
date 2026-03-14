using System.Runtime.InteropServices;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;
using Atom.Media.Audio.Effects;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class VirtualMicrophoneIntegrationTests(ILogger logger)
    : BenchmarkTests<VirtualMicrophoneIntegrationTests>(logger)
{
    public VirtualMicrophoneIntegrationTests() : this(ConsoleLogger.Unicode) { }

    private static VirtualMicrophone CreateMic(
        FakeMicrophoneBackend? backend = null,
        VirtualMicrophoneSettings? settings = null)
    {
        backend ??= new FakeMicrophoneBackend();
        settings ??= new VirtualMicrophoneSettings();
        return new VirtualMicrophone(backend, settings);
    }

    private static byte[] GenerateF32Samples(int sampleCount, float amplitude = 1.0f)
    {
        var data = new byte[sampleCount * sizeof(float)];
        for (var i = 0; i < sampleCount; i++)
        {
            var value = amplitude * MathF.Sin(2.0f * MathF.PI * 440.0f * i / 48000.0f);
            MemoryMarshal.Write(data.AsSpan(i * sizeof(float)), value);
        }

        return data;
    }

    private static byte[] GenerateF32Silence(int sampleCount)
    {
        return new byte[sampleCount * sizeof(float)];
    }

    private static byte[] GenerateF32Constant(int sampleCount, float value)
    {
        var data = new byte[sampleCount * sizeof(float)];
        for (var i = 0; i < sampleCount; i++)
        {
            MemoryMarshal.Write(data.AsSpan(i * sizeof(float)), value);
        }

        return data;
    }

    // ═══════════════════════════════════════════════════════════════
    // Effects интеграция
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Effects: пустая цепочка — данные проходят без изменений")]
    public void EmptyEffectChainPassthrough()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);

        var data = GenerateF32Constant(480, 0.5f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
        Assert.That(backend.LastWrittenSamples, Is.EqualTo(data));
    }

    [TestCase(TestName = "Effects: отключённая цепочка — данные проходят без изменений")]
    public void DisabledEffectChainPassthrough()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);
        mic.Effects.Add(new Compressor());
        mic.Effects.IsEnabled = false;

        var data = GenerateF32Constant(480, 0.5f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.EqualTo(data));
    }

    [TestCase(TestName = "Effects: компрессор уменьшает громкий сигнал")]
    public void CompressorReducesLoudSignal()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);
        mic.Effects.Add(new Compressor { ThresholdDb = -20, Ratio = 10 });

        var data = GenerateF32Constant(480, 0.9f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
        var outputSamples = MemoryMarshal.Cast<byte, float>(backend.LastWrittenSamples);
        Assert.That(outputSamples[^1], Is.LessThan(0.9f));
    }

    [TestCase(TestName = "Effects: noise gate заглушает тихий сигнал")]
    public void NoiseGateSilencesQuietSignal()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);
        mic.Effects.Add(new NoiseGate { ThresholdDb = -10, AttackMs = 0, ReleaseMs = 0 });

        var data = GenerateF32Constant(480, 0.01f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
        var outputSamples = MemoryMarshal.Cast<byte, float>(backend.LastWrittenSamples);

        var maxAbs = 0.0f;
        foreach (var s in outputSamples)
        {
            var abs = MathF.Abs(s);
            if (abs > maxAbs) maxAbs = abs;
        }

        Assert.That(maxAbs, Is.LessThan(0.01f));
    }

    [TestCase(TestName = "Effects: цепочка из нескольких эффектов применяется последовательно")]
    public void MultipleEffectsAppliedSequentially()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);
        mic.Effects.Add(new NoiseGate { ThresholdDb = -60 });
        mic.Effects.Add(new Compressor { ThresholdDb = -20, Ratio = 4 });

        var data = GenerateF32Samples(480, 0.8f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
        Assert.That(backend.LastWrittenSamples!.Length, Is.EqualTo(data.Length));
    }

    [TestCase(TestName = "Effects: обработка S16 формата")]
    public void EffectsWithS16Format()
    {
        var backend = new FakeMicrophoneBackend();
        var settings = new VirtualMicrophoneSettings { SampleFormat = AudioSampleFormat.S16 };
        var mic = CreateMic(backend, settings);
        mic.Effects.Add(new Compressor { ThresholdDb = -20, Ratio = 4 });

        var data = new byte[480 * 2];
        for (var i = 0; i < 480; i++)
        {
            var value = (short)(16000);
            data[i * 2] = (byte)(value & 0xFF);
            data[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
        Assert.That(backend.LastWrittenSamples!.Length, Is.EqualTo(data.Length));
    }

    // ═══════════════════════════════════════════════════════════════
    // AudioMeter интеграция
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Metering: тишина — уровни нулевые")]
    public void MeteringSilenceZeroLevels()
    {
        var mic = CreateMic();

        var silence = GenerateF32Silence(480);
        mic.WriteSamples(silence);

        Assert.Multiple(() =>
        {
            Assert.That(mic.PeakLevel, Is.Zero.Within(0.001f));
            Assert.That(mic.CurrentLevel, Is.Zero.Within(0.001f));
        });
    }

    [TestCase(TestName = "Metering: полная амплитуда — пик = 1.0")]
    public void MeteringFullAmplitude()
    {
        var mic = CreateMic();

        var data = GenerateF32Constant(480, 1.0f);
        mic.WriteSamples(data);

        Assert.Multiple(() =>
        {
            Assert.That(mic.PeakLevel, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(mic.CurrentLevel, Is.EqualTo(1.0f).Within(0.001f));
        });
    }

    [TestCase(TestName = "Metering: половинная амплитуда — пик = 0.5")]
    public void MeteringHalfAmplitude()
    {
        var mic = CreateMic();

        var data = GenerateF32Constant(480, 0.5f);
        mic.WriteSamples(data);

        Assert.Multiple(() =>
        {
            Assert.That(mic.PeakLevel, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(mic.CurrentLevel, Is.EqualTo(0.5f).Within(0.001f));
        });
    }

    [TestCase(TestName = "Metering: уровни обновляются при каждом WriteSamples")]
    public void MeteringUpdatesOnEachWrite()
    {
        var mic = CreateMic();

        mic.WriteSamples(GenerateF32Constant(480, 0.8f));
        Assert.That(mic.PeakLevel, Is.EqualTo(0.8f).Within(0.001f));

        mic.WriteSamples(GenerateF32Constant(480, 0.2f));
        Assert.That(mic.PeakLevel, Is.EqualTo(0.2f).Within(0.001f));
    }

    [TestCase(TestName = "Metering: уровни с активными эффектами")]
    public void MeteringWithActiveEffects()
    {
        var mic = CreateMic();
        mic.Effects.Add(new Compressor { ThresholdDb = -6, Ratio = 10 });

        var data = GenerateF32Constant(480, 0.9f);
        mic.WriteSamples(data);

        Assert.That(mic.PeakLevel, Is.LessThan(0.9f));
        Assert.That(mic.CurrentLevel, Is.GreaterThan(0.0f));
    }

    [TestCase(TestName = "Metering: ResetLevels сбрасывает на ноль")]
    public void MeteringResetLevels()
    {
        var mic = CreateMic();

        mic.WriteSamples(GenerateF32Constant(480, 0.7f));
        Assert.That(mic.PeakLevel, Is.GreaterThan(0.0f));

        mic.ResetLevels();

        Assert.Multiple(() =>
        {
            Assert.That(mic.PeakLevel, Is.Zero.Within(0.001f));
            Assert.That(mic.CurrentLevel, Is.Zero.Within(0.001f));
        });
    }

    [TestCase(TestName = "Metering: синусоида — RMS ≈ peak / √2")]
    public void MeteringSineWaveRms()
    {
        var mic = CreateMic();

        var data = GenerateF32Samples(48000, 1.0f);
        mic.WriteSamples(data);

        var expectedRms = 1.0f / MathF.Sqrt(2.0f);
        Assert.That(mic.CurrentLevel, Is.EqualTo(expectedRms).Within(0.02f));
    }

    // ═══════════════════════════════════════════════════════════════
    // MonitorBuffer интеграция
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Monitor: по умолчанию null")]
    public void MonitorBufferDefaultNull()
    {
        var mic = CreateMic();
        Assert.That(mic.MonitorBuffer, Is.Null);
    }

    [TestCase(TestName = "Monitor: EnableMonitoring создаёт буфер")]
    public void EnableMonitoringCreatesBuffer()
    {
        var mic = CreateMic();
        mic.EnableMonitoring(4096);

        Assert.That(mic.MonitorBuffer, Is.Not.Null);
        Assert.That(mic.MonitorBuffer!.Capacity, Is.EqualTo(4096));
    }

    [TestCase(TestName = "Monitor: DisableMonitoring обнуляет буфер")]
    public void DisableMonitoringClearsBuffer()
    {
        var mic = CreateMic();
        mic.EnableMonitoring(4096);
        mic.DisableMonitoring();

        Assert.That(mic.MonitorBuffer, Is.Null);
    }

    [TestCase(TestName = "Monitor: WriteSamples пишет в буфер мониторинга")]
    public void WriteSamplesFillsMonitorBuffer()
    {
        var mic = CreateMic();
        mic.EnableMonitoring(8192);

        var data = GenerateF32Constant(480, 0.5f);
        mic.WriteSamples(data);

        Assert.That(mic.MonitorBuffer!.AvailableRead, Is.EqualTo(data.Length));

        var readBack = new byte[data.Length];
        var read = mic.MonitorBuffer.Read(readBack);

        Assert.That(read, Is.EqualTo(data.Length));
        Assert.That(readBack, Is.EqualTo(data));
    }

    [TestCase(TestName = "Monitor: буфер содержит обработанные данные после эффектов")]
    public void MonitorBufferContainsProcessedData()
    {
        var mic = CreateMic();
        mic.EnableMonitoring(8192);
        mic.Effects.Add(new Compressor { ThresholdDb = -6, Ratio = 10 });

        var data = GenerateF32Constant(480, 0.9f);
        mic.WriteSamples(data);

        var readBack = new byte[data.Length];
        mic.MonitorBuffer!.Read(readBack);
        var monitorSamples = MemoryMarshal.Cast<byte, float>(readBack);

        Assert.That(monitorSamples[^1], Is.LessThan(0.9f));
    }

    [TestCase(TestName = "Monitor: без мониторинга WriteSamples не ошибается")]
    public void WriteSamplesWithoutMonitorNoError()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);

        var data = GenerateF32Constant(480, 0.5f);
        Assert.DoesNotThrow(() => mic.WriteSamples(data));
        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
    }

    [TestCase(TestName = "Monitor: DisposeAsync обнуляет буфер")]
    public async Task DisposeAsyncClearsMonitorBuffer()
    {
        var mic = CreateMic();
        mic.EnableMonitoring(4096);

        await mic.DisposeAsync();

        Assert.That(mic.MonitorBuffer, Is.Null);
    }

    // ═══════════════════════════════════════════════════════════════
    // Полный пайплайн
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Pipeline: effects + metering + monitor — полная цепочка")]
    public void FullPipelineIntegration()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);
        mic.Effects.Add(new Compressor { ThresholdDb = -6, Ratio = 4 });
        mic.EnableMonitoring(8192);

        var data = GenerateF32Constant(480, 0.9f);
        mic.WriteSamples(data);

        Assert.Multiple(() =>
        {
            // Metering отражает обработанный сигнал
            Assert.That(mic.PeakLevel, Is.LessThan(0.9f));
            Assert.That(mic.PeakLevel, Is.GreaterThan(0.0f));
            Assert.That(mic.CurrentLevel, Is.GreaterThan(0.0f));

            // Backend получил обработанные данные
            Assert.That(backend.LastWrittenSamples, Is.Not.Null);
            var backendSamples = MemoryMarshal.Cast<byte, float>(backend.LastWrittenSamples);
            Assert.That(backendSamples[^1], Is.LessThan(0.9f));

            // Monitor содержит те же данные, что и backend
            Assert.That(mic.MonitorBuffer!.AvailableRead, Is.EqualTo(data.Length));
        });
    }

    [TestCase(TestName = "Pipeline: несколько вызовов WriteSamples подряд")]
    public void MultipleWriteSamplesCalls()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);
        mic.EnableMonitoring(32768);

        mic.WriteSamples(GenerateF32Constant(480, 0.3f));
        Assert.That(mic.PeakLevel, Is.EqualTo(0.3f).Within(0.001f));

        mic.WriteSamples(GenerateF32Constant(480, 0.7f));
        Assert.That(mic.PeakLevel, Is.EqualTo(0.7f).Within(0.001f));

        Assert.That(mic.MonitorBuffer!.AvailableRead, Is.EqualTo(480 * sizeof(float) * 2));
    }

    [TestCase(TestName = "Effects: свойство Effects доступно и не null")]
    public void EffectsPropertyNotNull()
    {
        var mic = CreateMic();
        Assert.That(mic.Effects, Is.Not.Null);
        Assert.That(mic.Effects.Effects, Is.Empty);
    }

    // ═══════════════════════════════════════════════════════════════
    // PeakHold интеграция
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "PeakHold: свойство доступно и начальное значение 0")]
    public void PeakHoldPropertyAvailable()
    {
        var mic = CreateMic();
        Assert.That(mic.PeakHold, Is.Not.Null);
        Assert.That(mic.PeakHold.HoldPeak, Is.Zero.Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: обновляется при WriteSamples")]
    public void PeakHoldUpdatedOnWriteSamples()
    {
        var mic = CreateMic();
        mic.WriteSamples(GenerateF32Constant(480, 0.8f));

        Assert.That(mic.PeakHold.HoldPeak, Is.EqualTo(0.8f).Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: удерживает максимум при уменьшении сигнала")]
    public void PeakHoldRetainsMaximum()
    {
        var mic = CreateMic();
        mic.WriteSamples(GenerateF32Constant(480, 0.9f));
        mic.WriteSamples(GenerateF32Constant(480, 0.1f));

        Assert.That(mic.PeakHold.HoldPeak, Is.EqualTo(0.9f).Within(0.001f));
    }

    [TestCase(TestName = "PeakHold: ResetLevels сбрасывает и HoldPeak")]
    public void ResetLevelsClearsPeakHold()
    {
        var mic = CreateMic();
        mic.WriteSamples(GenerateF32Constant(480, 0.7f));
        mic.ResetLevels();

        Assert.That(mic.PeakHold.HoldPeak, Is.Zero.Within(0.001f));
    }

    // ═══════════════════════════════════════════════════════════════
    // Компенсация латентности
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Latency: по умолчанию 0")]
    public void LatencyDefaultZero()
    {
        var mic = CreateMic();
        Assert.That(mic.LatencyCompensationMs, Is.Zero.Within(0.001));
    }

    [TestCase(TestName = "Latency: установка задержки")]
    public void LatencySetValue()
    {
        var mic = CreateMic();
        mic.LatencyCompensationMs = 50.0;

        Assert.That(mic.LatencyCompensationMs, Is.EqualTo(50.0).Within(0.001));
    }

    [TestCase(TestName = "Latency: первый фрейм — тишина")]
    public void LatencyFirstFrameIsSilence()
    {
        var backend = new FakeMicrophoneBackend();
        var settings = new VirtualMicrophoneSettings { SampleRate = 48000, Channels = 1 };
        var mic = CreateMic(backend, settings);
        mic.LatencyCompensationMs = 100.0;

        var data = GenerateF32Constant(480, 0.8f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
        var output = MemoryMarshal.Cast<byte, float>(backend.LastWrittenSamples);

        var maxAbs = 0.0f;
        foreach (var s in output)
        {
            var abs = MathF.Abs(s);
            if (abs > maxAbs) maxAbs = abs;
        }

        Assert.That(maxAbs, Is.Zero.Within(0.001f), "Первый фрейм должен быть тишиной из-за задержки");
    }

    [TestCase(TestName = "Latency: задержанные данные появляются после заполнения буфера")]
    public void LatencyDelayedDataAppearsLater()
    {
        var backend = new FakeMicrophoneBackend();
        var settings = new VirtualMicrophoneSettings { SampleRate = 48000, Channels = 1, LatencyMs = 10 };
        var mic = CreateMic(backend, settings);
        mic.LatencyCompensationMs = 10.0;

        var signal = GenerateF32Constant(480, 0.75f);
        mic.WriteSamples(signal);
        mic.WriteSamples(signal);

        Assert.That(backend.LastWrittenSamples, Is.Not.Null);
        var output = MemoryMarshal.Cast<byte, float>(backend.LastWrittenSamples);

        var anyNonZero = false;
        foreach (var s in output)
        {
            if (MathF.Abs(s) > 0.01f)
            {
                anyNonZero = true;
                break;
            }
        }

        Assert.That(anyNonZero, Is.True, "После заполнения буфера задержки данные должны появиться");
    }

    [TestCase(TestName = "Latency: сброс на 0 отключает задержку")]
    public void LatencyResetToZero()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);
        mic.LatencyCompensationMs = 50.0;
        mic.LatencyCompensationMs = 0;

        var data = GenerateF32Constant(480, 0.6f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.EqualTo(data));
    }

    [TestCase(TestName = "Latency: без задержки данные проходят без изменений")]
    public void NoLatencyPassthrough()
    {
        var backend = new FakeMicrophoneBackend();
        var mic = CreateMic(backend);

        var data = GenerateF32Constant(480, 0.5f);
        mic.WriteSamples(data);

        Assert.That(backend.LastWrittenSamples, Is.EqualTo(data));
    }

    // ═══════════════════════════════════════════════════════════════
    // Fake backend для тестирования
    // ═══════════════════════════════════════════════════════════════

    internal sealed class FakeMicrophoneBackend : IVirtualMicrophoneBackend, IDisposable
    {
        public string DeviceIdentifier => "fake:test";

        public bool IsCapturing { get; private set; }

        public byte[]? LastWrittenSamples { get; private set; }

        public ValueTask InitializeAsync(
            VirtualMicrophoneSettings settings, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteSamples(ReadOnlySpan<byte> sampleData) =>
            LastWrittenSamples = sampleData.ToArray();

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(MicrophoneControlType control, float value) { }

        public float GetControl(MicrophoneControlType control) =>
            control == MicrophoneControlType.Volume ? 1.0f : 0.0f;

        public MicrophoneControlRange? GetControlRange(MicrophoneControlType control) => null;

#pragma warning disable CS0067
        public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;
#pragma warning restore CS0067

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() { }
    }
}
