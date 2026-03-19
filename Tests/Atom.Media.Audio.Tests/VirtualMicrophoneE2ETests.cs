using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Atom.Tests;
using Atom.Media.Audio;
using Atom.Media.Video;

namespace Atom.Media.Audio.Tests;

[TestFixture]
[Category("E2E")]
[SupportedOSPlatform("linux")]
public class VirtualMicrophoneE2ETests(ILogger logger) : BenchmarkTests<VirtualMicrophoneE2ETests>(logger)
{
    private static readonly bool isPipeWireAvailable = CheckPipeWireAvailable();

    public VirtualMicrophoneE2ETests() : this(ConsoleLogger.Unicode) { }

    private static bool CheckPipeWireAvailable()
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pw-cli",
                Arguments = "info 0",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("E2E тесты запускаются только на Linux.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }
    }

    [TestCase(TestName = "E2E: виртуальный микрофон появляется как нода PipeWire")]
    public async Task MicrophoneAppearsAsPipeWireNode()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E Test Microphone",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var expectedNodeName = BuildExpectedNodeName(settings.Name);

        Assert.That(
            nodes.Any(n => n.NodeName == expectedNodeName),
            Is.True,
            $"Нода {expectedNodeName} не найдена в PipeWire");
    }

    [TestCase(TestName = "E2E: нода микрофона имеет правильное описание")]
    public async Task MicrophoneNodeHasCorrectDescription()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 44100,
            Channels = 2,
            Name = "E2E Description Mic",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeName == BuildExpectedNodeName(settings.Name)
            && n.NodeDescription == "E2E Description Mic");

        Assert.That(node, Is.Not.Null, "Нода с описанием 'E2E Description Mic' не найдена");
    }

    [TestCase(TestName = "E2E: нода микрофона имеет media.type=Audio и media.category=Source")]
    public async Task MicrophoneNodeHasAudioSourceProperties()
    {
        var settings = new VirtualMicrophoneSettings
        {
            Name = "E2E Audio Source Test",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeName == BuildExpectedNodeName(settings.Name)
            && n.NodeDescription == "E2E Audio Source Test");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.MediaType, Is.EqualTo("Audio"));
        Assert.That(node.MediaCategory, Is.EqualTo("Source"));
        Assert.That(node.MediaRole, Is.EqualTo("Communication"));
    }

    [TestCase(TestName = "E2E: метаданные производителя передаются в PipeWire")]
    public async Task MicrophoneMetadataPassedToPipeWire()
    {
        var settings = new VirtualMicrophoneSettings
        {
            Name = "E2E Metadata Mic",
            Vendor = "Escorp Dynamics",
            Model = "Atom VMic",
            SerialNumber = "E2E-MIC-001",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeName == BuildExpectedNodeName(settings.Name)
            && n.NodeDescription == "E2E Metadata Mic");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.DeviceVendor, Is.EqualTo("Escorp Dynamics"));
        Assert.That(node.DeviceProduct, Is.EqualTo("Atom VMic"));
        Assert.That(node.DeviceSerial, Is.EqualTo("E2E-MIC-001"));
    }

    [TestCase(TestName = "E2E: USB VID/PID микрофона публикуются в PipeWire свойствах")]
    public async Task MicrophoneUsbIdsPassedToPipeWire()
    {
        var settings = new VirtualMicrophoneSettings
        {
            Name = "E2E USB Mic",
            DeviceId = "usb-mic-001",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0102,
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeDescription == settings.Name);

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.NodeName, Is.EqualTo("atom.microphone.usb-mic-001"));
        Assert.That(node.NodeGroup, Is.EqualTo(settings.DeviceId));
        Assert.That(node.DeviceVendorId, Is.EqualTo("0x1d6b"));
        Assert.That(node.DeviceProductId, Is.EqualTo("0x0102"));
    }

    [TestCase(TestName = "E2E: нода латентности видна в PipeWire свойствах")]
    public async Task MicrophoneLatencyPropertyVisible()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            LatencyMs = 20,
            Name = "E2E Latency Mic",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeName == BuildExpectedNodeName(settings.Name)
            && n.NodeDescription == "E2E Latency Mic");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        // 20ms * 48000Hz / 1000 = 960 frames
        Assert.That(node!.NodeLatency, Is.EqualTo("960/48000"));
    }

    [TestCase(TestName = "E2E: нода исчезает после Dispose")]
    public async Task MicrophoneNodeDisappearsAfterDispose()
    {
        var uniqueName = "E2E Dispose " + Guid.NewGuid().ToString("N")[..8];

        var settings = new VirtualMicrophoneSettings { Name = uniqueName };

        var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodesBefore = await GetPipeWireNodesAsync();
        Assert.That(
            nodesBefore.Any(n => n.NodeDescription == uniqueName),
            Is.True,
            "Нода не найдена до Dispose");

        await mic.DisposeAsync();
        await Task.Delay(millisecondsDelay: 200);

        var nodesAfter = await GetPipeWireNodesAsync();
        Assert.That(
            nodesAfter.Any(n => n.NodeDescription == uniqueName),
            Is.False,
            "Нода всё ещё существует после Dispose");
    }

    [TestCase(TestName = "E2E: полный цикл — создание, захват, запись семплов, остановка")]
    public async Task FullCycleWithSampleWrite()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E Full Cycle",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        Assert.That(mic.DeviceIdentifier, Is.EqualTo("pipewire:E2E Full Cycle"));

        await Task.Delay(millisecondsDelay: 100);

        var nodes = await GetPipeWireNodesAsync();
        Assert.That(
            nodes.Any(n => n.NodeDescription == "E2E Full Cycle"),
            Is.True,
            "Нода не найдена");

        await mic.StartCaptureAsync();
        Assert.That(mic.IsCapturing, Is.True);

        // Генерируем 480 семплов F32 mono (10ms при 48kHz) × 10 раз
        var buffer = new byte[480 * 4];
        for (var i = 0; i < 10; i++)
        {
            // Заполняем синусоидой 440Hz
            FillSineTone(buffer, 440.0, 48000, i * 480);
            mic.WriteSamples(buffer);
            await Task.Delay(millisecondsDelay: 10);
        }

        await mic.StopCaptureAsync();
        Assert.That(mic.IsCapturing, Is.False);
    }

    [TestCase(TestName = "E2E: media.class = Audio/Source и node.virtual = true")]
    public async Task MicrophoneHasSourceMediaClass()
    {
        var settings = new VirtualMicrophoneSettings { Name = "E2E MediaClass Test" };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeDescription == "E2E MediaClass Test");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.MediaClass, Is.EqualTo("Audio/Source"));
        Assert.That(node.NodeVirtual, Is.EqualTo("true"));
    }

    [TestCase(TestName = "E2E: формат ноды — audio/raw, F32LE")]
    public async Task MicrophoneFormatIsAudioF32()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E Format Test",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeDescription == "E2E Format Test");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.FormatMediaType, Is.EqualTo("audio"));
        Assert.That(node.FormatMediaSubtype, Is.EqualTo("raw"));
        Assert.That(node.FormatName, Is.EqualTo("F32LE"));
        Assert.That(node.FormatRate, Is.EqualTo(48000));
        Assert.That(node.FormatChannels, Is.EqualTo(1));
    }

    [TestCase(TestName = "E2E: формат stereo — 2 канала в EnumFormat")]
    public async Task MicrophoneFormatStereoChannels()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 44100,
            Channels = 2,
            SampleFormat = AudioSampleFormat.S16,
            Name = "E2E Stereo Format",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeDescription == "E2E Stereo Format");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.FormatMediaType, Is.EqualTo("audio"));
        Assert.That(node.FormatChannels, Is.EqualTo(2));
        Assert.That(node.FormatRate, Is.EqualTo(44100));
    }

    [TestCase(TestName = "E2E: микрофон виден в PulseAudio (pactl list sources)")]
    public async Task MicrophoneVisibleInPulseAudio()
    {
        var settings = new VirtualMicrophoneSettings { Name = "E2E Pactl Test" };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 500);

        var sources = await GetPactlSourceNamesAsync();

        Assert.That(
            sources.Any(s => s.Contains(BuildExpectedNodeName(settings.Name), StringComparison.Ordinal)),
            Is.True,
            $"{BuildExpectedNodeName(settings.Name)} не найден среди pactl sources: [{string.Join(", ", sources)}]");
    }

    [TestCase(TestName = "E2E: микрофон исчезает из PulseAudio после Dispose")]
    public async Task MicrophoneDisappearsFromPulseAfterDispose()
    {
        var settings = new VirtualMicrophoneSettings { Name = "E2E Pactl Dispose" };

        var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 500);

        var sourcesBefore = await GetPactlSourceNamesAsync();
        Assert.That(
            sourcesBefore.Any(s => s.Contains(BuildExpectedNodeName(settings.Name), StringComparison.Ordinal)),
            Is.True,
            "Микрофон не найден в pactl до Dispose");

        await mic.DisposeAsync();
        await Task.Delay(millisecondsDelay: 500);

        var sourcesAfter = await GetPactlSourceNamesAsync();
        Assert.That(
            sourcesAfter.Any(s => s.Contains(BuildExpectedNodeName(settings.Name), StringComparison.Ordinal)),
            Is.False,
            "Микрофон всё ещё виден в pactl после Dispose");
    }

    [TestCase(TestName = "E2E: ручная проверка — синусоида 440Hz, 30 секунд")]
    [Explicit("Ручной тест: запустите и проверьте микрофон в pavucontrol (Recording)")]
    public async Task ManualBroadcastSineTone()
    {
        const int sampleRate = 48000;
        const int durationSeconds = 30;
        const int samplesPerFrame = 480; // 10ms at 48kHz
        const int framesPerSecond = 100;

        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = sampleRate,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "Atom Manual Test Mic",
            LatencyMs = 10,
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        var buffer = new byte[samplesPerFrame * 4];
        var totalFrames = durationSeconds * framesPerSecond;

        TestContext.Progress.WriteLine($"Микрофон: {mic.DeviceIdentifier}");
        TestContext.Progress.WriteLine(
            $"Формат: {sampleRate}Hz, mono, F32 | Тон: 440Hz | Длительность: {durationSeconds}s");
        TestContext.Progress.WriteLine(
            "Откройте pavucontrol → Recording для проверки");
        TestContext.Progress.WriteLine();

        for (var i = 0; i < totalFrames; i++)
        {
            FillSineTone(buffer, 440.0, sampleRate, i * samplesPerFrame);
            mic.WriteSamples(buffer);

            if (i % 50 == 0)
            {
                var elapsed = i * 10 / 1000.0;
                var dbRms = AudioMeter.ToDecibels(mic.CurrentLevel);
                TestContext.Progress.WriteLine(
                    $"  [{elapsed,5:F1}s]  Peak: {mic.PeakLevel:F3}  " +
                    $"RMS: {mic.CurrentLevel:F3} ({dbRms:F1} dB)  " +
                    $"Hold: {mic.PeakHold.HoldPeak:F3}");
            }

            await Task.Delay(millisecondsDelay: 10);
        }

        await mic.StopCaptureAsync();
        TestContext.Progress.WriteLine();
        TestContext.Progress.WriteLine("Трансляция завершена.");
    }

    [TestCase(TestName = "E2E: два микрофона одновременно видны как разные ноды")]
    public async Task TwoMicrophonesSimultaneously()
    {
        var settings1 = new VirtualMicrophoneSettings { Name = "E2E Mic 1" };
        var settings2 = new VirtualMicrophoneSettings { Name = "E2E Mic 2" };

        await using var mic1 = await VirtualMicrophone.CreateAsync(settings1);
        await using var mic2 = await VirtualMicrophone.CreateAsync(settings2);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();

        Assert.That(
            nodes.Any(n => n.NodeDescription == "E2E Mic 1"),
            Is.True,
            "Микрофон 1 не найден");

        Assert.That(
            nodes.Any(n => n.NodeDescription == "E2E Mic 2"),
            Is.True,
            "Микрофон 2 не найден");
    }

    [TestCase(TestName = "E2E: DeviceId связывает микрофон через node.group")]
    public async Task DeviceIdLinksViaNodeGroup()
    {
        var settings = new VirtualMicrophoneSettings
        {
            Name = "E2E DeviceId Mic",
            DeviceId = "shared-device-001",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeDescription == "E2E DeviceId Mic");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.NodeName, Is.EqualTo("atom.microphone.shared-device-001"));
        Assert.That(node!.DeviceId, Is.EqualTo("shared-device-001"));
        Assert.That(node.NodeGroup, Is.EqualTo("shared-device-001"));
    }

    [TestCase(TestName = "E2E: камера и микрофон с общим DeviceId публикуются как связанная пара")]
    public async Task CameraAndMicrophoneShareDeviceGroup()
    {
        const string deviceId = "shared-av-001";

        var cameraSettings = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E Paired Camera",
            DeviceId = deviceId,
        };

        var micSettings = new VirtualMicrophoneSettings
        {
            Name = "E2E Paired Mic",
            DeviceId = deviceId,
        };

        await using var camera = await VirtualCamera.CreateAsync(cameraSettings);
        await using var mic = await VirtualMicrophone.CreateAsync(micSettings);
        await Task.Delay(millisecondsDelay: 300);

        var nodes = await GetPipeWireNodesAsync();
        var cameraNode = nodes.FirstOrDefault(n => n.NodeDescription == cameraSettings.Name);
        var microphoneNode = nodes.FirstOrDefault(n => n.NodeDescription == micSettings.Name);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(cameraNode, Is.Not.Null, "Нода камеры не найдена");
        Assert.That(microphoneNode, Is.Not.Null, "Нода микрофона не найдена");
        Assert.That(cameraNode!.NodeName, Is.EqualTo("atom.camera.shared-av-001"));
        Assert.That(cameraNode.NodeGroup, Is.EqualTo(deviceId));
        Assert.That(microphoneNode!.NodeName, Is.EqualTo("atom.microphone.shared-av-001"));
        Assert.That(microphoneNode.NodeGroup, Is.EqualTo(deviceId));
    }

    [TestCase(TestName = "E2E: микрофон виден через wpctl status")]
    public async Task MicrophoneVisibleInWpctlStatus()
    {
        if (!ProcessCommandHelpers.IsCommandAvailable("wpctl"))
        {
            Assert.Ignore("wpctl не найден.");
        }

        var settings = new VirtualMicrophoneSettings
        {
            Name = "E2E Wpctl Mic",
            DeviceId = "wpctl-mic-001",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 500);

        var output = await ProcessCommandHelpers.RunProcessAsync("wpctl", "status -n");

        Assert.That(output, Does.Contain("atom.microphone.wpctl-mic-001"));
    }

    [TestCase(TestName = "E2E: связанная camera+mic пара одновременно видна во внешних consumers")]
    public async Task PairedCameraAndMicrophoneVisibleToConsumers()
    {
        if (!ProcessCommandHelpers.IsCommandAvailable("wpctl"))
        {
            Assert.Ignore("wpctl не найден.");
        }

        if (!ProcessCommandHelpers.IsCommandAvailable("gst-device-monitor-1.0"))
        {
            Assert.Ignore("gst-device-monitor-1.0 не найден.");
        }

        const string deviceId = "shared-av-consumer-001";

        var cameraSettings = new VirtualCameraSettings
        {
            Width = 640,
            Height = 480,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E Consumer Pair Camera",
            DeviceId = deviceId,
        };

        var micSettings = new VirtualMicrophoneSettings
        {
            Name = "E2E Consumer Pair Mic",
            DeviceId = deviceId,
        };

        await using var camera = await VirtualCamera.CreateAsync(cameraSettings);
        await using var mic = await VirtualMicrophone.CreateAsync(micSettings);
        await camera.StartCaptureAsync();
        await mic.StartCaptureAsync();

        var frame = new byte[cameraSettings.Width * cameraSettings.Height * 4];
        Array.Fill(frame, (byte)0x60);
        camera.WriteFrame(frame);

        var samples = new byte[480 * 4];
        FillSineTone(samples, 440.0, 48000, startSample: 0);
        mic.WriteSamples(samples);

        await Task.Delay(millisecondsDelay: 500);

        var wpctlOutput = await ProcessCommandHelpers.RunProcessAsync("wpctl", "status -n");
        var gstOutput = await ProcessCommandHelpers.RunProcessAsync("timeout", "6s gst-device-monitor-1.0 Video/Source", includeStandardError: true);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(wpctlOutput, Does.Contain("atom.microphone.shared-av-consumer-001"));
        Assert.That(gstOutput, Does.Contain("atom.camera.shared-av-consumer-001"));
        Assert.That(gstOutput, Does.Contain("node.group = shared-av-consumer-001"));
        Assert.That(gstOutput, Does.Contain("device.name = atom.device.shared-av-consumer-001"));
    }

    [TestCase(TestName = "E2E: Volume устанавливается и считывается")]
    public async Task VolumeSetGet()
    {
        var settings = new VirtualMicrophoneSettings { Name = "E2E Volume" };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 200);

        mic.Volume = 0.75f;
        var vol = mic.Volume;
        Assert.That(vol, Is.EqualTo(0.75f).Within(0.01f));

        mic.Volume = 0.0f;
        Assert.That(mic.Volume, Is.Zero.Within(0.01f));

        mic.Volume = 1.0f;
        Assert.That(mic.Volume, Is.EqualTo(1.0f).Within(0.01f));

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: IsMuted переключает mute")]
    public async Task MuteToggle()
    {
        var settings = new VirtualMicrophoneSettings { Name = "E2E Mute" };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 200);

        mic.IsMuted = true;
        Assert.That(mic.IsMuted, Is.True);

        mic.IsMuted = false;
        Assert.That(mic.IsMuted, Is.False);

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: GainDb конвертирует dB → linear корректно")]
    public async Task GainDbConversion()
    {
        var settings = new VirtualMicrophoneSettings { Name = "E2E Gain" };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 200);

        // 0 dB → volume = 1.0
        mic.GainDb = 0.0;
        Assert.That(mic.Volume, Is.EqualTo(1.0f).Within(0.01f));

        // -6 dB → volume ≈ 0.501
        mic.GainDb = -6.0;
        Assert.That(mic.Volume, Is.EqualTo(0.501f).Within(0.02f));

        // -∞ dB → volume = 0.0
        mic.GainDb = double.NegativeInfinity;
        Assert.That(mic.Volume, Is.Zero.Within(0.01f));

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: Volume clamping — значения за пределами [0,1]")]
    public async Task VolumeClamp()
    {
        var settings = new VirtualMicrophoneSettings { Name = "E2E Clamp" };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 200);

        mic.Volume = 1.5f;
        Assert.That(mic.Volume, Is.LessThanOrEqualTo(1.0f));

        mic.Volume = -0.5f;
        Assert.That(mic.Volume, Is.GreaterThanOrEqualTo(0.0f));

        await mic.StopCaptureAsync();
    }

    // --- WriteSamples(string path) ---

    [TestCase(TestName = "E2E: WriteSamples из WAV-файла записывает аудио")]
    public async Task WriteSamplesFromWavWritesAudio()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E WAV Write",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        var wavPath = Path.GetTempFileName();
        try
        {
            var pcm = new byte[480 * 4]; // 10 мс при 48000 Hz, F32
            FillSineTone(pcm, 440.0, 48000, 0);
            File.WriteAllBytes(wavPath, CreateWavBytes(48000, 1, 32, 3, pcm));

            mic.WriteSamples(wavPath);

            Assert.That(mic.CurrentLevel, Is.GreaterThan(0.0f));
            Assert.That(mic.PeakLevel, Is.GreaterThan(0.0f));
        }
        finally
        {
            File.Delete(wavPath);
        }

        await mic.StopCaptureAsync();
    }

    // --- WriteSamples(Stream) ---

    [TestCase(TestName = "E2E: WriteSamples из Stream записывает аудио")]
    public async Task WriteSamplesFromStreamWritesAudio()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E Stream Write",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        var pcm = new byte[480 * 4]; // 10 мс при 48000 Hz, F32
        FillSineTone(pcm, 440.0, 48000, 0);
        using var stream = new MemoryStream(CreateWavBytes(48000, 1, 32, 3, pcm));

        mic.WriteSamples(stream);

        Assert.That(mic.CurrentLevel, Is.GreaterThan(0.0f));
        Assert.That(mic.PeakLevel, Is.GreaterThan(0.0f));

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: WriteSamples(Stream) — несовпадение частоты → InvalidOperationException")]
    public async Task WriteSamplesStreamWrongSampleRateThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E Stream Rate Mismatch",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        using var stream = new MemoryStream(CreateWavBytes(44100, 1, 32, 3, new byte[441 * 4]));
        Assert.Throws<InvalidOperationException>(() => mic.WriteSamples(stream));

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: WriteSamples — несовпадение частоты → InvalidOperationException")]
    public async Task WriteSamplesWrongSampleRateThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E WAV Rate Mismatch",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        // WAV 44100 Hz, но микрофон настроен на 48000 Hz
        var wavPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(wavPath, CreateWavBytes(44100, 1, 32, 3, new byte[441 * 4]));
            Assert.Throws<InvalidOperationException>(() => mic.WriteSamples(wavPath));
        }
        finally
        {
            File.Delete(wavPath);
        }

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: WriteSamples — несовпадение каналов → InvalidOperationException")]
    public async Task WriteSamplesWrongChannelsThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E WAV Channel Mismatch",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        // WAV стерео, но микрофон настроен на монo
        var wavPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(wavPath, CreateWavBytes(48000, 2, 32, 3, new byte[480 * 2 * 4]));
            Assert.Throws<InvalidOperationException>(() => mic.WriteSamples(wavPath));
        }
        finally
        {
            File.Delete(wavPath);
        }

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: WriteSamples — несовпадение формата → InvalidOperationException")]
    public async Task WriteSamplesWrongFormatThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = AudioSampleFormat.F32,
            Name = "E2E WAV Format Mismatch",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        // WAV S16 (PCM int), но микрофон F32
        var wavPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(wavPath, CreateWavBytes(48000, 1, 16, 1, new byte[480 * 2]));
            Assert.Throws<InvalidOperationException>(() => mic.WriteSamples(wavPath));
        }
        finally
        {
            File.Delete(wavPath);
        }

        await mic.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: StreamFromAsync с пустым путём бросает ArgumentException")]
    public async Task StreamFromAsyncEmptyPathThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E StreamFrom Empty",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        Assert.ThrowsAsync<ArgumentException>(
            () => mic.StreamFromAsync(string.Empty));
    }

    [TestCase(TestName = "E2E: StreamFromAsync с неподдерживаемым расширением бросает NotSupportedException")]
    public async Task StreamFromAsyncUnsupportedFormatThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E StreamFrom Unsupported",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        Assert.ThrowsAsync<NotSupportedException>(
            () => mic.StreamFromAsync("/tmp/test.xyz"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Stream) с null бросает ArgumentNullException")]
    public async Task StreamFromAsyncNullStreamThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E StreamFrom Null Stream",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => mic.StreamFromAsync((Stream)null!, "mp4"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Stream) с неподдерживаемым форматом бросает NotSupportedException")]
    public async Task StreamFromAsyncStreamUnsupportedFormatThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E StreamFrom Stream Unsupported",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        using var ms = new MemoryStream([0x00, 0x01, 0x02]);
        Assert.ThrowsAsync<NotSupportedException>(
            () => mic.StreamFromAsync(ms, ".xyz"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Uri) с null бросает ArgumentNullException")]
    public async Task StreamFromAsyncNullUrlThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E StreamFrom Null URL",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => mic.StreamFromAsync((Uri)null!));
    }

    [TestCase(TestName = "E2E: StreamFromAsync для MP4 без зарегистрированного демуксера бросает NotSupportedException")]
    public async Task StreamFromAsyncNoDemuxerThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E StreamFrom No Demuxer",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        Assert.ThrowsAsync<NotSupportedException>(
            () => mic.StreamFromAsync("/tmp/test.mp4"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Stream) без зарегистрированного демуксера бросает NotSupportedException")]
    public async Task StreamFromAsyncStreamNoDemuxerThrows()
    {
        var settings = new VirtualMicrophoneSettings
        {
            SampleRate = 48000,
            Channels = 1,
            Name = "E2E StreamFrom Stream No Demuxer",
        };

        await using var mic = await VirtualMicrophone.CreateAsync(settings);
        await mic.StartCaptureAsync();

        using var ms = new MemoryStream([0x00, 0x01, 0x02]);
        Assert.ThrowsAsync<NotSupportedException>(
            () => mic.StreamFromAsync(ms, "wma"));
    }

    // --- Helpers ---

    private static void FillSineTone(byte[] buffer, double frequency, int sampleRate, int startSample)
    {
        for (var i = 0; i < buffer.Length / 4; i++)
        {
            var t = (startSample + i) / (double)sampleRate;
            var value = (float)(Math.Sin(2.0 * Math.PI * frequency * t) * 0.5);
            BitConverter.TryWriteBytes(buffer.AsSpan(i * 4), value);
        }
    }

    private static Task<List<PipeWireNodeSnapshot>> GetPipeWireNodesAsync()
    {
        return PipeWireSnapshotHelpers.GetNodesAsync();
    }

    private static async Task<List<string>> GetPactlSourceNamesAsync()
    {
        var output = await ProcessCommandHelpers.RunProcessAsync("pactl", "list sources short");
        var names = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                names.Add(parts[1]);
            }
        }

        return names;
    }

    private static string BuildExpectedNodeName(string value)
    {
        return "atom.microphone." + BuildExpectedSlug(value);
    }

    private static string BuildExpectedSlug(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        var previousWasSeparator = false;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
                continue;
            }

            if (length > 0 && !previousWasSeparator)
            {
                buffer[length++] = '-';
                previousWasSeparator = true;
            }
        }

        return new string(buffer[..length]).Trim('-');
    }

    private static byte[] CreateWavBytes(
        int sampleRate, int channels, int bitsPerSample, int audioFormat, byte[] pcmData)
    {
        const int fmtChunkSize = 16;
        var dataChunkSize = pcmData.Length;
        var totalSize = 4 + (8 + fmtChunkSize) + (8 + dataChunkSize);
        var wav = new byte[12 + (8 + fmtChunkSize) + (8 + dataChunkSize)];
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
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 16), sampleRate * channels * (bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 20), (short)(channels * (bitsPerSample / 8)));
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 22), (short)bitsPerSample);

        pos += 8 + fmtChunkSize;
        "data"u8.CopyTo(span.Slice(pos));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 4), dataChunkSize);
        pcmData.CopyTo(span.Slice(pos + 8));

        return wav;
    }
}
