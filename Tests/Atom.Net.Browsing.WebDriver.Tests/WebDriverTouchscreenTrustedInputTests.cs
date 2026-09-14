using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Atom.Hardware.Display;
using Atom.Hardware.Input.Touch;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Оракул сенсорного ввода: отличает НАСТОЯЩЕЕ касание от мышиного клика.
/// </summary>
/// <remarks>
/// Проверять сам факт клика бесполезно — мышь его тоже даёт. Различие видно только в событии
/// указателя: у касания <c lang="text">pointerType</c> равен «touch» и следом приходит
/// <c lang="text">touchstart</c>, у мыши — «mouse» и никакого touch-события. Синтетика из страницы
/// отсеивается флагом <c lang="text">isTrusted</c>. Контрольный десктопный профиль доказывает, что
/// развилка в <c lang="text">ClickViewportPointAsync</c> работает в обе стороны, а не залипла на одной ветке.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("Hardware")]
public sealed class WebDriverTouchscreenTrustedInputTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Мобильный профиль: телефон обязан обслуживаться касанием.</summary>
    private static Device CreateTouchDevice() => new()
    {
        Name = "Galaxy S23 Ultra",
        ViewportSize = new Size(412, 915),
        DeviceScaleFactor = 3,
        IsMobile = true,
        HasTouch = true,
        MaxTouchPoints = 5,
        UserAgent = "Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36",
        Platform = "Linux armv8l",
        Locale = "en-US",
        Timezone = "America/New_York",
        Languages = ["en-US", "en"],
        HardwareConcurrency = 8,
        DeviceMemory = 8,
        ScreenOrientation = "portrait-primary",
    };

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ClickViewportPointOnTouchDeviceProducesTrustedTouchPointerEvent()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser touchscreen test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        var probe = await CaptureViewportClickProbeAsync(CreateTouchDevice()).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(probe.PointerType, Is.EqualTo("touch"), $"Мобильный профиль обязан обслуживаться тачскрином, а не мышью. {probe.Diagnostics}");
            Assert.That(probe.IsTrusted, Is.True, $"Событие касания обязано быть доверенным. {probe.Diagnostics}");
            Assert.That(probe.TouchStartFired, Is.True, $"За настоящим касанием обязан следовать touchstart. {probe.Diagnostics}");
        });
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ClickViewportPointOnDesktopDeviceKeepsMousePointerEvent()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Real-browser touchscreen test requires ATOM_TEST_WEBDRIVER_BROWSER.");

        var probe = await CaptureViewportClickProbeAsync(Device.DesktopFullHd).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(probe.PointerType, Is.EqualTo("mouse"), $"Десктопный профиль обязан остаться на мышином вводе. {probe.Diagnostics}");
            Assert.That(probe.IsTrusted, Is.True, $"Мышиный клик обязан быть доверенным. {probe.Diagnostics}");
            Assert.That(probe.TouchStartFired, Is.False, $"Мышиный клик не имеет права порождать touchstart. {probe.Diagnostics}");
        });
    }

    [SupportedOSPlatform("linux")]
    private static async Task<ProbeResult> CaptureViewportClickProbeAsync(Device device)
    {
        using var server = new TouchProbeLoopbackServer();
        await server.StartAsync().ConfigureAwait(false);

        await using var display = await CreateHiddenVirtualDisplayOrIgnoreAsync(device).ConfigureAwait(false);
        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Display = display,
            UseHeadlessMode = true,
            Device = device,
        }).ConfigureAwait(false);

        var page = (WebPage)browser.CurrentPage;

        // Навигация идёт командой моста, как и в остальных живых тестах: после коммита мост
        // переподключается, и прямой EvaluateAsync ловит «вкладка-отключена».
        await page.BridgeCommands!.NavigateAsync(server.PageUrl).ConfigureAwait(false);
        await WaitForProbeReadyAsync(page).ConfigureAwait(false);

        var viewportSize = await page.GetViewportSizeAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Не удалось получить размер viewport для пробного клика.");

        var pointX = viewportSize.Width / 2.0;
        var pointY = viewportSize.Height / 2.0;

        await page.ClickViewportPointAsync(pointX, pointY, CancellationToken.None).ConfigureAwait(false);

        var payload = await WaitForProbePayloadAsync(page).ConfigureAwait(false);
        var touchscreenIdentifier = DescribeResolvedTouchscreen(browser);
        var diagnostics = $"device={device.Name};hasTouch={device.HasTouch};viewport={viewportSize};clickPoint=({pointX},{pointY});resolved={page.LastResolvedInputPoint?.ToString() ?? "<null>"};touchscreen={touchscreenIdentifier};probe={payload ?? "<null>"}";

        if (string.IsNullOrWhiteSpace(payload))
            Assert.Fail($"Пробная область не получила ни одного события указателя. {diagnostics}");

        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;

        var result = new ProbeResult(
            ReadString(root, "pointerType"),
            ReadBoolean(root, "isTrusted"),
            ReadBoolean(root, "touchStartFired"),
            diagnostics);

        await TestContext.Out.WriteLineAsync(
            $"[touch-probe] device={device.Name} hasTouch={device.HasTouch} pointerType={result.PointerType} isTrusted={result.IsTrusted} touchStartFired={result.TouchStartFired} touchscreen={touchscreenIdentifier}")
            .ConfigureAwait(false);

        return result;
    }

    private static async Task WaitForProbeReadyAsync(WebPage page)
    {
        var deadline = DateTime.UtcNow + ProbeTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var ready = await TryEvaluateAsync(page, "String(document.documentElement.dataset.atomTouchProbeReady ?? '')").ConfigureAwait(false);
            if (string.Equals(ready, "1", StringComparison.Ordinal))
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail("Пробная страница не успела навесить слушатели событий указателя.");
    }

    private static async Task<string?> WaitForProbePayloadAsync(WebPage page)
    {
        var deadline = DateTime.UtcNow + ProbeTimeout;
        string? payload = null;

        while (DateTime.UtcNow < deadline)
        {
            payload = await TryEvaluateAsync(page, "String(document.documentElement.dataset.atomTouchProbe ?? '')").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(payload))
                return payload;

            await Task.Delay(50).ConfigureAwait(false);
        }

        return payload;
    }

    /// <summary>После коммита навигации мост переподключается, и вычисление какое-то время падает.</summary>
    private static async Task<string?> TryEvaluateAsync(WebPage page, string script)
    {
        try
        {
            return await page.EvaluateAsync<string>(script).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "<null>"
            : "<missing>";

    private static bool ReadBoolean(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string DescribeResolvedTouchscreen(WebBrowser browser)
    {
        var property = typeof(WebBrowser).GetProperty("ResolvedTouchscreen", BindingFlags.Instance | BindingFlags.NonPublic);
        return property?.GetValue(browser) is VirtualTouchscreen touchscreen
            ? touchscreen.DeviceIdentifier
            : "<none>";
    }

    /// <summary>
    /// Скрытый дисплей под заявленное устройство.
    /// </summary>
    /// <remarks>
    /// ★ Сенсор объявляет САМ дисплей: без этого браузер не заводит устройство касания и
    /// отбрасывает события, даже если профиль заявляет телефон.
    /// </remarks>
    [SupportedOSPlatform("linux")]
    private static async ValueTask<VirtualDisplay> CreateHiddenVirtualDisplayOrIgnoreAsync(Device device)
    {
        try
        {
            return await VirtualDisplay.CreateAsync(new VirtualDisplaySettings
            {
                IsVisible = false,
                HasTouch = device.HasTouch,
                Resolution = device.Screen is { Width: > 0 and var width, Height: > 0 and var height }
                    ? new Size(width, height)
                    : new Size(1920, 1080),
            }).ConfigureAwait(false);
        }
        catch (VirtualDisplayException ex)
        {
            Assert.Ignore("Virtual display backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
    }

    private sealed record ProbeResult(string PointerType, bool IsTrusted, bool TouchStartFired, string Diagnostics);

    /// <summary>
    /// Отдаёт страницу-ловушку: пишет первое событие указателя в dataset корневого узла.
    /// </summary>
    private sealed class TouchProbeLoopbackServer : IDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private Task? serverTask;

        public Uri PageUrl { get; private set; } = null!;

        public Task StartAsync()
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            PageUrl = new Uri($"http://127.0.0.1:{port}/touch-probe");
            serverTask = Task.Run(() => AcceptLoopAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();

            try { listener.Stop(); }
            catch (SocketException) { }

            try { serverTask?.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (SocketException) { }

            cancellationTokenSource.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    _ = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                    await WriteResponseAsync(stream, Encoding.UTF8.GetBytes(PageBody), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private const string PageBody = """
            <!doctype html>
            <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>Touch Probe</title>
                    <style>
                        html, body { margin: 0; height: 100%; }
                        #touch-target {
                            position: fixed;
                            inset: 0;
                            background: #10403b;
                            color: white;
                            font: 600 18px/1.4 sans-serif;
                            display: grid;
                            place-items: center;
                            touch-action: manipulation;
                        }
                    </style>
                </head>
                <body>
                    <div id="touch-target">probe</div>
                    <script>
                        (() => {
                            const target = document.getElementById('touch-target');
                            const state = { pointerType: null, isTrusted: null, touchStartFired: false, clientX: null, clientY: null };

                            const publish = () => {
                                globalThis.__probe = { ...state };
                                if (state.pointerType !== null) {
                                    document.documentElement.dataset.atomTouchProbe = JSON.stringify(globalThis.__probe);
                                }
                            };

                            target.addEventListener('touchstart', (event) => {
                                state.touchStartFired = true;
                                if (state.pointerType === null && event.touches.length > 0) {
                                    state.pointerType = 'touch';
                                    state.isTrusted = !!event.isTrusted;
                                    state.clientX = event.touches[0].clientX;
                                    state.clientY = event.touches[0].clientY;
                                }
                                publish();
                            }, { passive: true });

                            target.addEventListener('pointerdown', (event) => {
                                state.pointerType = event.pointerType;
                                state.isTrusted = !!event.isTrusted;
                                state.clientX = event.clientX;
                                state.clientY = event.clientY;
                                publish();
                                // touchstart приходит после pointerdown: даём ему шанс лечь в тот же снимок.
                                setTimeout(publish, 100);
                            });

                            publish();
                            document.documentElement.dataset.atomTouchProbeReady = '1';
                        })();
                    </script>
                </body>
            </html>
            """;

        private static async Task<string> ReadRequestHeadAsync(System.Net.Sockets.NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var memory = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                memory.Write(buffer, 0, read);
                var snapshot = Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
                if (snapshot.Contains("\r\n\r\n", StringComparison.Ordinal))
                    return snapshot;
            }

            return Encoding.ASCII.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
        }

        private static async Task WriteResponseAsync(System.Net.Sockets.NetworkStream stream, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var head = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
