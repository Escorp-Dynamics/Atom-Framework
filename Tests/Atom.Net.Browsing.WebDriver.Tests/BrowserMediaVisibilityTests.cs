using System.Diagnostics;
using System.Text.Json;
using Atom.Media;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Browsing.WebDriver;

namespace Tests;

[TestFixture, Category("GUI"), NonParallelizable]
public sealed class BrowserMediaVisibilityTests(ILogger logger) : BenchmarkTests<BrowserMediaVisibilityTests>(logger)
{
    private readonly ILogger log = logger;

    public override bool IsBenchmarkEnabled => default;

    public BrowserMediaVisibilityTests() : this(ConsoleLogger.Unicode) { }

    private static string ExtensionPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "Extension"));

    private static string AssetsPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "assets"));

    private static readonly bool isPipeWireAvailable = CheckPipeWireAvailable();
    private static readonly bool isFfmpegAvailable = CheckCommandAvailable("ffmpeg");

    private static IEnumerable<TestCaseData> CameraFileStreamCases()
    {
        yield return new TestCaseData("mp4", "test.mp4", 960, 540, true)
            .SetName("webcamtests.com: VirtualCamera стримит MP4 файл");
        yield return new TestCaseData("webm", "test.webm", 1920, 1080, true)
            .SetName("webcamtests.com: VirtualCamera стримит WebM файл");
        yield return new TestCaseData("png", "test.png", 1920, 1080, false)
            .SetName("webcamtests.com: VirtualCamera стримит PNG кадр как live stream");
        yield return new TestCaseData("webp", "test.webp", 1600, 955, false)
            .SetName("webcamtests.com: VirtualCamera стримит WebP кадр как live stream");
    }

    private static bool CheckPipeWireAvailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

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

    private static bool CheckCommandAvailable(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = "-version",
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

    [TestCase(TestName = "Chromium enumerateDevices видит virtual microphone")]
    public async Task ChromiumEnumeratesVirtualMicrophone()
    {
        var probe = await ProbeChromiumAsync(includeCamera: false, includeMicrophone: true);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(probe.SecureContext, Is.EqualTo("true"), "Discovery page должна быть secure context для mediaDevices API.");
        Assert.That(probe.PermissionText, Does.Contain("\"ok\":true"), "getUserMedia(audio) должен успешно открывать виртуальный микрофон.");
        Assert.That(probe.DevicesText, Does.Contain("audioinput"), "Browser должен возвращать хотя бы один audioinput.");
        Assert.That(probe.DevicesText, Does.Contain(probe.MicrophoneName!), "Browser должен видеть виртуальный микрофон по label.");
    }

    [TestCase(TestName = "OpenIsolatedTabAsync: VirtualMediaDevices добавляет alias audio/video и рабочий getUserMedia(audio/video)")]
    public async Task IsolatedTabVirtualMediaDevicesExposeAudioAndVideo()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("VirtualMediaDevices routing E2E предназначен для Linux/PipeWire.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }

        if (!isFfmpegAvailable)
        {
            Assert.Ignore("ffmpeg недоступен, file-backed camera streaming test пропускается.");
        }

        var (browserPath, extensionPath) = FindChromiumBrowser();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var audioLabel = "Tab Local Microphone " + suffix;
        var videoLabel = "Tab Local Camera " + suffix;

        var microphoneSettings = new VirtualMicrophoneSettings
        {
            Name = audioLabel,
            DeviceId = "tab-local-mic-" + suffix,
            Vendor = "Atom",
            Model = "Tab Local Microphone",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0103,
        };

        var cameraSettings = new VirtualCameraSettings
        {
            Name = videoLabel,
            DeviceId = "tab-local-cam-" + suffix,
            Width = 640,
            Height = 480,
            PixelFormat = VideoPixelFormat.Rgba32,
            Vendor = "Atom",
            Model = "Tab Local Camera",
            BusType = "usb",
            FormFactor = "webcam",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0102,
        };

        await using var microphone = await VirtualMicrophone.CreateAsync(microphoneSettings);
        await using var camera = await VirtualCamera.CreateAsync(cameraSettings);
        await microphone.StartCaptureAsync();
        await camera.StartCaptureAsync();

        using var pumpCancellation = new CancellationTokenSource();
        var microphonePump = PumpMicrophoneAsync(microphone, microphoneSettings.SampleRate, pumpCancellation.Token);
        var cameraPump = PumpCameraAsync(camera, cameraSettings, pumpCancellation.Token);

        try
        {
              await Task.Delay(millisecondsDelay: 1200);

            await using var browser = await WebDriverBrowser.LaunchAsync(
                browserPath,
                extensionPath,
                arguments:
                [
                    "--no-sandbox",
                    "--disable-features=Translate",
                    "--use-fake-ui-for-media-stream",
                ]);

            var page = await WaitForFirstTabAsync(browser);
            await Task.Delay(millisecondsDelay: 1200);

            var audioBrowserDeviceId = await ResolveBrowserDeviceIdAsync(page, "audioinput", audioLabel, includeCamera: false, includeMicrophone: true);

            Assert.That(audioBrowserDeviceId, Is.Not.Null.And.Not.Empty, "Не удалось определить browser-visible deviceId для виртуального микрофона.");

            var tab = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings
            {
                VirtualMediaDevices = new VirtualMediaDevicesSettings
                {
                    AudioInputLabel = audioLabel,
                    AudioInputBrowserDeviceId = audioBrowserDeviceId,
                    VideoInputLabel = videoLabel,
                },
            });

            await Task.Delay(millisecondsDelay: 500);

            var devicesText = await ReadDevicesSnapshotAsync(tab);
            var audioPermissionText = await ReadGetUserMediaSnapshotAsync(tab, includeAudio: true, includeVideo: false);
            var videoPermissionText = await ReadGetUserMediaSnapshotAsync(tab, includeAudio: false, includeVideo: true);

            using var scope = Assert.EnterMultipleScope();
            Assert.That(devicesText, Does.Contain("audioinput"));
            Assert.That(devicesText, Does.Contain("videoinput"));
            Assert.That(devicesText, Does.Contain(audioLabel));
            Assert.That(devicesText, Does.Contain(videoLabel));
            Assert.That(audioPermissionText, Does.Contain("\"ok\":true"));
            Assert.That(audioPermissionText, Does.Contain("\"kind\":\"audio\""));
            Assert.That(videoPermissionText, Does.Contain("\"ok\":true"));
            Assert.That(videoPermissionText, Does.Contain("\"kind\":\"video\""));
        }
        finally
        {
            pumpCancellation.Cancel();
            await Task.WhenAll(cameraPump, microphonePump);
        }
    }

    [TestCase(TestName = "OpenIsolatedTabAsync: VirtualMediaDevices остаются tab-local")]
    public async Task IsolatedTabsKeepVirtualMediaSeparated()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("VirtualMediaDevices routing E2E предназначен для Linux/PipeWire.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }

        var (browserPath, extensionPath) = FindChromiumBrowser();
        var leftSuffix = Guid.NewGuid().ToString("N")[..6];
        var rightSuffix = Guid.NewGuid().ToString("N")[..6];
        var leftAudio = "Left Mic " + leftSuffix;
        var leftVideo = "Left Cam " + leftSuffix;
        var rightAudio = "Right Mic " + rightSuffix;
        var rightVideo = "Right Cam " + rightSuffix;

        var leftMicrophoneSettings = new VirtualMicrophoneSettings
        {
            Name = leftAudio,
            DeviceId = "left-mic-" + leftSuffix,
            Vendor = "Atom",
            Model = "Left Tab Microphone",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0103,
        };

        var leftCameraSettings = new VirtualCameraSettings
        {
            Name = leftVideo,
            DeviceId = "left-cam-" + leftSuffix,
            Width = 320,
            Height = 240,
            PixelFormat = VideoPixelFormat.Rgba32,
            Vendor = "Atom",
            Model = "Left Tab Camera",
            BusType = "usb",
            FormFactor = "webcam",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0102,
        };

        var rightMicrophoneSettings = new VirtualMicrophoneSettings
        {
            Name = rightAudio,
            DeviceId = "right-mic-" + rightSuffix,
            Vendor = "Atom",
            Model = "Right Tab Microphone",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0103,
        };

        var rightCameraSettings = new VirtualCameraSettings
        {
            Name = rightVideo,
            DeviceId = "right-cam-" + rightSuffix,
            Width = 320,
            Height = 240,
            PixelFormat = VideoPixelFormat.Rgba32,
            Vendor = "Atom",
            Model = "Right Tab Camera",
            BusType = "usb",
            FormFactor = "webcam",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0102,
        };

        await using var leftMicrophone = await VirtualMicrophone.CreateAsync(leftMicrophoneSettings);
        await using var leftCamera = await VirtualCamera.CreateAsync(leftCameraSettings);
        await using var rightMicrophone = await VirtualMicrophone.CreateAsync(rightMicrophoneSettings);
        await using var rightCamera = await VirtualCamera.CreateAsync(rightCameraSettings);
        await leftMicrophone.StartCaptureAsync();
        await leftCamera.StartCaptureAsync();
        await rightMicrophone.StartCaptureAsync();
        await rightCamera.StartCaptureAsync();

        using var pumpCancellation = new CancellationTokenSource();
        var pumps = Task.WhenAll(
            PumpMicrophoneAsync(leftMicrophone, leftMicrophoneSettings.SampleRate, pumpCancellation.Token),
            PumpCameraAsync(leftCamera, leftCameraSettings, pumpCancellation.Token),
            PumpMicrophoneAsync(rightMicrophone, rightMicrophoneSettings.SampleRate, pumpCancellation.Token),
            PumpCameraAsync(rightCamera, rightCameraSettings, pumpCancellation.Token));

        try
        {
            await Task.Delay(millisecondsDelay: 1200);

            await using var browser = await WebDriverBrowser.LaunchAsync(
                browserPath,
                extensionPath,
                arguments:
                [
                    "--no-sandbox",
                    "--disable-features=Translate",
                    "--use-fake-ui-for-media-stream",
                ]);

            var page = await WaitForFirstTabAsync(browser);
            await Task.Delay(millisecondsDelay: 1200);

            var leftAudioBrowserDeviceId = await ResolveBrowserDeviceIdAsync(page, "audioinput", leftAudio, includeCamera: false, includeMicrophone: true);
            var rightAudioBrowserDeviceId = await ResolveBrowserDeviceIdAsync(page, "audioinput", rightAudio, includeCamera: false, includeMicrophone: true);

            Assert.Multiple(() =>
            {
                Assert.That(leftAudioBrowserDeviceId, Is.Not.Null.And.Not.Empty, "Не удалось определить browser-visible deviceId для левого микрофона.");
                Assert.That(rightAudioBrowserDeviceId, Is.Not.Null.And.Not.Empty, "Не удалось определить browser-visible deviceId для правого микрофона.");
            });

            var leftTab = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings
            {
                VirtualMediaDevices = new VirtualMediaDevicesSettings
                {
                    AudioInputLabel = leftAudio,
                    AudioInputBrowserDeviceId = leftAudioBrowserDeviceId,
                    VideoInputLabel = leftVideo,
                },
            });

            var rightTab = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings
            {
                VirtualMediaDevices = new VirtualMediaDevicesSettings
                {
                    AudioInputLabel = rightAudio,
                    AudioInputBrowserDeviceId = rightAudioBrowserDeviceId,
                    VideoInputLabel = rightVideo,
                },
            });

            await Task.Delay(millisecondsDelay: 500);

            var leftDevices = await ReadDevicesSnapshotAsync(leftTab);
            var rightDevices = await ReadDevicesSnapshotAsync(rightTab);

            using var scope = Assert.EnterMultipleScope();
            Assert.That(leftDevices, Does.Contain(leftAudio));
            Assert.That(leftDevices, Does.Contain(leftVideo));
            Assert.That(leftDevices, Does.Not.Contain(rightAudio));
            Assert.That(leftDevices, Does.Not.Contain(rightVideo));
            Assert.That(rightDevices, Does.Contain(rightAudio));
            Assert.That(rightDevices, Does.Contain(rightVideo));
            Assert.That(rightDevices, Does.Not.Contain(leftAudio));
            Assert.That(rightDevices, Does.Not.Contain(leftVideo));
        }
        finally
        {
            pumpCancellation.Cancel();
            await pumps;
        }
    }

    [TestCase(TestName = "webcamtests.com: VirtualMediaDevices видны сайту, открываются и дают live stream")]
    public async Task WebcamTestsDetectsVirtualCameraStream()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("VirtualMediaDevices routing E2E предназначен для Linux/PipeWire.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var videoLabel = "WebcamTests Camera " + suffix;

        var cameraSettings = new VirtualCameraSettings
        {
            Name = videoLabel,
            DeviceId = "webcamtests-cam-" + suffix,
            Width = 640,
            Height = 480,
            PixelFormat = VideoPixelFormat.Rgba32,
            Vendor = "Atom",
            Model = "Webcam Tests Camera",
            BusType = "usb",
            FormFactor = "webcam",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0102,
        };

        var (detectedSnapshot, streamingSnapshot, _) = await RunWebcamTestsCameraScenarioAsync(
            cameraSettings,
            (camera, cancellationToken) => PumpCameraAsync(camera, cameraSettings, cancellationToken));

        log.WriteLine(LogKind.Default, $"webcamtests detected={detectedSnapshot.RawJson}");
        log.WriteLine(LogKind.Default, $"webcamtests streaming={streamingSnapshot.RawJson}");
        log.WriteLine(LogKind.Default, $"webcamtests table={streamingSnapshot.TableJson}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(detectedSnapshot.SelectOptionsText, Does.Contain(videoLabel), "webcamtests.com должен увидеть виртуальную камеру в списке устройств.");
        Assert.That(streamingSnapshot.MediaNameText, Does.Contain(videoLabel), "webcamtests.com должен выбрать и отобразить имя виртуальной камеры.");
        Assert.That(streamingSnapshot.MetadataJson, Does.Contain("webcam-prop_media_name"), "Нужно выпарсить распознанные сайтом webcam metadata поля.");
        Assert.That(streamingSnapshot.MetadataJson, Does.Contain("webcam-prop_image_resolution"), "Нужно выпарсить распознанное сайтом webcam resolution поле.");
        Assert.That(streamingSnapshot.TableJson, Does.Contain("webcam-prop_media_name"), "Нужно выпарсить полную таблицу параметров webcam test page.");
        Assert.That(streamingSnapshot.StreamActive, Is.True, "Сайт должен получить активный MediaStream от virtual camera.");
        Assert.That(streamingSnapshot.VideoTrackState, Is.EqualTo("live"), "Видео-трек на странице должен быть live.");
        Assert.That(streamingSnapshot.VideoWidth, Is.GreaterThan(0), "video element должен получить ненулевую ширину потока.");
        Assert.That(streamingSnapshot.VideoHeight, Is.GreaterThan(0), "video element должен получить ненулевую высоту потока.");
        Assert.That(streamingSnapshot.ResolutionText, Is.Not.EqualTo("—"), "Сайт должен успеть определить разрешение потока.");
    }

    [TestCaseSource(nameof(CameraFileStreamCases))]
    public async Task WebcamTestsDetectsVirtualCameraFileStream(string formatName, string assetFileName, int width, int height, bool useVideoStreamApi)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("VirtualMediaDevices routing E2E предназначен для Linux/PipeWire.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }

        var assetPath = GetAssetPath(assetFileName);
        Assert.That(File.Exists(assetPath), Is.True, $"Media asset не найден: {assetPath}");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var videoLabel = $"WebcamTests {formatName.ToUpperInvariant()} Camera {suffix}";

        var cameraSettings = new VirtualCameraSettings
        {
            Name = videoLabel,
            DeviceId = $"webcamtests-{formatName}-cam-{suffix}",
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32,
            Vendor = "Atom",
            Model = $"Webcam Tests {formatName.ToUpperInvariant()} Camera",
            BusType = "usb",
            FormFactor = "webcam",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0102,
        };

        var (detectedSnapshot, streamingSnapshot, directProbe) = await RunWebcamTestsCameraScenarioAsync(
            cameraSettings,
            (camera, cancellationToken) => StreamCameraFromAssetAsync(camera, assetPath, width, height, useVideoStreamApi, cancellationToken),
            directVideoProbeLabel: videoLabel,
            directVideoProbeWidth: width,
            directVideoProbeHeight: height);
        (RgbaSample TopLeft, RgbaSample Center)? expectedStillImageSamples = IsStillImageFormat(formatName)
            ? await ReadExpectedStillImageSamplesAsync(assetPath, width, height)
            : null;

        var resolutionValue = GetTableValue(streamingSnapshot.TableJson, "webcam-prop_image_resolution") ?? string.Empty;

        log.WriteLine(LogKind.Default, $"webcamtests file[{formatName}] asset={assetPath}");
        log.WriteLine(LogKind.Default, $"webcamtests file[{formatName}] detected={detectedSnapshot.RawJson}");
        log.WriteLine(LogKind.Default, $"webcamtests file[{formatName}] streaming={streamingSnapshot.RawJson}");
        log.WriteLine(LogKind.Default, $"webcamtests file[{formatName}] table={streamingSnapshot.TableJson}");
        log.WriteLine(LogKind.Default, $"webcamtests file[{formatName}] directProbe={directProbe?.RawJson ?? string.Empty}");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(detectedSnapshot.SelectOptionsText, Does.Contain(videoLabel), $"webcamtests.com должен увидеть виртуальную камеру для {formatName} в списке устройств.");
        Assert.That(streamingSnapshot.MediaNameText, Does.Contain(videoLabel), $"webcamtests.com должен выбрать и отобразить имя virtual camera для {formatName}.");
        Assert.That(streamingSnapshot.StreamActive, Is.True, $"Сайт должен получить активный MediaStream для {formatName}.");
        Assert.That(streamingSnapshot.VideoTrackState, Is.EqualTo("live"), $"Видео-трек для {formatName} должен быть live.");
        Assert.That(streamingSnapshot.VideoWidth, Is.GreaterThan(0), $"webcamtests video element должен получить ненулевую ширину потока для {formatName}.");
        Assert.That(streamingSnapshot.VideoHeight, Is.GreaterThan(0), $"webcamtests video element должен получить ненулевую высоту потока для {formatName}.");
        Assert.That(resolutionValue, Is.Not.EqualTo("—"), $"webcamtests.com должна определить итоговое разрешение для {formatName}.");
        Assert.That(streamingSnapshot.TableJson, Does.Contain("webcam-prop_video_standard"), $"Нужно выпарсить итоговую таблицу параметров webcam test page для {formatName}.");
        Assert.That(directProbe, Is.Not.Null, $"Нужен прямой browser-side probe для {formatName}.");
        Assert.That(directProbe!.Ok, Is.True, $"Прямой browser-side getUserMedia probe должен открыть {formatName} camera с exact constraints. raw={directProbe.RawJson}");
        Assert.That(directProbe.Label, Does.Contain(videoLabel), $"Прямой probe должен вернуть track с ожидаемым label для {formatName}.");
        Assert.That(directProbe.StreamActive, Is.True, $"Прямой probe должен вернуть active MediaStream для {formatName}.");
        Assert.That(directProbe.VideoTrackState, Is.EqualTo("live"), $"Прямой probe должен вернуть live video track для {formatName}.");
        Assert.That(directProbe.VideoWidth, Is.EqualTo(width), $"Прямой probe должен получить ожидаемую ширину потока для {formatName}.");
        Assert.That(directProbe.VideoHeight, Is.EqualTo(height).Within(1), $"Прямой probe должен получить ожидаемую высоту потока для {formatName}.");
        Assert.That(directProbe.SettingsWidth, Is.EqualTo(width), $"Track settings width должна совпадать для {formatName}.");
        Assert.That(directProbe.SettingsHeight, Is.EqualTo(height).Within(1), $"Track settings height должна совпадать для {formatName}.");

        if (expectedStillImageSamples is not null)
        {
            var expectedTopLeftSample = expectedStillImageSamples.Value.TopLeft;
            var expectedCenterSample = expectedStillImageSamples.Value.Center;

            Assert.That(streamingSnapshot.TopLeftSample, Is.Not.Null, $"webcamtests video element должен вернуть top-left pixel sample для {formatName}.");
            Assert.That(streamingSnapshot.CenterSample, Is.Not.Null, $"webcamtests video element должен вернуть center pixel sample для {formatName}.");
            Assert.That(streamingSnapshot.TopLeftSample!.Matches(expectedTopLeftSample), Is.True,
                $"webcamtests video element должен показывать реальный {formatName.ToUpperInvariant()} top-left pixel, а не заглушку. expected={expectedTopLeftSample}, actual={streamingSnapshot.TopLeftSample}, raw={streamingSnapshot.RawJson}");
            Assert.That(streamingSnapshot.CenterSample!.Matches(expectedCenterSample), Is.True,
                $"webcamtests video element должен показывать реальный {formatName.ToUpperInvariant()} center pixel, а не заглушку. expected={expectedCenterSample}, actual={streamingSnapshot.CenterSample}, raw={streamingSnapshot.RawJson}");
            Assert.That(directProbe.TopLeftSample, Is.Not.Null, $"Прямой probe должен вернуть top-left pixel sample для {formatName}.");
            Assert.That(directProbe.CenterSample, Is.Not.Null, $"Прямой probe должен вернуть center pixel sample для {formatName}.");
            Assert.That(directProbe.TopLeftSample!.Matches(expectedTopLeftSample), Is.True,
                $"Прямой probe должен показывать реальный {formatName.ToUpperInvariant()} top-left pixel. expected={expectedTopLeftSample}, actual={directProbe.TopLeftSample}, raw={directProbe.RawJson}");
            Assert.That(directProbe.CenterSample!.Matches(expectedCenterSample), Is.True,
                $"Прямой probe должен показывать реальный {formatName.ToUpperInvariant()} center pixel. expected={expectedCenterSample}, actual={directProbe.CenterSample}, raw={directProbe.RawJson}");
        }
    }

    [TestCase(TestName = "mictests.com: PipeWire VirtualMicrophone виден сайту и даёт слышимый audio capture")]
    public async Task MicTestsDetectsVirtualMicrophoneStream()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("mictests PipeWire E2E предназначен для Linux.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }

        var (browserPath, extensionPath) = FindChromiumBrowser();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var audioLabel = "MicTests Microphone " + suffix;
        var deviceId = "mictests-browser-" + suffix;

        var microphoneSettings = new VirtualMicrophoneSettings
        {
            Name = audioLabel,
            DeviceId = deviceId,
            SampleRate = 48000,
            Channels = 1,
            SampleFormat = Atom.Media.Audio.AudioSampleFormat.S16,
            Vendor = "Atom",
            Model = "Browser Visible Microphone",
            UsbVendorId = 0x1d6b,
            UsbProductId = 0x0103,
        };

        await using var microphone = await VirtualMicrophone.CreateAsync(microphoneSettings);
        await microphone.StartCaptureAsync();
        using var pumpCancellation = new CancellationTokenSource();
        var microphonePump = PumpMicrophoneS16Async(microphone, microphoneSettings.SampleRate, pumpCancellation.Token);

        try
        {
            await Task.Delay(millisecondsDelay: 1200);

            await using var browser = await WebDriverBrowser.LaunchAsync(
                browserPath,
                extensionPath,
                arguments:
                [
                    "--no-sandbox",
                    "--disable-features=Translate",
                    "--use-fake-ui-for-media-stream",
                ]);

            var tab = await WaitForFirstTabAsync(browser);
            await Task.Delay(millisecondsDelay: 1200);
            await tab.NavigateAsync(new Uri("https://mictests.com/"));

            var detectedSnapshot = await WaitForMicTestsSnapshotAsync(
                tab,
                snapshot => snapshot.SelectOptionsText.Contains(audioLabel, StringComparison.Ordinal)
                    || snapshot.VisibleNoticeText.Contains("Press \"Test my mic\"", StringComparison.Ordinal)
                    || snapshot.VisibleNoticeText.Contains("Several microphones were detected", StringComparison.Ordinal),
                TimeSpan.FromSeconds(20));

            var launchedSnapshot = await StartMicTestsAsync(tab, audioLabel, detectedSnapshot);
            Assert.That(
                launchedSnapshot.StreamActive
                || launchedSnapshot.VisibleNoticeText.Contains("recording your voice", StringComparison.OrdinalIgnoreCase)
                || launchedSnapshot.VisibleNoticeText.Contains("completed successfully", StringComparison.OrdinalIgnoreCase)
                || launchedSnapshot.VisibleNoticeText.Contains("checking your microphone", StringComparison.OrdinalIgnoreCase),
                Is.True,
                "mictests.com не перешёл в phase записи после выбора устройства и клика по кнопке запуска.");

            var streamingSnapshot = await WaitForMicTestsSnapshotAsync(
                tab,
                snapshot => snapshot.HasCompletedTableAnalysis(audioLabel)
                    && !snapshot.VisibleNoticeText.Contains("could not capture any sounds", StringComparison.OrdinalIgnoreCase)
                    && !snapshot.VisibleNoticeText.Contains("audio track is paused", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(45));

            log.WriteLine(LogKind.Default, $"mictests detected={detectedSnapshot.RawJson}");
            log.WriteLine(LogKind.Default, $"mictests streaming={streamingSnapshot.RawJson}");
            log.WriteLine(LogKind.Default, $"mictests table={streamingSnapshot.TableJson}");

            var realProbe = await ProbeVirtualAudioStreamAsync(tab, audioLabel);
            log.WriteLine(LogKind.Default, $"mictests directProbe={realProbe.RawJson}");

            using var scope = Assert.EnterMultipleScope();
            Assert.That(detectedSnapshot.SelectOptionsText, Does.Contain(audioLabel), "mictests.com должен увидеть PipeWire виртуальный микрофон в списке устройств.");
            Assert.That(streamingSnapshot.MediaNameText, Does.Contain(audioLabel), "mictests.com должен выбрать и отобразить имя PipeWire виртуального микрофона.");
            Assert.That(streamingSnapshot.MetadataJson, Does.Contain("mic-prop_media_name"), "Нужно выпарсить распознанные сайтом microphone metadata поля.");
            Assert.That(streamingSnapshot.MetadataJson, Does.Contain("mic-prop_audio_sampleRate"), "Нужно выпарсить распознанный сайтом sample rate.");
            Assert.That(streamingSnapshot.MetadataJson, Does.Contain("mic-prop_audio_channelCount"), "Нужно выпарсить распознанное сайтом число каналов.");
            Assert.That(streamingSnapshot.TableJson, Does.Contain("mic-prop_media_name"), "Нужно выпарсить полную таблицу параметров microphone test page.");
            Assert.That(streamingSnapshot.VisibleNoticeText, Does.Not.Contain("could not capture any sounds").IgnoreCase, "Сайт не должен считать PipeWire виртуальный микрофон немым.");
            Assert.That(streamingSnapshot.VisibleNoticeText, Does.Not.Contain("audio track is paused").IgnoreCase, "Сайт не должен считать PipeWire виртуальный микрофон paused.");
            Assert.That(streamingSnapshot.SampleRateText, Is.Not.EqualTo("—"), "Сайт должен определить sample rate потока.");
            Assert.That(streamingSnapshot.ChannelCountText, Is.Not.EqualTo("—"), "Сайт должен определить число каналов потока.");
            Assert.That(streamingSnapshot.VolumeText, Is.Not.EqualTo("—"), "Сайт должен зафиксировать audio analysis state.");
            Assert.That(realProbe.Label, Does.Contain(audioLabel), "Прямой post-check должен вернуть PipeWire виртуальный микрофон по ожидаемому label.");
            Assert.That(realProbe.AnalyserPeak, Is.GreaterThan(0.001d), "Page-side analyser должен видеть ненулевой пик сигнала.");
            Assert.That(realProbe.AnalyserRms, Is.GreaterThan(0.0001d), "Page-side analyser должен видеть ненулевой RMS сигнала.");
        }
        finally
        {
            pumpCancellation.Cancel();
            await microphonePump;
        }
    }

    [TestCase(TestName = "Chromium enumerateDevices видит PipeWire virtual camera")]
    [Explicit("Requires Linux Chromium/Brave raw PipeWire camera support in the current test environment.")]
    public async Task ChromiumEnumeratesPipeWireVirtualCamera()
    {
        var results = await ProbeChromiumAcrossLaunchProfilesAsync(includeCamera: true, includeMicrophone: false);

        var probe = results.FirstOrDefault(static candidate => candidate.IsSuccessful);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(probe, Is.Not.Null,
            "Хотя бы один raw Chromium launch profile должен вернуть одновременно videoinput, успешный getUserMedia(video) и ожидаемый label виртуальной камеры.");
        Assert.That(probe!.SecureContext, Is.EqualTo("true"), "Discovery page должна быть secure context для mediaDevices API.");
        Assert.That(probe.PermissionText, Does.Contain("\"ok\":true"), "getUserMedia(video) должен успешно открывать виртуальную камеру.");
        Assert.That(probe.DevicesText, Does.Contain("videoinput"), "Browser должен возвращать хотя бы один videoinput.");
        Assert.That(probe.PermissionText, Does.Contain(probe.CameraName!), "getUserMedia(video) должен вернуть track с ожидаемым label виртуальной камеры.");
    }

    [TestCase(TestName = "Chromium raw PipeWire virtual camera: launch profile matrix")]
    [Explicit("Raw Chromium PipeWire camera enumeration diagnostic matrix for Linux launch profiles.")]
    public async Task ChromiumEnumeratesPipeWireVirtualCameraAcrossLaunchProfiles()
    {
        var results = await ProbeChromiumAcrossLaunchProfilesAsync(includeCamera: true, includeMicrophone: false);

        var successfulProbe = results.FirstOrDefault(static probe => probe.IsSuccessful);
        Assert.That(successfulProbe, Is.Not.Null,
            "Ни один raw Chromium launch profile не дал одновременно videoinput + успешный getUserMedia(video) + ожидаемый camera label.");
    }

    [TestCase(TestName = "Chromium raw PipeWire virtual camera: profile order comparison")]
    [Explicit("Compares natural and reversed Chromium launch profile order to expose order-sensitive camera flakiness.")]
    public async Task ChromiumEnumeratesPipeWireVirtualCameraAcrossProfileOrders()
    {
        var profiles = BuildRawChromiumLaunchProfiles(includeCamera: true, includeMicrophone: false);
        var naturalResults = await ProbeChromiumAcrossLaunchProfilesAsync(
            includeCamera: true,
            includeMicrophone: false,
            profiles: profiles,
            orderLabel: "natural");
        var reversedResults = await ProbeChromiumAcrossLaunchProfilesAsync(
            includeCamera: true,
            includeMicrophone: false,
            profiles: [.. profiles.AsEnumerable().Reverse()],
            orderLabel: "reversed");

        using var scope = Assert.EnterMultipleScope();
        Assert.That(naturalResults.Any(static probe => probe.IsSuccessful), Is.True,
            "В natural порядке хотя бы один raw Chromium profile должен открыть виртуальную камеру.");
        Assert.That(reversedResults.Any(static probe => probe.IsSuccessful), Is.True,
            "В reversed порядке хотя бы один raw Chromium profile должен открыть виртуальную камеру.");
    }

    private async Task<List<MediaProbeResult>> ProbeChromiumAcrossLaunchProfilesAsync(
        bool includeCamera,
        bool includeMicrophone,
        IReadOnlyList<RawChromiumLaunchProfile>? profiles = null,
        string? orderLabel = null)
    {
        profiles ??= BuildRawChromiumLaunchProfiles(includeCamera, includeMicrophone);
        var results = new List<MediaProbeResult>(profiles.Count);

        if (!string.IsNullOrWhiteSpace(orderLabel))
        {
            log.WriteLine(LogKind.Default, $"rawProfileOrder={orderLabel}");
        }

        foreach (var profile in profiles)
        {
            var probe = await ProbeChromiumAsync(
                includeCamera: includeCamera,
                includeMicrophone: includeMicrophone,
                launchProfileName: profile.Name,
                extraLaunchArguments: profile.Arguments);

            results.Add(probe);
            log.WriteLine(LogKind.Default, $"rawCameraProfile[{probe.LaunchProfile}] args={probe.ArgumentsText}");
            log.WriteLine(LogKind.Default, $"rawCameraProfile[{probe.LaunchProfile}] secure={probe.SecureContext}");
            log.WriteLine(LogKind.Default, $"rawCameraProfile[{probe.LaunchProfile}] gum={probe.PermissionText}");
            log.WriteLine(LogKind.Default, $"rawCameraProfile[{probe.LaunchProfile}] devices={probe.DevicesText}");
        }

        return results;
    }

    private async Task<MediaProbeResult> ProbeChromiumAsync(
        bool includeCamera,
        bool includeMicrophone,
        string? launchProfileName = null,
        IReadOnlyList<string>? extraLaunchArguments = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("Browser media visibility E2E предназначен для Linux/PipeWire.");
        }

        var (browserPath, extensionPath) = FindChromiumBrowser();
        var browserIdentity = DescribeBrowser(browserPath);
        var slug = Guid.NewGuid().ToString("N")[..8];
        var deviceId = "browser-visible-" + slug;
        var cameraName = includeCamera ? "Browser Visible Camera " + slug : null;
        var microphoneName = includeMicrophone ? "Browser Visible Microphone " + slug : null;
        var pipeWireExpectedName = cameraName ?? microphoneName;

        VirtualCamera? camera = null;
        VirtualMicrophone? microphone = null;
        Task cameraPump = Task.CompletedTask;
        Task microphonePump = Task.CompletedTask;
        using var pumpCancellation = new CancellationTokenSource();

        try
        {
            if (includeCamera)
            {
                var cameraSettings = new VirtualCameraSettings
                {
                    Name = cameraName!,
                    DeviceId = deviceId,
                    Width = 320,
                    Height = 240,
                    PixelFormat = VideoPixelFormat.Rgba32,
                    Vendor = "Atom",
                    Model = "Browser Visibility Camera",
                    BusType = "usb",
                    FormFactor = "webcam",
                    UsbVendorId = 0x1d6b,
                    UsbProductId = 0x0102,
                };

                camera = await VirtualCamera.CreateAsync(cameraSettings);
                await camera.StartCaptureAsync();
                cameraPump = PumpCameraAsync(camera, cameraSettings, pumpCancellation.Token);
            }

            if (includeMicrophone)
            {
                var microphoneSettings = new VirtualMicrophoneSettings
                {
                    Name = microphoneName!,
                    DeviceId = deviceId,
                    Vendor = "Atom",
                    Model = "Browser Visibility Microphone",
                    UsbVendorId = 0x1d6b,
                    UsbProductId = 0x0103,
                };

                microphone = await VirtualMicrophone.CreateAsync(microphoneSettings);
                await microphone.StartCaptureAsync();
                microphonePump = PumpMicrophoneAsync(microphone, microphoneSettings.SampleRate, pumpCancellation.Token);
            }

            var initialPipeWireSnapshot = pipeWireExpectedName is not null
                ? CapturePipeWireRegistrySnapshot(pipeWireExpectedName, deviceId)
                : null;

            await Task.Delay(millisecondsDelay: includeCamera ? 2200 : 1200);

            var preLaunchPipeWireSnapshot = pipeWireExpectedName is not null
                ? CapturePipeWireRegistrySnapshot(pipeWireExpectedName, deviceId)
                : null;

            var requestedLaunchArguments = BuildRawChromiumLaunchArguments(includeCamera, includeMicrophone, extraLaunchArguments);
            var effectiveLaunchArguments = WebDriverBrowser.NormalizeChromiumLaunchArguments(requestedLaunchArguments);

            await using var browser = await WebDriverBrowser.LaunchAsync(
                browserPath,
                extensionPath,
                arguments: requestedLaunchArguments);

            var page = await WaitForFirstTabAsync(browser);
            var probeSnapshot = await WaitForChromiumProbeSnapshotAsync(
                page,
                includeCamera,
                includeMicrophone,
                expectedCameraLabel: cameraName,
                expectedMicrophoneLabel: microphoneName,
                timeout: TimeSpan.FromSeconds(15));
            var isSuccessful = probeSnapshot.SatisfiesExpectation(cameraName, microphoneName, includeCamera, includeMicrophone);
            var postFailurePipeWireSnapshot = !isSuccessful && pipeWireExpectedName is not null
                ? CapturePipeWireRegistrySnapshot(pipeWireExpectedName, deviceId)
                : null;

            log.WriteLine(LogKind.Default, $"rawProfile={launchProfileName ?? RawChromiumLaunchProfile.DefaultProfileName}");
            log.WriteLine(LogKind.Default, $"rawBrowser={browserIdentity}");
            log.WriteLine(LogKind.Default, $"rawRequestedArgs={string.Join(" ", requestedLaunchArguments)}");
            log.WriteLine(LogKind.Default, $"rawEffectiveArgs={string.Join(" ", effectiveLaunchArguments)}");
            if (initialPipeWireSnapshot is not null)
            {
                log.WriteLine(LogKind.Default, $"rawPipeWireInitial={initialPipeWireSnapshot.Summary}");
            }

            if (preLaunchPipeWireSnapshot is not null)
            {
                log.WriteLine(LogKind.Default, $"rawPipeWirePreLaunch={preLaunchPipeWireSnapshot.Summary}");
            }

            if (postFailurePipeWireSnapshot is not null)
            {
                log.WriteLine(LogKind.Default, $"rawPipeWirePostFailure={postFailurePipeWireSnapshot.Summary}");
            }

            log.WriteLine(LogKind.Default, $"isSecureContext={probeSnapshot.SecureContext}");
            log.WriteLine(LogKind.Default, $"getUserMedia={probeSnapshot.PermissionText}");
            log.WriteLine(LogKind.Default, $"enumerateDevices={probeSnapshot.DevicesText}");

            return new MediaProbeResult(
                launchProfileName ?? RawChromiumLaunchProfile.DefaultProfileName,
                string.Join(" ", effectiveLaunchArguments),
                cameraName,
                microphoneName,
                probeSnapshot.SecureContext,
                probeSnapshot.PermissionText,
                probeSnapshot.DevicesText);
        }
        finally
        {
            pumpCancellation.Cancel();
            await Task.WhenAll(cameraPump, microphonePump);

            if (camera is not null)
            {
                await camera.DisposeAsync();
            }

            if (microphone is not null)
            {
                await microphone.DisposeAsync();
            }
        }
    }

    private static string BuildGetUserMediaConstraints(bool includeCamera, bool includeMicrophone)
    {
        return (includeCamera, includeMicrophone) switch
        {
            (true, true) => "{audio:true,video:true}",
            (true, false) => "{video:true}",
            (false, true) => "{audio:true}",
            _ => throw new ArgumentException("Хотя бы один тип media должен быть включён."),
        };
    }

    private static List<string> BuildRawChromiumLaunchArguments(
        bool includeCamera,
        bool includeMicrophone,
        IReadOnlyList<string>? extraLaunchArguments)
    {
        var arguments = new List<string>
        {
            "--no-sandbox",
            "--disable-features=Translate",
            "--auto-accept-camera-and-microphone-capture",
        };

        if (extraLaunchArguments is not null)
        {
            arguments.AddRange(extraLaunchArguments);
        }

        if (includeCamera && !includeMicrophone)
        {
            WebDriverBrowser.MergeChromiumEnabledFeature(arguments, "PulseaudioLoopbackForScreenShare");
        }

        return arguments;
    }

    private static List<RawChromiumLaunchProfile> BuildRawChromiumLaunchProfiles(bool includeCamera, bool includeMicrophone)
    {
        _ = includeCamera;
        _ = includeMicrophone;

        List<RawChromiumLaunchProfile> profiles =
        [
            new(RawChromiumLaunchProfile.DefaultProfileName, []),
        ];

        if (IsWaylandSession())
        {
            profiles.Add(new RawChromiumLaunchProfile("wayland-native", ["--ozone-platform=wayland", "--ozone-platform-hint=wayland"]));
        }

        profiles.Add(new RawChromiumLaunchProfile("x11-forced", ["--ozone-platform=x11"]));
        return profiles;
    }

    private static bool IsWaylandSession()
        => OperatingSystem.IsLinux()
            && string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    private static PipeWireRegistrySnapshot? CapturePipeWireRegistrySnapshot(string expectedName, string deviceId)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pw-dump",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);

            if (process.ExitCode != 0)
            {
                return new PipeWireRegistrySnapshot($"pw-dump failed: {error.Trim()}");
            }

            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new PipeWireRegistrySnapshot("pw-dump returned non-array payload");
            }

            var matches = new List<PipeWireRegistryMatch>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("info", out var info)
                    || !info.TryGetProperty("props", out var props)
                    || props.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var mediaClass = GetJsonProperty(props, "media.class");
                var nodeDescription = GetJsonProperty(props, "node.description");
                var mediaName = GetJsonProperty(props, "media.name");
                var nodeGroup = GetJsonProperty(props, "node.group");
                var pipeWireDeviceId = GetJsonProperty(props, "device.id");
                var pipeWireDeviceName = GetJsonProperty(props, "device.name");

                if (!ContainsMatch(nodeDescription, expectedName)
                    && !ContainsMatch(mediaName, expectedName)
                    && !ContainsMatch(nodeGroup, deviceId)
                    && !ContainsMatch(pipeWireDeviceId, deviceId)
                    && !ContainsMatch(pipeWireDeviceName, deviceId))
                {
                    continue;
                }

                var id = element.TryGetProperty("id", out var idElement)
                    ? idElement.ToString()
                    : "?";
                matches.Add(new PipeWireRegistryMatch(
                    id,
                    mediaClass ?? "?",
                    nodeDescription ?? mediaName ?? "?",
                    nodeGroup ?? pipeWireDeviceId ?? pipeWireDeviceName ?? "?"));
            }

            return matches.Count == 0
                ? new PipeWireRegistrySnapshot($"no-match name={expectedName} deviceId={deviceId}")
                : new PipeWireRegistrySnapshot($"matches={matches.Count} {string.Join(" | ", matches.Select(static match => match.Summary))}");
        }
        catch (Exception exception)
        {
            return new PipeWireRegistrySnapshot($"pw-dump exception: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? GetJsonProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) ? value.ToString() : null;

    private static bool ContainsMatch(string? value, string expected)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(expected, StringComparison.Ordinal);

    private static async Task<ChromiumProbeSnapshot> WaitForChromiumProbeSnapshotAsync(
        WebDriverPage page,
        bool includeCamera,
        bool includeMicrophone,
        string? expectedCameraLabel,
        string? expectedMicrophoneLabel,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        ChromiumProbeSnapshot? lastSnapshot = null;

        while (DateTime.UtcNow < deadline)
        {
            lastSnapshot = await ReadChromiumProbeSnapshotAsync(page, includeCamera, includeMicrophone);
            if (lastSnapshot.SatisfiesExpectation(expectedCameraLabel, expectedMicrophoneLabel, includeCamera, includeMicrophone))
            {
                return lastSnapshot;
            }

            await Task.Delay(500);
        }

        return lastSnapshot ?? await ReadChromiumProbeSnapshotAsync(page, includeCamera, includeMicrophone);
    }

    private static async Task<ChromiumProbeSnapshot> ReadChromiumProbeSnapshotAsync(
        WebDriverPage page,
        bool includeCamera,
        bool includeMicrophone)
    {
        var secureContext = (await page.ExecuteAsync("window.isSecureContext ? 'true' : 'false'"))?.ToString() ?? string.Empty;
        var constraints = BuildGetUserMediaConstraints(includeCamera, includeMicrophone);
        var permissionText = (await page.ExecuteAsync(
            "navigator.mediaDevices.getUserMedia(" + constraints + ")"
            + ".then(function(stream){"
            + "var tracks=stream.getTracks().map(function(track){return {kind:track.kind,label:track.label,readyState:track.readyState};});"
            + "stream.getTracks().forEach(function(track){track.stop();});"
            + "return JSON.stringify({ok:true,tracks:tracks});"
            + "})"
            + ".catch(function(error){return JSON.stringify({ok:false,name:error.name,message:error.message});})"))?.ToString() ?? string.Empty;

        var devicesText = (await page.ExecuteAsync(
            "navigator.mediaDevices.enumerateDevices()"
            + ".then(function(devices){"
            + "return JSON.stringify(devices.map(function(device){"
            + "return {kind:device.kind,label:device.label,deviceId:device.deviceId,groupId:device.groupId};"
            + "}));"
            + "})"))?.ToString() ?? string.Empty;

        return new ChromiumProbeSnapshot(secureContext, permissionText, devicesText);
    }

    private static async Task PumpCameraAsync(
        VirtualCamera camera,
        VirtualCameraSettings settings,
        CancellationToken cancellationToken)
    {
        var frame = new byte[settings.Width * settings.Height * 4];
        for (var i = 0; i < frame.Length; i += 4)
        {
            frame[i] = 0x30;
            frame[i + 1] = 0x90;
            frame[i + 2] = 0xd0;
            frame[i + 3] = 0xff;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                camera.WriteFrame(frame);
                await Task.Delay(33, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsStillImageFormat(string formatName)
    {
        return string.Equals(formatName, "png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatName, "webp", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task StreamCameraFromAssetAsync(
        VirtualCamera camera,
        string assetPath,
        int width,
        int height,
        bool useVideoStreamApi,
        CancellationToken cancellationToken)
    {
        if (IsStillImageFormat(Path.GetExtension(assetPath).TrimStart('.')))
        {
            await StreamCameraStillImageAsync(camera, assetPath, width, height, frameRate: 30, cancellationToken).ConfigureAwait(false);
            return;
        }

        var frameSize = checked(width * height * 4);
        var arguments = BuildFfmpegAssetStreamingArguments(assetPath, width, height, frameRate: 30, useVideoStreamApi);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Не удалось запустить ffmpeg для asset '{assetPath}'.");
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var frame = new byte[frameSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await process.StandardOutput.BaseStream.ReadExactlyAsync(frame.AsMemory(0, frameSize), cancellationToken).ConfigureAwait(false);
                camera.WriteFrame(frame);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
            var stderr = await stderrTask.ConfigureAwait(false);
            throw new InvalidOperationException($"ffmpeg преждевременно завершил rawvideo stream для '{assetPath}'. stderr={stderr}");
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static async Task StreamCameraStillImageAsync(
        VirtualCamera camera,
        string assetPath,
        int width,
        int height,
        int frameRate,
        CancellationToken cancellationToken)
    {
        var streamParameters = new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32,
            FrameRate = frameRate,
        };

        var imageBytes = await File.ReadAllBytesAsync(assetPath, cancellationToken).ConfigureAwait(false);
        using var videoStream = VideoStream.FromStillImage(imageBytes, Path.GetExtension(assetPath), streamParameters);
        await camera.StreamFromAsync(videoStream, loop: true, cancellationToken).ConfigureAwait(false);
    }

    private static string GetAssetPath(string assetFileName)
        => Path.Combine(AssetsPath, assetFileName);

    private static string BuildFfmpegAssetStreamingArguments(string assetPath, int width, int height, int frameRate, bool useVideoStreamApi)
    {
        var inputArguments = useVideoStreamApi
            ? $"-stream_loop -1 -i {QuoteArgument(assetPath)}"
            : $"-loop 1 -framerate {frameRate} -i {QuoteArgument(assetPath)}";

        return $"-hide_banner -loglevel error -nostdin {inputArguments} -an -sn -dn -vf fps={frameRate},scale={width}:{height} -pix_fmt rgba -f rawvideo pipe:1";
    }

    private static string QuoteArgument(string value)
        => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    private static bool ResolutionContainsExpectedHeight(string resolutionValue, int expectedHeight)
        => resolutionValue.Contains(expectedHeight.ToString(), StringComparison.Ordinal)
            || resolutionValue.Contains((expectedHeight - 1).ToString(), StringComparison.Ordinal);

    private static async Task<(WebcamTestsSnapshot Detected, WebcamTestsSnapshot Streaming, DirectVideoProbeSnapshot? DirectProbe)> RunWebcamTestsCameraScenarioAsync(
        VirtualCameraSettings cameraSettings,
        Func<VirtualCamera, CancellationToken, Task> cameraStreamer,
        string? directVideoProbeLabel = null,
        int directVideoProbeWidth = 0,
        int directVideoProbeHeight = 0)
    {
        var (browserPath, extensionPath) = FindChromiumBrowser();

        await using var camera = await VirtualCamera.CreateAsync(cameraSettings);
        await camera.StartCaptureAsync();

        using var pumpCancellation = new CancellationTokenSource();
        var cameraPump = RunCameraStreamerAsync(camera, cameraStreamer, pumpCancellation.Token);

        try
        {
            await Task.Delay(millisecondsDelay: 1200);

            await using var browser = await WebDriverBrowser.LaunchAsync(
                browserPath,
                extensionPath,
                arguments:
                [
                    "--no-sandbox",
                    "--disable-features=Translate",
                    "--use-fake-ui-for-media-stream",
                ]);

            var page = await WaitForFirstTabAsync(browser);
            await Task.Delay(millisecondsDelay: 1200);

            var tab = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings
            {
                VirtualMediaDevices = new VirtualMediaDevicesSettings
                {
                    AudioInputEnabled = false,
                    VideoInputLabel = cameraSettings.Name,
                },
            });

            await tab.NavigateAsync(new Uri("https://webcamtests.com/"));

            var detectedSnapshot = await WaitForWebcamTestsSnapshotAsync(
                tab,
                snapshot => snapshot.SelectOptionsText.Contains(cameraSettings.Name, StringComparison.Ordinal)
                    || snapshot.VisibleNoticeText.Contains("Press \"Test my cam\"", StringComparison.Ordinal)
                    || snapshot.VisibleNoticeText.Contains("Several web cameras were detected", StringComparison.Ordinal),
                TimeSpan.FromSeconds(20));

            var launchedSnapshot = await StartWebcamTestsAsync(tab, cameraSettings.Name);
            Assert.That(
                launchedSnapshot.StreamActive
                || launchedSnapshot.MediaNameText.Contains(cameraSettings.Name, StringComparison.Ordinal)
                || launchedSnapshot.VisibleNoticeText.Contains("Testing your webcam", StringComparison.OrdinalIgnoreCase),
                Is.True,
                "webcamtests.com не перешёл в phase live test после клика по кнопке запуска.");

            var streamingSnapshot = await WaitForWebcamTestsSnapshotAsync(
                tab,
                snapshot => snapshot.StreamActive
                    && snapshot.VideoTrackState == "live"
                    && snapshot.VideoWidth > 0
                    && snapshot.VideoHeight > 0
                    && snapshot.HasCompletedTableAnalysis(cameraSettings.Name),
                TimeSpan.FromSeconds(45));

            var directProbe = string.IsNullOrWhiteSpace(directVideoProbeLabel)
                ? null
                : await ProbeVirtualVideoStreamAsync(tab, directVideoProbeLabel, directVideoProbeWidth, directVideoProbeHeight);

            return (detectedSnapshot, streamingSnapshot, directProbe);
        }
        finally
        {
            pumpCancellation.Cancel();
            await cameraPump;
        }
    }

    private static async Task RunCameraStreamerAsync(
        VirtualCamera camera,
        Func<VirtualCamera, CancellationToken, Task> cameraStreamer,
        CancellationToken cancellationToken)
    {
        try
        {
            await cameraStreamer(camera, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task PumpMicrophoneAsync(
        VirtualMicrophone microphone,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        const int samplesPerChunk = 480;
        var buffer = new byte[samplesPerChunk * sizeof(float)];
        var startSample = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                FillSineTone(buffer, 440.0, sampleRate, startSample);
                microphone.WriteSamples(buffer);
                startSample += samplesPerChunk;
                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task PumpMicrophoneS16Async(
        VirtualMicrophone microphone,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        const int samplesPerChunk = 480;
        var buffer = new byte[samplesPerChunk * sizeof(short)];
        var startSample = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                FillSineToneS16(buffer, 440.0, sampleRate, startSample);
                microphone.WriteSamples(buffer);
                startSample += samplesPerChunk;
                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void FillSineTone(byte[] buffer, double frequency, int sampleRate, int startSample)
    {
        for (var i = 0; i < buffer.Length / sizeof(float); i++)
        {
            var t = (startSample + i) / (double)sampleRate;
            var value = (float)(Math.Sin(2.0 * Math.PI * frequency * t) * 0.5);
            BitConverter.TryWriteBytes(buffer.AsSpan(i * sizeof(float)), value);
        }
    }

    private static void FillSineToneS16(byte[] buffer, double frequency, int sampleRate, int startSample)
    {
        for (var i = 0; i < buffer.Length / sizeof(short); i++)
        {
            var t = (startSample + i) / (double)sampleRate;
            var value = (short)(Math.Sin(2.0 * Math.PI * frequency * t) * short.MaxValue * 0.5);
            BitConverter.TryWriteBytes(buffer.AsSpan(i * sizeof(short)), value);
        }
    }

    private static (string BrowserPath, string ExtensionPath) FindChromiumBrowser()
    {
        var overridePath = Environment.GetEnvironmentVariable("ATOM_TEST_CHROMIUM_BROWSER")
            ?? Environment.GetEnvironmentVariable("ATOM_BROWSER_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            Assert.That(File.Exists(overridePath), Is.True, $"Путь браузера из env не существует: {overridePath}");
            Assert.That(Directory.Exists(ExtensionPath), Is.True, "Расширение не найдено.");
            return (overridePath, ExtensionPath);
        }

        string? browserPath = null;
        foreach (var candidate in (ReadOnlySpan<string>)[
            "/usr/bin/brave",
            "/usr/bin/opera",
            "/usr/bin/vivaldi-stable",
            "/usr/bin/microsoft-edge-stable",
            "/usr/bin/google-chrome-stable",
            "/usr/bin/yandex-browser-corporate",
            "/usr/bin/yandex-browser"])
        {
            if (File.Exists(candidate))
            {
                browserPath = candidate;
                break;
            }
        }

        Assert.That(browserPath, Is.Not.Null, "Chromium-браузер не найден.");
        Assert.That(Directory.Exists(ExtensionPath), Is.True, "Расширение не найдено.");
        return (browserPath!, ExtensionPath);
    }

    private static string DescribeBrowser(string browserPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = browserPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return browserPath;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(output)
                ? browserPath
                : browserPath + " :: " + output;
        }
        catch
        {
            return browserPath;
        }
    }

    private static async Task<WebDriverPage> WaitForFirstTabAsync(WebDriverBrowser browser)
    {
        var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
        browser.TabConnected += (_, e) =>
        {
            tcs.TrySetResult(e);
            return ValueTask.CompletedTask;
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        if (browser.ConnectionCount > 0)
        {
            return browser.GetAllPages().First();
        }

        var result = await tcs.Task.WaitAsync(cts.Token);
        var page = browser.GetPage(result.TabId)
            ?? throw new BridgeException($"Страница {result.TabId} не найдена.");

        using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await page.ExecuteAsync("1", warmupCts.Token);
        }
        catch
        {
        }

        return page;
    }

    private static async Task<string> ReadDevicesSnapshotAsync(WebDriverPage page)
    {
        var result = await page.ExecuteAsync(
            "navigator.mediaDevices.enumerateDevices()"
            + ".then(function(devices){"
            + "return JSON.stringify(devices.map(function(device){"
            + "return {kind:device.kind,label:device.label,deviceId:device.deviceId,groupId:device.groupId};"
            + "}));"
            + "})");

        return result?.ToString() ?? string.Empty;
    }

    private static async Task<string> ReadGetUserMediaSnapshotAsync(WebDriverPage page, bool includeAudio, bool includeVideo)
    {
        var constraints = BuildGetUserMediaConstraints(includeVideo, includeAudio);
        var result = await page.ExecuteAsync(
            "navigator.mediaDevices.getUserMedia(" + constraints + ")"
            + ".then(function(stream){"
            + "var tracks=stream.getTracks().map(function(track){return {kind:track.kind,readyState:track.readyState};});"
            + "stream.getTracks().forEach(function(track){track.stop();});"
            + "return JSON.stringify({ok:true,tracks:tracks});"
            + "})"
            + ".catch(function(error){return JSON.stringify({ok:false,name:error.name,message:error.message});})");

        return result?.ToString() ?? string.Empty;
    }

    private static async Task<string?> ResolveBrowserDeviceIdAsync(
        WebDriverPage page,
        string kind,
        string label,
        bool includeCamera,
        bool includeMicrophone)
    {
        var constraints = BuildGetUserMediaConstraints(includeCamera, includeMicrophone);
        await page.ExecuteAsync(
            "navigator.mediaDevices.getUserMedia(" + constraints + ")"
            + ".then(function(stream){stream.getTracks().forEach(function(track){track.stop();});return true;})"
            + ".catch(function(){return false;})");

        var result = await page.ExecuteAsync(
            "navigator.mediaDevices.enumerateDevices()"
            + ".then(function(devices){"
            + "var targetKind=" + ToJavaScriptStringLiteral(kind) + ";"
            + "var targetLabel=" + ToJavaScriptStringLiteral(label) + ";"
            + "var match=devices.find(function(device){return device.kind===targetKind&&device.label===targetLabel;});"
            + "return match?match.deviceId:'';"
            + "})");

        var deviceId = result?.ToString();
        return string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
    }

    private static async Task<bool> SelectOptionByTextAsync(WebDriverPage page, string selectId, string optionText)
    {
        var selectIdLiteral = ToJavaScriptStringLiteral(selectId);
        var optionTextLiteral = ToJavaScriptStringLiteral(optionText);
        var script = $$"""
            (() => {
                const select = document.getElementById({{selectIdLiteral}});
                const expectedText = {{optionTextLiteral}};
                if (!(select instanceof HTMLSelectElement)) {
                    return false;
                }

                const option = Array.from(select.options).find(candidate =>
                    (candidate.textContent || '').trim() === expectedText);
                if (!option) {
                    return false;
                }

                select.value = option.value;
                option.selected = true;
                select.dispatchEvent(new Event('input', { bubbles: true }));
                select.dispatchEvent(new Event('change', { bubbles: true }));
                return (select.options[select.selectedIndex]?.textContent || '').trim() === expectedText;
            })()
            """;

        return bool.TryParse((await ExecuteBrowserMediaScriptAsync(page, script, "выбор option в select"))?.ToString(), out var selected) && selected;
    }

    private static async Task<bool> DispatchElementClickAsync(WebDriverPage page, string elementId)
    {
        var elementIdLiteral = ToJavaScriptStringLiteral(elementId);
        var script = $$"""
            (() => {
                const element = document.getElementById({{elementIdLiteral}});
                if (!(element instanceof HTMLElement)) {
                    return false;
                }

                const rect = element.getBoundingClientRect();
                const clientX = rect.left + rect.width / 2;
                const clientY = rect.top + rect.height / 2;
                const mouseBase = {
                    bubbles: true,
                    cancelable: true,
                    composed: true,
                    clientX,
                    clientY,
                    button: 0,
                    buttons: 1,
                    detail: 1,
                    view: window,
                };

                element.dispatchEvent(new MouseEvent('mouseover', mouseBase));
                element.dispatchEvent(new MouseEvent('mousedown', mouseBase));
                element.dispatchEvent(new MouseEvent('mouseup', { ...mouseBase, buttons: 0 }));
                element.dispatchEvent(new MouseEvent('click', { ...mouseBase, buttons: 0 }));
                element.click();
                return true;
            })()
            """;

        return bool.TryParse((await ExecuteBrowserMediaScriptAsync(page, script, "dispatch click по elementId"))?.ToString(), out var clicked) && clicked;
    }

    private static async Task<bool> DispatchSelectorClickAsync(WebDriverPage page, string selector)
    {
        var script = $$"""
            (() => {
                const element = document.querySelector({{ToJavaScriptStringLiteral(selector)}});
                if (!element) {
                    return false;
                }

                element.scrollIntoView({ block: 'center', inline: 'center' });
                const rect = element.getBoundingClientRect();
                const clientX = rect.left + Math.max(1, rect.width) / 2;
                const clientY = rect.top + Math.max(1, rect.height) / 2;
                const mouseBase = {
                    bubbles: true,
                    cancelable: true,
                    composed: true,
                    clientX,
                    clientY,
                    button: 0,
                    buttons: 1,
                    detail: 1,
                    view: window,
                };

                element.dispatchEvent(new MouseEvent('mouseover', mouseBase));
                element.dispatchEvent(new MouseEvent('mousedown', mouseBase));
                element.dispatchEvent(new MouseEvent('mouseup', { ...mouseBase, buttons: 0 }));
                element.dispatchEvent(new MouseEvent('click', { ...mouseBase, buttons: 0 }));
                element.click();
                return true;
            })()
            """;

        return bool.TryParse((await ExecuteBrowserMediaScriptAsync(page, script, "dispatch click по selector"))?.ToString(), out var clicked) && clicked;
    }

    private static async Task<WebcamTestsSnapshot> StartWebcamTestsAsync(WebDriverPage page, string videoLabel)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        WebcamTestsSnapshot? lastSnapshot = null;

        while (DateTime.UtcNow < deadline)
        {
            var clicked = await DispatchElementClickAsync(page, "webcam-launcher");
            Assert.That(clicked, Is.True, "Не удалось диспатчить click по кнопке Test my cam.");

            await Task.Delay(1200);
            lastSnapshot = await ReadWebcamTestsSnapshotAsync(page);
            if (lastSnapshot.StreamActive
                || lastSnapshot.MediaNameText.Contains(videoLabel, StringComparison.Ordinal)
                || lastSnapshot.VisibleNoticeText.Contains("Testing your webcam", StringComparison.OrdinalIgnoreCase)
                || lastSnapshot.ResolutionText != "—")
            {
                return lastSnapshot;
            }
        }

        return lastSnapshot ?? await ReadWebcamTestsSnapshotAsync(page);
    }

    private static async Task<MicTestsSnapshot> StartMicTestsAsync(WebDriverPage page, string audioLabel, MicTestsSnapshot? initialSnapshot = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        MicTestsSnapshot? lastSnapshot = initialSnapshot;

        while (DateTime.UtcNow < deadline)
        {
            if (!IsMicSelectionAlreadyReady(lastSnapshot, audioLabel))
            {
                var selected = await SelectOptionByTextAsync(page, "mic-selecter", audioLabel);
                Assert.That(selected, Is.True, "Не удалось выбрать виртуальный микрофон в selector mictests.com.");
            }

            await ExecuteBrowserMediaScriptAsync(page, "document.getElementById('mic-launcher')?.focus(); 'focused';", "focus кнопки запуска микрофона");
            var clicked = await DispatchElementClickAsync(page, "mic-launcher");
            Assert.That(clicked, Is.True, "Не удалось диспатчить click по кнопке Test my mic.");

            await Task.Delay(1200);
            lastSnapshot = await ReadMicTestsSnapshotAsync(page);
            if (lastSnapshot.StreamActive
                || lastSnapshot.VisibleNoticeText.Contains("recording your voice", StringComparison.OrdinalIgnoreCase)
                || lastSnapshot.VisibleNoticeText.Contains("completed successfully", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(lastSnapshot.SampleRateText) && lastSnapshot.SampleRateText != "—"))
            {
                return lastSnapshot;
            }

            if (lastSnapshot.VisibleNoticeText.Contains("paused", StringComparison.OrdinalIgnoreCase)
                || lastSnapshot.VisibleNoticeText.Contains("permission", StringComparison.OrdinalIgnoreCase)
                || lastSnapshot.VisibleNoticeText.Contains("cannot stream audio", StringComparison.OrdinalIgnoreCase))
            {
                await DispatchSelectorClickAsync(page, "#mic-notices .text_forceQuery");
                await Task.Delay(800);
                lastSnapshot = await ReadMicTestsSnapshotAsync(page);
                if (lastSnapshot.StreamActive
                    || lastSnapshot.VisibleNoticeText.Contains("recording your voice", StringComparison.OrdinalIgnoreCase)
                    || lastSnapshot.VisibleNoticeText.Contains("completed successfully", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(lastSnapshot.SampleRateText) && lastSnapshot.SampleRateText != "—"))
                {
                    return lastSnapshot;
                }
            }
        }

        return lastSnapshot ?? await ReadMicTestsSnapshotAsync(page);
    }

    private static bool IsMicSelectionAlreadyReady(MicTestsSnapshot? snapshot, string audioLabel)
    {
        if (snapshot is null)
        {
            return false;
        }

        if (snapshot.SelectedOptionText.Contains(audioLabel, StringComparison.Ordinal))
        {
            return true;
        }

        return snapshot.SelectOptionsCount == 1
            && snapshot.SelectOptionsText.Contains(audioLabel, StringComparison.Ordinal);
    }

    private static string ToJavaScriptStringLiteral(string value)
    {
        return "'" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            + "'";
    }

    private static async Task<WebcamTestsSnapshot> WaitForWebcamTestsSnapshotAsync(
        WebDriverPage page,
        Func<WebcamTestsSnapshot, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        WebcamTestsSnapshot? lastSnapshot = null;

        while (DateTime.UtcNow < deadline)
        {
            lastSnapshot = await ReadWebcamTestsSnapshotAsync(page);
            if (predicate(lastSnapshot))
            {
                return lastSnapshot;
            }

            await Task.Delay(250);
        }

        return lastSnapshot ?? await ReadWebcamTestsSnapshotAsync(page);
    }

    private static async Task<WebcamTestsSnapshot> ReadWebcamTestsSnapshotAsync(WebDriverPage page)
    {
        var result = (await page.ExecuteAsync("""
            (() => {
                const visibleNotices = Array.from(document.querySelectorAll('#webcam-notices li'))
                    .filter(item => {
                        const style = window.getComputedStyle(item);
                        return style.display !== 'none' && style.visibility !== 'hidden' && item.textContent?.trim();
                    })
                    .map(item => item.textContent.trim());
                const selector = document.getElementById('webcam-selecter');
                const stream = document.getElementById('webcam-stream');
                const mediaName = document.getElementById('webcam-prop_media_name')?.textContent?.trim() || '';
                const resolution = document.getElementById('webcam-prop_image_resolution')?.textContent?.trim() || '';
                const frameRate = document.getElementById('webcam-prop_video_frame_rate')?.textContent?.trim() || '';
                const metadata = Object.fromEntries(
                    Array.from(document.querySelectorAll('[id^="webcam-prop_"]'))
                        .map(element => [element.id, element.textContent?.trim() || '']));
                const table = Array.from(document.querySelectorAll('[id^="webcam-prop_"]'))
                    .map(element => {
                        const row = element.closest('tr');
                        const cells = row ? Array.from(row.children).map(cell => cell.textContent?.trim() || '').filter(Boolean) : [];
                        return {
                            id: element.id,
                            value: element.textContent?.trim() || '',
                            rowText: cells.join(' | '),
                            cells
                        };
                    });
                const selectedOption = selector && selector.selectedIndex >= 0 ? selector.options[selector.selectedIndex]?.textContent?.trim() || '' : '';
                const options = selector ? Array.from(selector.options).map(option => option.textContent?.trim() || '') : [];
                const srcObject = stream && 'srcObject' in stream ? stream.srcObject : null;
                const videoTrack = srcObject && typeof srcObject.getVideoTracks === 'function' ? srcObject.getVideoTracks()[0] || null : null;
                const sampleVideoPixel = (video, xRatio, yRatio) => {
                    if (!(video instanceof HTMLVideoElement) || !video.videoWidth || !video.videoHeight) {
                        return '';
                    }

                    const canvas = document.createElement('canvas');
                    canvas.width = video.videoWidth;
                    canvas.height = video.videoHeight;
                    const context = canvas.getContext('2d', { willReadFrequently: true });
                    if (!context) {
                        return '';
                    }

                    context.drawImage(video, 0, 0, canvas.width, canvas.height);
                    const sampleX = Math.max(0, Math.min(canvas.width - 1, Math.round((canvas.width - 1) * xRatio)));
                    const sampleY = Math.max(0, Math.min(canvas.height - 1, Math.round((canvas.height - 1) * yRatio)));
                    const pixel = context.getImageData(sampleX, sampleY, 1, 1).data;
                    return `${pixel[0]},${pixel[1]},${pixel[2]},${pixel[3]}`;
                };

                return JSON.stringify({
                    visibleNoticeText: visibleNotices.join(' | '),
                    selectOptionsText: options.join(' | '),
                    selectOptionsCount: options.length,
                    selectedOptionText: selectedOption,
                    mediaNameText: mediaName,
                    resolutionText: resolution,
                    frameRateText: frameRate,
                    metadataJson: JSON.stringify(metadata),
                    tableJson: JSON.stringify(table),
                    streamActive: !!(srcObject && srcObject.active),
                    videoTrackState: videoTrack ? (videoTrack.readyState || '') : '',
                    videoWidth: stream && typeof stream.videoWidth === 'number' ? stream.videoWidth : 0,
                    videoHeight: stream && typeof stream.videoHeight === 'number' ? stream.videoHeight : 0,
                    topLeftSample: sampleVideoPixel(stream, 0, 0),
                    centerSample: sampleVideoPixel(stream, 0.5, 0.5)
                });
            })()
            """))?.ToString() ?? "{}";

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        return new WebcamTestsSnapshot(
            root.TryGetProperty("visibleNoticeText", out var visibleNoticeText) ? visibleNoticeText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("selectOptionsText", out var selectOptionsText) ? selectOptionsText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("selectOptionsCount", out var selectOptionsCount) ? selectOptionsCount.GetInt32() : 0,
            root.TryGetProperty("selectedOptionText", out var selectedOptionText) ? selectedOptionText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("mediaNameText", out var mediaNameText) ? mediaNameText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("resolutionText", out var resolutionText) ? resolutionText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("frameRateText", out var frameRateText) ? frameRateText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("metadataJson", out var metadataJson) ? metadataJson.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("tableJson", out var tableJson) ? tableJson.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("streamActive", out var streamActive) && streamActive.GetBoolean(),
            root.TryGetProperty("videoTrackState", out var videoTrackState) ? videoTrackState.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("videoWidth", out var videoWidth) ? videoWidth.GetInt32() : 0,
            root.TryGetProperty("videoHeight", out var videoHeight) ? videoHeight.GetInt32() : 0,
            ParseRgbaSample(root.TryGetProperty("topLeftSample", out var topLeftSample) ? topLeftSample.GetString() ?? string.Empty : string.Empty),
            ParseRgbaSample(root.TryGetProperty("centerSample", out var centerSample) ? centerSample.GetString() ?? string.Empty : string.Empty),
            result);
    }

    private static async Task<MicTestsSnapshot> WaitForMicTestsSnapshotAsync(
        WebDriverPage page,
        Func<MicTestsSnapshot, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        MicTestsSnapshot? lastSnapshot = null;
        BridgeException? lastBridgeException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                lastSnapshot = await ReadMicTestsSnapshotAsync(page);
                lastBridgeException = null;
            }
            catch (BridgeException ex) when (IsTransientMicTestsReadFailure(page, ex))
            {
                lastBridgeException = ex;

                if (lastSnapshot is not null)
                {
                    return lastSnapshot;
                }

                await Task.Delay(250);
                continue;
            }

            if (predicate(lastSnapshot))
            {
                return lastSnapshot;
            }

            await Task.Delay(250);
        }

        if (lastSnapshot is not null)
        {
            return lastSnapshot;
        }

        if (lastBridgeException is not null)
        {
            Assert.Fail($"Не удалось дочитать snapshot mictests.com из вкладки '{page.TabId}': {lastBridgeException.Message}");
        }

        return await ReadMicTestsSnapshotAsync(page);
    }

    private static async Task<JsonElement?> ExecuteBrowserMediaScriptAsync(
        WebDriverPage page,
        string script,
        string operation,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        BridgeException? lastBridgeException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return await page.ExecuteAsync(script);
            }
            catch (BridgeException ex) when (IsTransientMicTestsReadFailure(page, ex))
            {
                lastBridgeException = ex;
                await Task.Delay(250);
            }
        }

        throw new AssertionException(
            $"Не удалось выполнить page script '{operation}' во вкладке '{page.TabId}'. Последняя ошибка: {lastBridgeException?.Message ?? "неизвестно"}");
    }

    private static bool IsTransientMicTestsReadFailure(WebDriverPage page, BridgeException exception)
    {
        if (!page.IsConnected)
        {
            return true;
        }

        return exception.Message.Contains("не подключена", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("разорвано", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("таймаут", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<MicTestsSnapshot> ReadMicTestsSnapshotAsync(WebDriverPage page)
    {
        var result = (await ExecuteBrowserMediaScriptAsync(page, """
            (() => {
                const visibleNotices = Array.from(document.querySelectorAll('#mic-notices li'))
                    .filter(item => {
                        const style = window.getComputedStyle(item);
                        return style.display !== 'none' && style.visibility !== 'hidden' && item.textContent?.trim();
                    })
                    .map(item => item.textContent.trim());
                const selector = document.getElementById('mic-selecter');
                const audio = document.getElementById('mic-audio');
                const mediaName = document.getElementById('mic-prop_media_name')?.textContent?.trim() || '';
                const sampleRate = document.getElementById('mic-prop_audio_sampleRate')?.textContent?.trim() || '';
                const channelCount = document.getElementById('mic-prop_audio_channelCount')?.textContent?.trim() || '';
                const volume = document.getElementById('mic-prop_audio_volume')?.textContent?.trim() || '';
                const metadata = Object.fromEntries(
                    Array.from(document.querySelectorAll('[id^="mic-prop_"]'))
                        .map(element => [element.id, element.textContent?.trim() || '']));
                const table = Array.from(document.querySelectorAll('[id^="mic-prop_"]'))
                    .map(element => {
                        const row = element.closest('tr');
                        const cells = row ? Array.from(row.children).map(cell => cell.textContent?.trim() || '').filter(Boolean) : [];
                        return {
                            id: element.id,
                            value: element.textContent?.trim() || '',
                            rowText: cells.join(' | '),
                            cells
                        };
                    });
                const selectedOption = selector && selector.selectedIndex >= 0 ? selector.options[selector.selectedIndex]?.textContent?.trim() || '' : '';
                const options = selector ? Array.from(selector.options).map(option => option.textContent?.trim() || '') : [];
                const srcObject = audio && 'srcObject' in audio ? audio.srcObject : null;
                const audioTrack = srcObject && typeof srcObject.getAudioTracks === 'function' ? srcObject.getAudioTracks()[0] || null : null;
                const debug = window.__atomVirtualMediaStatus || null;

                return JSON.stringify({
                    visibleNoticeText: visibleNotices.join(' | '),
                    selectOptionsText: options.join(' | '),
                    selectOptionsCount: options.length,
                    selectedOptionText: selectedOption,
                    mediaNameText: mediaName,
                    sampleRateText: sampleRate,
                    channelCountText: channelCount,
                    volumeText: volume,
                    metadataJson: JSON.stringify(metadata),
                    tableJson: JSON.stringify(table),
                    streamActive: !!(srcObject && srcObject.active),
                    audioTrackState: audioTrack ? (audioTrack.readyState || '') : '',
                    debugJson: debug ? JSON.stringify(debug) : ''
                });
            })()
            """, "чтение snapshot mictests"))?.ToString() ?? "{}";

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        return new MicTestsSnapshot(
            root.TryGetProperty("visibleNoticeText", out var visibleNoticeText) ? visibleNoticeText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("selectOptionsText", out var selectOptionsText) ? selectOptionsText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("selectOptionsCount", out var selectOptionsCount) ? selectOptionsCount.GetInt32() : 0,
            root.TryGetProperty("selectedOptionText", out var selectedOptionText) ? selectedOptionText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("mediaNameText", out var mediaNameText) ? mediaNameText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("sampleRateText", out var sampleRateText) ? sampleRateText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("channelCountText", out var channelCountText) ? channelCountText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("volumeText", out var volumeText) ? volumeText.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("metadataJson", out var metadataJson) ? metadataJson.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("tableJson", out var tableJson) ? tableJson.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("streamActive", out var streamActive) && streamActive.GetBoolean(),
            root.TryGetProperty("audioTrackState", out var audioTrackState) ? audioTrackState.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("debugJson", out var debugJson) ? debugJson.GetString() ?? string.Empty : string.Empty,
            result);
    }

    private static async Task<DirectAudioProbeSnapshot> ProbeVirtualAudioStreamAsync(WebDriverPage page, string audioLabel)
    {
        var result = (await page.ExecuteAsync($$"""
            (async () => {
                const expectedLabel = {{ToJavaScriptStringLiteral(audioLabel)}};
                const devices = await navigator.mediaDevices.enumerateDevices();
                const target = devices.find(device => device.kind === 'audioinput' && (device.label || '').includes(expectedLabel));
                if (!target) {
                    return JSON.stringify({ ok: false, reason: 'device-not-found', label: '' });
                }

                try {
                    const stream = await navigator.mediaDevices.getUserMedia({
                        audio: {
                            deviceId: { exact: target.deviceId }
                        },
                        video: false
                    });
                    const track = typeof stream.getAudioTracks === 'function' ? stream.getAudioTracks()[0] || null : null;
                    let analyserRms = -1;
                    let analyserPeak = -1;
                    try {
                        const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
                        if (AudioContextCtor) {
                            const audioContext = new AudioContextCtor();
                            const source = audioContext.createMediaStreamSource(stream);
                            const analyser = audioContext.createAnalyser();
                            analyser.fftSize = 2048;
                            source.connect(analyser);
                            const samples = new Float32Array(analyser.fftSize);
                            await new Promise(resolve => setTimeout(resolve, 250));
                            analyser.getFloatTimeDomainData(samples);
                            let sum = 0;
                            let peak = 0;
                            for (const sample of samples) {
                                sum += sample * sample;
                                const abs = Math.abs(sample);
                                if (abs > peak) {
                                    peak = abs;
                                }
                            }
                            analyserRms = Math.sqrt(sum / samples.length);
                            analyserPeak = peak;
                            await audioContext.close();
                        }
                    } catch {
                    }
                    const payload = {
                        ok: true,
                        label: track?.label || '',
                        streamActive: !!stream.active,
                        audioTrackState: track?.readyState || '',
                        analyserRms,
                        analyserPeak
                    };
                    stream.getTracks().forEach(track => track.stop());
                    return JSON.stringify(payload);
                } catch (error) {
                    return JSON.stringify({ ok: false, reason: error?.name || 'gum-failed', label: '' });
                }
            })()
            """))?.ToString() ?? "{}";

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        return new DirectAudioProbeSnapshot(
            root.TryGetProperty("ok", out var ok) && ok.GetBoolean(),
            root.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("streamActive", out var streamActive) && streamActive.GetBoolean(),
            root.TryGetProperty("audioTrackState", out var audioTrackState) ? audioTrackState.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("analyserRms", out var analyserRms) ? analyserRms.GetDouble() : -1,
            root.TryGetProperty("analyserPeak", out var analyserPeak) ? analyserPeak.GetDouble() : -1,
            result);
    }

    private static async Task<DirectVideoProbeSnapshot> ProbeVirtualVideoStreamAsync(
        WebDriverPage page,
        string videoLabel,
        int width,
        int height)
    {
        var result = (await page.ExecuteAsync($$"""
            (async () => {
                const expectedLabel = {{ToJavaScriptStringLiteral(videoLabel)}};
                const expectedWidth = {{width}};
                const expectedHeight = {{height}};
                const devices = await navigator.mediaDevices.enumerateDevices();
                const target = devices.find(device => device.kind === 'videoinput' && (device.label || '').includes(expectedLabel));
                if (!target) {
                    return JSON.stringify({ ok: false, reason: 'device-not-found', label: '', videoWidth: 0, videoHeight: 0, settingsWidth: 0, settingsHeight: 0 });
                }

                try {
                    const stream = await navigator.mediaDevices.getUserMedia({
                        video: {
                            deviceId: { exact: target.deviceId },
                            width: { exact: expectedWidth },
                            height: { exact: expectedHeight }
                        },
                        audio: false
                    });

                    const track = typeof stream.getVideoTracks === 'function' ? stream.getVideoTracks()[0] || null : null;
                    const settings = track && typeof track.getSettings === 'function' ? track.getSettings() : {};
                    const video = document.createElement('video');
                    video.muted = true;
                    video.autoplay = true;
                    video.playsInline = true;
                    video.srcObject = stream;

                    await new Promise((resolve, reject) => {
                        const timeoutId = setTimeout(() => reject(new Error('video-metadata-timeout')), 5000);
                        video.onloadedmetadata = () => {
                            clearTimeout(timeoutId);
                            resolve();
                        };
                        video.onerror = () => {
                            clearTimeout(timeoutId);
                            reject(new Error('video-element-error'));
                        };
                    });

                    try {
                        await video.play();
                    } catch {
                    }

                    await new Promise(resolve => setTimeout(resolve, 250));

                    const canvas = document.createElement('canvas');
                    canvas.width = video.videoWidth || expectedWidth || 1;
                    canvas.height = video.videoHeight || expectedHeight || 1;
                    const context = canvas.getContext('2d', { willReadFrequently: true });
                    let topLeftSample = '';
                    let centerSample = '';
                    if (context) {
                        context.drawImage(video, 0, 0, canvas.width, canvas.height);
                        const topLeftPixel = context.getImageData(0, 0, 1, 1).data;
                        const centerX = Math.max(0, Math.min(canvas.width - 1, Math.floor(canvas.width / 2)));
                        const centerY = Math.max(0, Math.min(canvas.height - 1, Math.floor(canvas.height / 2)));
                        const centerPixel = context.getImageData(centerX, centerY, 1, 1).data;
                        topLeftSample = `${topLeftPixel[0]},${topLeftPixel[1]},${topLeftPixel[2]},${topLeftPixel[3]}`;
                        centerSample = `${centerPixel[0]},${centerPixel[1]},${centerPixel[2]},${centerPixel[3]}`;
                    }

                    const payload = {
                        ok: true,
                        label: track?.label || '',
                        streamActive: !!stream.active,
                        videoTrackState: track?.readyState || '',
                        videoWidth: typeof video.videoWidth === 'number' ? video.videoWidth : 0,
                        videoHeight: typeof video.videoHeight === 'number' ? video.videoHeight : 0,
                        settingsWidth: typeof settings.width === 'number' ? settings.width : 0,
                        settingsHeight: typeof settings.height === 'number' ? settings.height : 0,
                        topLeftSample,
                        centerSample
                    };

                    stream.getTracks().forEach(track => track.stop());
                    video.srcObject = null;
                    return JSON.stringify(payload);
                } catch (error) {
                    return JSON.stringify({
                        ok: false,
                        reason: error?.name || 'gum-failed',
                        message: error?.message || '',
                        label: '',
                        videoWidth: 0,
                        videoHeight: 0,
                        settingsWidth: 0,
                        settingsHeight: 0,
                        topLeftSample: '',
                        centerSample: ''
                    });
                }
            })()
            """))?.ToString() ?? "{}";

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        return new DirectVideoProbeSnapshot(
            root.TryGetProperty("ok", out var ok) && ok.GetBoolean(),
            root.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("streamActive", out var streamActive) && streamActive.GetBoolean(),
            root.TryGetProperty("videoTrackState", out var videoTrackState) ? videoTrackState.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("videoWidth", out var videoWidth) ? videoWidth.GetInt32() : 0,
            root.TryGetProperty("videoHeight", out var videoHeight) ? videoHeight.GetInt32() : 0,
            root.TryGetProperty("settingsWidth", out var settingsWidth) ? settingsWidth.GetInt32() : 0,
            root.TryGetProperty("settingsHeight", out var settingsHeight) ? settingsHeight.GetInt32() : 0,
            ParseRgbaSample(root.TryGetProperty("topLeftSample", out var topLeftSample) ? topLeftSample.GetString() ?? string.Empty : string.Empty),
            ParseRgbaSample(root.TryGetProperty("centerSample", out var centerSample) ? centerSample.GetString() ?? string.Empty : string.Empty),
            result);
    }

    private static async Task<(RgbaSample TopLeft, RgbaSample Center)> ReadExpectedStillImageSamplesAsync(
        string assetPath,
        int width,
        int height)
    {
        var streamParameters = new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32,
            FrameRate = 30,
        };

        var imageBytes = await File.ReadAllBytesAsync(assetPath).ConfigureAwait(false);
        using var videoStream = VideoStream.FromStillImage(imageBytes, Path.GetExtension(assetPath), streamParameters);
        var frame = videoStream.CurrentFrame.Span;

        return (
            ReadRgbaSample(frame, width, x: 0, y: 0),
            ReadRgbaSample(frame, width, x: width / 2, y: height / 2));
    }

    private static RgbaSample ReadRgbaSample(ReadOnlySpan<byte> rgbaFrame, int width, int x, int y)
    {
        var clampedX = Math.Max(0, Math.Min(width - 1, x));
        var clampedY = Math.Max(0, Math.Min((rgbaFrame.Length / 4 / width) - 1, y));
        var pixelOffset = ((clampedY * width) + clampedX) * 4;
        return new RgbaSample(
            rgbaFrame[pixelOffset],
            rgbaFrame[pixelOffset + 1],
            rgbaFrame[pixelOffset + 2],
            rgbaFrame[pixelOffset + 3]);
    }

    private sealed record MediaProbeResult(
        string LaunchProfile,
        string ArgumentsText,
        string? CameraName,
        string? MicrophoneName,
        string SecureContext,
        string PermissionText,
        string DevicesText)
    {
        public bool IsSuccessful
            => new ChromiumProbeSnapshot(SecureContext, PermissionText, DevicesText)
                .SatisfiesExpectation(CameraName, MicrophoneName, CameraName is not null, MicrophoneName is not null);
    }

    private sealed record ChromiumProbeSnapshot(
        string SecureContext,
        string PermissionText,
        string DevicesText)
    {
        public bool SatisfiesExpectation(
            string? expectedCameraLabel,
            string? expectedMicrophoneLabel,
            bool includeCamera,
            bool includeMicrophone)
        {
            if (!string.Equals(SecureContext, "true", StringComparison.Ordinal))
            {
                return false;
            }

            if (!PermissionText.Contains("\"ok\":true", StringComparison.Ordinal))
            {
                return false;
            }

            if (includeCamera)
            {
                if (!DevicesText.Contains("videoinput", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(expectedCameraLabel)
                    && !PermissionText.Contains(expectedCameraLabel, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (includeMicrophone)
            {
                if (!DevicesText.Contains("audioinput", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(expectedMicrophoneLabel)
                    && !PermissionText.Contains(expectedMicrophoneLabel, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed record PipeWireRegistrySnapshot(string Summary);

    private sealed record PipeWireRegistryMatch(
        string Id,
        string MediaClass,
        string Node,
        string Group)
    {
        public string Summary => $"{MediaClass}#{Id} node={Node} group={Group}";
    }

    private sealed record RawChromiumLaunchProfile(string Name, IReadOnlyList<string> Arguments)
    {
        public const string DefaultProfileName = "default";
    }

    private sealed record WebcamTestsSnapshot(
        string VisibleNoticeText,
        string SelectOptionsText,
        int SelectOptionsCount,
        string SelectedOptionText,
        string MediaNameText,
        string ResolutionText,
        string FrameRateText,
        string MetadataJson,
        string TableJson,
        bool StreamActive,
        string VideoTrackState,
        int VideoWidth,
        int VideoHeight,
        RgbaSample? TopLeftSample,
        RgbaSample? CenterSample,
        string RawJson)
    {
        public bool HasCompletedTableAnalysis(string expectedLabel)
            => MediaNameText.Contains(expectedLabel, StringComparison.Ordinal)
                && HasResolvedValue(GetTableValue(TableJson, "webcam-prop_image_resolution"))
                && HasResolvedValue(GetTableValue(TableJson, "webcam-prop_image_megapixels"))
                && HasResolvedValue(GetTableValue(TableJson, "webcam-prop_video_standard"))
                && HasResolvedValue(GetTableValue(TableJson, "webcam-prop_image_aspect_ratio"));
    }

    private sealed record MicTestsSnapshot(
        string VisibleNoticeText,
        string SelectOptionsText,
        int SelectOptionsCount,
        string SelectedOptionText,
        string MediaNameText,
        string SampleRateText,
        string ChannelCountText,
        string VolumeText,
        string MetadataJson,
        string TableJson,
        bool StreamActive,
        string AudioTrackState,
        string DebugJson,
        string RawJson)
    {
        public bool HasCompletedTableAnalysis(string expectedLabel)
            => MediaNameText.Contains(expectedLabel, StringComparison.Ordinal)
                && HasResolvedValue(GetTableValue(TableJson, "mic-prop_tester_wt_rating"))
                && HasResolvedValue(GetTableValue(TableJson, "mic-prop_audio_sampleRate"))
                && HasResolvedValue(GetTableValue(TableJson, "mic-prop_audio_channelCount"))
                && HasResolvedValue(GetTableValue(TableJson, "mic-prop_audio_sampleSize"))
                && HasResolvedValue(GetTableValue(TableJson, "mic-prop_audio_autoGainControl"))
                && HasResolvedValue(GetTableValue(TableJson, "mic-prop_audio_noiseSuppression"));
    }

    private static string? GetTableValue(string tableJson, string id)
    {
        if (string.IsNullOrWhiteSpace(tableJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(tableJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("id", out var idElement)
                    || !string.Equals(idElement.GetString(), id, StringComparison.Ordinal))
                {
                    continue;
                }

                return row.TryGetProperty("value", out var valueElement)
                    ? valueElement.GetString()
                    : null;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool HasResolvedValue(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value != "—"
            && value != "Not selected";

    private sealed record DirectAudioProbeSnapshot(
        bool Ok,
        string Label,
        bool StreamActive,
        string AudioTrackState,
        double AnalyserRms,
        double AnalyserPeak,
        string RawJson);

    private sealed record DirectVideoProbeSnapshot(
        bool Ok,
        string Label,
        bool StreamActive,
        string VideoTrackState,
        int VideoWidth,
        int VideoHeight,
        int SettingsWidth,
        int SettingsHeight,
        RgbaSample? TopLeftSample,
        RgbaSample? CenterSample,
        string RawJson);

    private sealed record RgbaSample(int R, int G, int B, int A)
    {
        public bool Matches(RgbaSample other, int tolerance = 4)
            => Math.Abs(R - other.R) <= tolerance
                && Math.Abs(G - other.G) <= tolerance
                && Math.Abs(B - other.B) <= tolerance
                && Math.Abs(A - other.A) <= tolerance;

        public override string ToString() => $"{R},{G},{B},{A}";
    }

    private static RgbaSample? ParseRgbaSample(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], out var r)
            || !int.TryParse(parts[1], out var g)
            || !int.TryParse(parts[2], out var b)
            || !int.TryParse(parts[3], out var a))
        {
            return null;
        }

        return new RgbaSample(r, g, b, a);
    }
}