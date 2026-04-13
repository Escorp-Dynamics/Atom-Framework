using System.Drawing;
using System.Text.Json.Nodes;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;
using Atom.Media.Video;
using Atom.Media.Video.Backends;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverDeviceResolutionTests
{
    [Test]
    public async Task LaunchAsyncAppliesLaunchDeviceToCurrentPageViewportFallback()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var viewport = await browser.CurrentPage.GetViewportSizeAsync();

        Assert.That(viewport, Is.EqualTo(Device.Pixel7.ViewportSize));
    }

    [Test]
    public async Task OpenWindowAsyncUsesResolvedWindowGeometry()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Position = new Point(10, 20),
            Size = new Size(1440, 900),
        });

        var window = await browser.OpenWindowAsync(new WebWindowSettings
        {
            Position = new Point(30, 40),
            Size = new Size(800, 600),
        });

        var bounds = await window.GetBoundingBoxAsync();

        Assert.That(bounds, Is.EqualTo(new Rectangle(new Point(30, 40), new Size(800, 600))));
    }

    [Test]
    public async Task OpenPageAsyncSnapshotsScopedDeviceAndDoesNotTrackExternalMutations()
    {
        var scopedDevice = Device.Pixel2;

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.MacBookPro14,
        });

        var page = (WebPage)await browser.CurrentWindow.OpenPageAsync(new WebPageSettings
        {
            Device = scopedDevice,
        });

        scopedDevice.Locale = "fr-FR";
        scopedDevice.ViewportSize = new Size(1, 1);

        var viewport = await page.GetViewportSizeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewport, Is.EqualTo(Device.Pixel2.ViewportSize));
            Assert.That(page.ResolvedDevice, Is.Not.Null);
            Assert.That(page.ResolvedDevice!.Locale, Is.EqualTo("en-US"));
            Assert.That(page.ResolvedDevice.ViewportSize, Is.EqualTo(Device.Pixel2.ViewportSize));
        });
    }

    [Test]
    public async Task OpenPageAsyncWithoutScopedDeviceInheritsWindowResolvedDevice()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.DesktopFullHd,
        });

        var window = await browser.OpenWindowAsync(new WebWindowSettings
        {
            Device = Device.iPhone14Pro,
        });

        var page = await window.OpenPageAsync();
        var viewport = await page.GetViewportSizeAsync();

        Assert.That(viewport, Is.EqualTo(Device.iPhone14Pro.ViewportSize));
    }

    [Test]
    public async Task MainFrameBoundingBoxUsesResolvedViewportFallback()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var bounds = await browser.CurrentPage.MainFrame.GetBoundingBoxAsync();

        Assert.That(bounds, Is.EqualTo(new Rectangle(Point.Empty, Device.Pixel7.ViewportSize)));
    }

    [Test]
    public async Task MainFrameIsVisibleWhenResolvedViewportIsAvailable()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var isVisible = await browser.CurrentPage.MainFrame.IsVisibleAsync();

        Assert.That(isVisible, Is.True);
    }

    [Test]
    public async Task ElementBoundingBoxUsesEnclosingFrameViewportFallback()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var element = new Element((WebPage)browser.CurrentPage);
        var bounds = await element.GetBoundingBoxAsync();

        Assert.That(bounds, Is.EqualTo(new Rectangle(Point.Empty, Device.Pixel7.ViewportSize)));
    }

    [Test]
    public async Task ElementVisibilityUsesEnclosingFrameViewportFallback()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var element = new Element((WebPage)browser.CurrentPage);
        var isVisible = await element.IsVisibleAsync();

        Assert.That(isVisible, Is.True);
    }

    [Test]
    public async Task ElementIntersectionUsesEnclosingFrameViewportFallback()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var element = new Element((WebPage)browser.CurrentPage);
        var intersects = await element.IsIntersectingViewportAsync();

        Assert.That(intersects, Is.True);
    }

    [Test]
    public async Task AttachVirtualMediaUpdatesPageResolvedDeviceMediaSnapshot()
    {
        using var cameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: "camera-native"));
        using var microphoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: "microphone-native"));
        await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 1,
            Height = 1,
            Name = "Scenario Camera",
            DeviceId = "bundle-7",
        }).ConfigureAwait(false);
        await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings
        {
            Name = "Scenario Microphone",
            DeviceId = "bundle-7",
        }).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var page = (WebPage)browser.CurrentPage;

        await page.AttachVirtualCameraAsync(camera).ConfigureAwait(false);
        await page.AttachVirtualMicrophoneAsync(microphone).ConfigureAwait(false);

        var mediaDevices = page.ResolvedDevice?.VirtualMediaDevices;

        Assert.Multiple(() =>
        {
            Assert.That(page.AttachedVirtualCamera, Is.SameAs(camera));
            Assert.That(page.AttachedVirtualMicrophone, Is.SameAs(microphone));
            Assert.That(mediaDevices, Is.Not.Null);
            Assert.That(mediaDevices?.VideoInputEnabled, Is.True);
            Assert.That(mediaDevices?.VideoInputLabel, Is.EqualTo("Scenario Camera"));
            Assert.That(mediaDevices?.VideoInputBrowserDeviceId, Is.EqualTo("camera-native"));
            Assert.That(mediaDevices?.AudioInputEnabled, Is.True);
            Assert.That(mediaDevices?.AudioInputLabel, Is.EqualTo("Scenario Microphone"));
            Assert.That(mediaDevices?.AudioInputBrowserDeviceId, Is.EqualTo("microphone-native"));
            Assert.That(mediaDevices?.GroupId, Is.EqualTo("bundle-7"));
        });
    }

    [Test]
    public async Task BrowserAndWindowAttachMediaTargetCurrentPageSnapshot()
    {
        using var firstCameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: "camera-a"));
        using var firstMicrophoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: "microphone-a"));
        await using var firstCamera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 1,
            Height = 1,
            Name = "Camera A",
            DeviceId = "device-a",
        }).ConfigureAwait(false);
        await using var firstMicrophone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings
        {
            Name = "Microphone A",
            DeviceId = "device-a",
        }).ConfigureAwait(false);

        firstCameraOverride.Dispose();
        firstMicrophoneOverride.Dispose();

        using var secondCameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: "camera-b"));
        using var secondMicrophoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: "microphone-b"));
        await using var secondCamera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 1,
            Height = 1,
            Name = "Camera B",
            DeviceId = "device-b",
        }).ConfigureAwait(false);
        await using var secondMicrophone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings
        {
            Name = "Microphone B",
            DeviceId = "device-b",
        }).ConfigureAwait(false);

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        });

        var window = (WebWindow)browser.CurrentWindow;
        var firstPage = (WebPage)window.CurrentPage;
        var secondPage = (WebPage)await window.OpenPageAsync(new WebPageSettings
        {
            Device = Device.Pixel2,
        }).ConfigureAwait(false);

        await browser.AttachVirtualCameraAsync(firstCamera).ConfigureAwait(false);
        await window.AttachVirtualMicrophoneAsync(firstMicrophone).ConfigureAwait(false);

        var thirdPage = (WebPage)await window.OpenPageAsync(new WebPageSettings
        {
            Device = Device.iPhone14Pro,
        }).ConfigureAwait(false);

        await browser.AttachVirtualCameraAsync(secondCamera).ConfigureAwait(false);
        await window.AttachVirtualMicrophoneAsync(secondMicrophone).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentPage, Is.SameAs(thirdPage));
            Assert.That(firstPage.AttachedVirtualCamera, Is.Null);
            Assert.That(firstPage.AttachedVirtualMicrophone, Is.Null);
            Assert.That(secondPage.AttachedVirtualCamera, Is.SameAs(firstCamera));
            Assert.That(secondPage.AttachedVirtualMicrophone, Is.SameAs(firstMicrophone));
            Assert.That(thirdPage.AttachedVirtualCamera, Is.SameAs(secondCamera));
            Assert.That(thirdPage.AttachedVirtualMicrophone, Is.SameAs(secondMicrophone));
            Assert.That(secondPage.ResolvedDevice?.VirtualMediaDevices?.VideoInputLabel, Is.EqualTo("Camera A"));
            Assert.That(secondPage.ResolvedDevice?.VirtualMediaDevices?.AudioInputLabel, Is.EqualTo("Microphone A"));
            Assert.That(thirdPage.ResolvedDevice?.VirtualMediaDevices?.VideoInputLabel, Is.EqualTo("Camera B"));
            Assert.That(thirdPage.ResolvedDevice?.VirtualMediaDevices?.AudioInputLabel, Is.EqualTo("Microphone B"));
        });
    }

    [Test]
    public async Task BuildSetTabContextPayloadIncludesVirtualMediaDevicesFromResolvedDevice()
    {
        var device = Device.Pixel7;
        device.VirtualMediaDevices = new VirtualMediaDevicesSettings
        {
            AudioInputEnabled = true,
            AudioInputLabel = "Payload Microphone",
            AudioInputBrowserDeviceId = "mic-visible",
            VideoInputEnabled = true,
            VideoInputLabel = "Payload Camera",
            VideoInputBrowserDeviceId = "camera-visible",
            AudioOutputEnabled = true,
            AudioOutputLabel = "Payload Speakers",
            GroupId = "payload-group",
        };

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = device,
        });

        var page = (WebPage)browser.CurrentPage;
        var payload = WebBrowser.BuildSetTabContextPayload(page);
        var mediaDevices = payload["virtualMediaDevices"] as JsonObject;

        Assert.Multiple(() =>
        {
            Assert.That(mediaDevices, Is.Not.Null);
            Assert.That(mediaDevices?["audioInputLabel"]?.GetValue<string>(), Is.EqualTo("Payload Microphone"));
            Assert.That(mediaDevices?["audioInputBrowserDeviceId"]?.GetValue<string>(), Is.EqualTo("mic-visible"));
            Assert.That(mediaDevices?["videoInputLabel"]?.GetValue<string>(), Is.EqualTo("Payload Camera"));
            Assert.That(mediaDevices?["videoInputBrowserDeviceId"]?.GetValue<string>(), Is.EqualTo("camera-visible"));
            Assert.That(mediaDevices?["audioOutputLabel"]?.GetValue<string>(), Is.EqualTo("Payload Speakers"));
            Assert.That(mediaDevices?["groupId"]?.GetValue<string>(), Is.EqualTo("payload-group"));
        });
    }

    [Test]
    public async Task BuildSetTabContextPayloadIncludesClientHintsFromResolvedDevice()
    {
        var device = Device.Pixel7;
        device.ClientHints ??= new ClientHintsSettings();
        device.ClientHints.FullVersionList = [new ClientHintBrand("Chromium", "131.0.0.0")];

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = device,
        });

        var page = (WebPage)browser.CurrentPage;
        var payload = WebBrowser.BuildSetTabContextPayload(page);
        var clientHints = payload["clientHints"] as JsonObject;
        var brands = clientHints?["brands"] as JsonArray;
        var fullVersionList = clientHints?["fullVersionList"] as JsonArray;

        Assert.Multiple(() =>
        {
            Assert.That(clientHints, Is.Not.Null);
            Assert.That(clientHints?["platform"]?.GetValue<string>(), Is.EqualTo("Android"));
            Assert.That(clientHints?["platformVersion"]?.GetValue<string>(), Is.EqualTo("14.0.0"));
            Assert.That(clientHints?["mobile"]?.GetValue<bool>(), Is.True);
            Assert.That(clientHints?["architecture"]?.GetValue<string>(), Is.EqualTo("arm"));
            Assert.That(clientHints?["model"]?.GetValue<string>(), Is.EqualTo("Pixel 7"));
            Assert.That(clientHints?["bitness"]?.GetValue<string>(), Is.EqualTo("64"));
            Assert.That(brands, Is.Not.Null);
            Assert.That(brands, Has.Count.EqualTo(2));
            Assert.That(brands?[0]?["brand"]?.GetValue<string>(), Is.EqualTo("Chromium"));
            Assert.That(brands?[0]?["version"]?.GetValue<string>(), Is.EqualTo("131"));
            Assert.That(fullVersionList, Is.Not.Null);
            Assert.That(fullVersionList, Has.Count.EqualTo(1));
            Assert.That(fullVersionList?[0]?["brand"]?.GetValue<string>(), Is.EqualTo("Chromium"));
            Assert.That(fullVersionList?[0]?["version"]?.GetValue<string>(), Is.EqualTo("131.0.0.0"));
        });
    }

    [Test]
    public async Task BuildSetTabContextPayloadPrefersPendingBridgeNavigationUrlOverStaleCurrentUrl()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());

        var page = (WebPage)browser.CurrentPage;
        await page.NavigateAsync(new Uri("https://initial.example/"), CancellationToken.None).ConfigureAwait(false);
        page.SetPendingBridgeNavigationUrl(new Uri("https://target.example/"));

        var payload = WebBrowser.BuildSetTabContextPayload(page);

        Assert.That(payload["url"]?.GetValue<string>(), Is.EqualTo("https://target.example/"));

        page.SetPendingBridgeNavigationUrl(null);
    }

    private sealed class FakeVirtualCameraBackend(string deviceIdentifier = "fake-camera") : IVirtualCameraBackend
    {
        public string DeviceIdentifier { get; } = deviceIdentifier;
        public bool IsCapturing { get; private set; }

        public event EventHandler<CameraControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(CameraControlType control, float value)
            => ControlChanged?.Invoke(this, new CameraControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });

        public float GetControl(CameraControlType control) => 0.0f;

        public CameraControlRange? GetControlRange(CameraControlType control) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeVirtualMicrophoneBackend(string deviceIdentifier = "fake-microphone") : IVirtualMicrophoneBackend
    {
        public string DeviceIdentifier { get; } = deviceIdentifier;
        public bool IsCapturing { get; private set; }

        public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualMicrophoneSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteSamples(ReadOnlySpan<byte> sampleData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(MicrophoneControlType control, float value)
            => ControlChanged?.Invoke(this, new MicrophoneControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });

        public float GetControl(MicrophoneControlType control) => 0.0f;

        public MicrophoneControlRange? GetControlRange(MicrophoneControlType control) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}