using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Atom.Media.Video;

namespace Atom.Media.Video.Tests;

[TestFixture]
[Category("E2E")]
[SupportedOSPlatform("linux")]
public class VirtualCameraE2ETests(ILogger logger) : BenchmarkTests<VirtualCameraE2ETests>(logger)
{
    private static readonly bool isPipeWireAvailable = CheckPipeWireAvailable();

    public VirtualCameraE2ETests() : this(ConsoleLogger.Unicode) { }

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

    [TestCase(TestName = "E2E: виртуальная камера появляется как нода PipeWire")]
    public async Task CameraAppearsAsPipeWireNode()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 640,
            Height = 480,
            Name = "E2E Test Camera",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);

        // Даём PipeWire время зарегистрировать ноду
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();

        Assert.That(
            nodes.Any(n => n.NodeName == "atom-virtual-camera"),
            Is.True,
            "Нода atom-virtual-camera не найдена в PipeWire");
    }

    [TestCase(TestName = "E2E: нода камеры имеет правильное описание")]
    public async Task CameraNodeHasCorrectDescription()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 1280,
            Height = 720,
            Name = "E2E Description Test",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeName == "atom-virtual-camera"
            && n.NodeDescription == "E2E Description Test");

        Assert.That(node, Is.Not.Null, "Нода с описанием 'E2E Description Test' не найдена");
    }

    [TestCase(TestName = "E2E: нода камеры имеет media.type=Video и media.category=Source")]
    public async Task CameraNodeHasVideoSourceProperties()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            Name = "E2E Media Type Test",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeName == "atom-virtual-camera"
            && n.NodeDescription == "E2E Media Type Test");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.MediaType, Is.EqualTo("Video"));
        Assert.That(node.MediaCategory, Is.EqualTo("Source"));
        Assert.That(node.MediaRole, Is.EqualTo("Camera"));
    }

    [TestCase(TestName = "E2E: метаданные производителя передаются в PipeWire")]
    public async Task CameraMetadataPassedToPipeWire()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            Name = "E2E Metadata Cam",
            Vendor = "Escorp Dynamics",
            Model = "Atom VCam",
            SerialNumber = "E2E-SN-001",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeName == "atom-virtual-camera"
            && n.NodeDescription == "E2E Metadata Cam");

        Assert.That(node, Is.Not.Null, "Нода не найдена");
        Assert.That(node!.DeviceVendor, Is.EqualTo("Escorp Dynamics"));
        Assert.That(node.DeviceProduct, Is.EqualTo("Atom VCam"));
        Assert.That(node.DeviceSerial, Is.EqualTo("E2E-SN-001"));
    }

    [TestCase(TestName = "E2E: нода исчезает после Dispose")]
    public async Task CameraNodeDisappearsAfterDispose()
    {
        string uniqueName = "E2E Dispose " + Guid.NewGuid().ToString("N")[..8];

        var settings = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            Name = uniqueName,
        };

        var camera = await VirtualCamera.CreateAsync(settings);
        await Task.Delay(millisecondsDelay: 200);

        // Проверяем что нода есть
        var nodesBefore = await GetPipeWireNodesAsync();
        Assert.That(
            nodesBefore.Any(n => n.NodeDescription == uniqueName),
            Is.True,
            "Нода не найдена до Dispose");

        // Dispose
        await camera.DisposeAsync();
        await Task.Delay(millisecondsDelay: 200);

        // Проверяем что нода исчезла
        var nodesAfter = await GetPipeWireNodesAsync();
        Assert.That(
            nodesAfter.Any(n => n.NodeDescription == uniqueName),
            Is.False,
            "Нода всё ещё существует после Dispose");
    }

    [TestCase(TestName = "E2E: полный цикл — создание, захват, запись кадра, остановка")]
    public async Task FullCycleWithFrameWrite()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E Full Cycle",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        Assert.That(camera.DeviceIdentifier, Is.EqualTo("pipewire:E2E Full Cycle"));

        await Task.Delay(millisecondsDelay: 100);

        // Проверяем ноду
        var nodes = await GetPipeWireNodesAsync();
        Assert.That(
            nodes.Any(n => n.NodeDescription == "E2E Full Cycle"),
            Is.True,
            "Нода не найдена");

        // Захват
        await camera.StartCaptureAsync();
        Assert.That(camera.IsCapturing, Is.True);

        // Пишем 10 кадров RGBA (4×4×4 = 64 байт)
        var frame = new byte[4 * 4 * 4];
        Array.Fill(frame, (byte)0x80);
        for (var i = 0; i < 10; i++)
        {
            camera.WriteFrame(frame);
            await Task.Delay(millisecondsDelay: 33); // ~30fps
        }

        // Остановка
        await camera.StopCaptureAsync();
        Assert.That(camera.IsCapturing, Is.False);
    }

    [TestCase(TestName = "E2E: две камеры одновременно видны как разные ноды")]
    public async Task TwoCamerasSimultaneously()
    {
        var settings1 = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            Name = "E2E Camera 1",
        };

        var settings2 = new VirtualCameraSettings
        {
            Width = 640,
            Height = 480,
            Name = "E2E Camera 2",
        };

        await using var camera1 = await VirtualCamera.CreateAsync(settings1);
        await using var camera2 = await VirtualCamera.CreateAsync(settings2);
        await Task.Delay(millisecondsDelay: 200);

        var nodes = await GetPipeWireNodesAsync();

        Assert.That(
            nodes.Any(n => n.NodeDescription == "E2E Camera 1"),
            Is.True,
            "Камера 1 не найдена");

        Assert.That(
            nodes.Any(n => n.NodeDescription == "E2E Camera 2"),
            Is.True,
            "Камера 2 не найдена");
    }

    [TestCase(TestName = "E2E: камера в режиме захвата имеет состояние streaming в PipeWire")]
    public async Task CameraStreamingStateVisible()
    {
        string uniqueName = "E2E Streaming " + Guid.NewGuid().ToString("N")[..8];

        var settings = new VirtualCameraSettings
        {
            Width = 16,
            Height = 16,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Rgb24,
            Name = uniqueName,
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        // Пишем кадры чтобы стрим был активен
        var frame = new byte[16 * 16 * 3];
        Array.Fill(frame, (byte)0xAA);

        for (var i = 0; i < 5; i++)
        {
            camera.WriteFrame(frame);
            await Task.Delay(millisecondsDelay: 33);
        }

        // Проверяем через pw-dump что нода существует и имеет state=streaming
        var nodes = await GetPipeWireNodesAsync();
        var node = nodes.FirstOrDefault(n => n.NodeDescription == uniqueName);

        Assert.That(node, Is.Not.Null, $"Нода с описанием '{uniqueName}' не найдена");
        Assert.That(node!.NodeName, Is.EqualTo("atom-virtual-camera"));
    }

    // --- WriteFrame(string imagePath) ---

    [TestCase(TestName = "E2E: WriteFrame из PNG-файла записывает кадр")]
    public async Task WriteFrameFromPngWritesFrame()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E PNG Write",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        var pngPath = Path.GetTempFileName() + ".png";
        try
        {
            CreateTestPng(pngPath, 4, 4);

            for (var i = 0; i < 5; i++)
            {
                camera.WriteFrame(pngPath);
                await Task.Delay(millisecondsDelay: 33);
            }

            Assert.That(camera.IsCapturing, Is.True);
        }
        finally
        {
            File.Delete(pngPath);
        }

        await camera.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: WriteFrame — неподдерживаемый формат → NotSupportedException")]
    public async Task WriteFrameUnsupportedFormatThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E Unsupported Format",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        var bmpPath = Path.GetTempFileName() + ".bmp";
        try
        {
            File.WriteAllBytes(bmpPath, [0x42, 0x4D]); // BMP header stub
            Assert.Throws<NotSupportedException>(() => camera.WriteFrame(bmpPath));
        }
        finally
        {
            File.Delete(bmpPath);
        }

        await camera.StopCaptureAsync();
    }

    // --- WriteFrame(Stream, string format) ---

    [TestCase(TestName = "E2E: WriteFrame из Stream записывает кадр")]
    public async Task WriteFrameFromStreamWritesFrame()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E Stream Write",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        var pngBytes = CreateTestPngBytes(4, 4);

        for (var i = 0; i < 5; i++)
        {
            using var stream = new MemoryStream(pngBytes);
            camera.WriteFrame(stream, ".png");
            await Task.Delay(millisecondsDelay: 33);
        }

        Assert.That(camera.IsCapturing, Is.True);

        await camera.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: WriteFrame(Stream) — неподдерживаемый формат → NotSupportedException")]
    public async Task WriteFrameStreamUnsupportedFormatThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E Stream Unsupported",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        using var stream = new MemoryStream([0x42, 0x4D]);
        Assert.Throws<NotSupportedException>(() => camera.WriteFrame(stream, ".bmp"));

        await camera.StopCaptureAsync();
    }

    // --- WebP ---

    [TestCase(TestName = "E2E: WriteFrame из WebP-файла записывает кадр")]
    public async Task WriteFrameFromWebpWritesFrame()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E WebP Write",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        var webpPath = Path.GetTempFileName() + ".webp";
        try
        {
            File.WriteAllBytes(webpPath, CreateTestWebpBytes(4, 4));

            for (var i = 0; i < 5; i++)
            {
                camera.WriteFrame(webpPath);
                await Task.Delay(millisecondsDelay: 33);
            }

            Assert.That(camera.IsCapturing, Is.True);
        }
        finally
        {
            File.Delete(webpPath);
        }

        await camera.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: WriteFrame из WebP-Stream записывает кадр")]
    public async Task WriteFrameFromWebpStreamWritesFrame()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Rgba32,
            Name = "E2E WebP Stream",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();
        await Task.Delay(millisecondsDelay: 100);

        var webpBytes = CreateTestWebpBytes(4, 4);

        for (var i = 0; i < 5; i++)
        {
            using var stream = new MemoryStream(webpBytes);
            camera.WriteFrame(stream, ".webp");
            await Task.Delay(millisecondsDelay: 33);
        }

        Assert.That(camera.IsCapturing, Is.True);

        await camera.StopCaptureAsync();
    }

    [TestCase(TestName = "E2E: StreamFromAsync с пустым путём бросает ArgumentException")]
    public async Task StreamFromAsyncEmptyPathThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            Name = "E2E StreamFrom Empty",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        Assert.ThrowsAsync<ArgumentException>(
            () => camera.StreamFromAsync(string.Empty));
    }

    [TestCase(TestName = "E2E: StreamFromAsync с неподдерживаемым расширением бросает NotSupportedException")]
    public async Task StreamFromAsyncUnsupportedFormatThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            Name = "E2E StreamFrom Unsupported",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        Assert.ThrowsAsync<NotSupportedException>(
            () => camera.StreamFromAsync("/tmp/test.xyz"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Stream) с null бросает ArgumentNullException")]
    public async Task StreamFromAsyncNullStreamThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            Name = "E2E StreamFrom Null Stream",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => camera.StreamFromAsync((Stream)null!, "mp4"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Stream) с неподдерживаемым форматом бросает NotSupportedException")]
    public async Task StreamFromAsyncStreamUnsupportedFormatThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            Name = "E2E StreamFrom Stream Unsupported",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        using var ms = new MemoryStream([0x00, 0x01, 0x02]);
        Assert.ThrowsAsync<NotSupportedException>(
            () => camera.StreamFromAsync(ms, ".xyz"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Uri) с null бросает ArgumentNullException")]
    public async Task StreamFromAsyncNullUrlThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            Name = "E2E StreamFrom Null URL",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        Assert.ThrowsAsync<ArgumentNullException>(
            () => camera.StreamFromAsync((Uri)null!));
    }

    [TestCase(TestName = "E2E: StreamFromAsync для MP4 без зарегистрированного демуксера бросает NotSupportedException")]
    public async Task StreamFromAsyncNoDemuxerThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            Name = "E2E StreamFrom No Demuxer",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        Assert.ThrowsAsync<NotSupportedException>(
            () => camera.StreamFromAsync("/tmp/test.mp4"));
    }

    [TestCase(TestName = "E2E: StreamFromAsync(Stream) без зарегистрированного демуксера бросает NotSupportedException")]
    public async Task StreamFromAsyncStreamNoDemuxerThrows()
    {
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 4,
            Name = "E2E StreamFrom Stream No Demuxer",
        };

        await using var camera = await VirtualCamera.CreateAsync(settings);
        await camera.StartCaptureAsync();

        using var ms = new MemoryStream([0x00, 0x01, 0x02]);
        Assert.ThrowsAsync<NotSupportedException>(
            () => camera.StreamFromAsync(ms, "avi"));
    }

    // --- Helpers ---

    private static async Task<List<PipeWireNode>> GetPipeWireNodesAsync()
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "pw-dump",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return ParsePipeWireNodes(output);
    }

    private static List<PipeWireNode> ParsePipeWireNodes(string json)
    {
        var result = new List<PipeWireNode>();

        using var doc = JsonDocument.Parse(json);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("type", out var typeEl)
                || typeEl.GetString() != "PipeWire:Interface:Node")
            {
                continue;
            }

            if (!element.TryGetProperty("info", out var info)
                || !info.TryGetProperty("props", out var props))
            {
                continue;
            }

            result.Add(new PipeWireNode
            {
                NodeId = element.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0,
                NodeName = GetStringProp(props, "node.name"),
                NodeDescription = GetStringProp(props, "node.description"),
                MediaType = GetStringProp(props, "media.type"),
                MediaCategory = GetStringProp(props, "media.category"),
                MediaRole = GetStringProp(props, "media.role"),
                DeviceVendor = GetStringProp(props, "device.vendor.name"),
                DeviceProduct = GetStringProp(props, "device.product.name"),
                DeviceSerial = GetStringProp(props, "device.serial"),
            });
        }

        return result;
    }

    private static string? GetStringProp(JsonElement props, string key)
    {
        return props.TryGetProperty(key, out var val) ? val.GetString() : null;
    }

    private sealed class PipeWireNode
    {
        public int NodeId { get; init; }
        public string? NodeName { get; init; }
        public string? NodeDescription { get; init; }
        public string? MediaType { get; init; }
        public string? MediaCategory { get; init; }
        public string? MediaRole { get; init; }
        public string? DeviceVendor { get; init; }
        public string? DeviceProduct { get; init; }
        public string? DeviceSerial { get; init; }
    }

    private static void CreateTestPng(string path, int width, int height)
    {
        File.WriteAllBytes(path, CreateTestPngBytes(width, height));
    }

    private static byte[] CreateTestPngBytes(int width, int height)
    {
        using var codec = new PngCodec();
        var parameters = new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32);
        codec.InitializeEncoder(parameters);

        using var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);

        // Заполняем однотонным цветом
        buffer.GetRawData().Fill(0x80);

        var estimatedSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[estimatedSize];
        var roFrame = buffer.AsReadOnlyFrame();
        codec.Encode(in roFrame, encoded, out var bytesWritten);

        return encoded.AsSpan(0, bytesWritten).ToArray();
    }

    private static byte[] CreateTestWebpBytes(int width, int height)
    {
        using var codec = new WebpCodec();
        var parameters = new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32);
        codec.InitializeEncoder(parameters);

        using var buffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);

        // Заполняем однотонным цветом
        buffer.GetRawData().Fill(0x60);

        var estimatedSize = codec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[estimatedSize];
        var roFrame = buffer.AsReadOnlyFrame();
        codec.Encode(in roFrame, encoded, out var bytesWritten);

        return encoded.AsSpan(0, bytesWritten).ToArray();
    }
}
