using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Проверяет, что <see cref="WebBrowser"/> управляет вкладками через <see cref="IBrowserCommandTransport"/>,
/// а не напрямую через мостовой сервер — это точка подключения альтернативных транспортов (CDP).
/// </summary>
[TestFixture]
public sealed class WebDriverCommandTransportAbstractionTests
{
    [Test]
    public async Task OpenWindowAndPageBindCommandsCreatedByCustomTransport()
    {
        var transport = new FakeBrowserCommandTransport();
        await using var browser = new WebBrowser(
            new WebBrowserSettings(),
            materializedProfilePath: null,
            browserProcess: null,
            display: null,
            ownsDisplay: false,
            bridgeServer: null,
            bridgeBootstrap: CreatePlan("session-fake"),
            commandTransport: transport);

        var initialPage = (WebPage)browser.CurrentPage;
        ((WebWindow)browser.CurrentWindow).BindBridgeWindowId("window-0");
        initialPage.BindPageCommands("session-fake", "tab-0", transport.CreatePageCommands(initialPage, "session-fake", "tab-0"));

        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var windowPage = (WebPage)window.CurrentPage;
        var extraPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(windowPage.BridgeCommands, Is.InstanceOf<FakePageCommandClient>());
            Assert.That(windowPage.BoundBridgeTabId, Is.EqualTo("tab-1"));
            Assert.That(window.BoundBridgeWindowId, Is.EqualTo("window-1"));
            Assert.That(extraPage.BridgeCommands, Is.InstanceOf<FakePageCommandClient>());
            Assert.That(extraPage.BoundBridgeTabId, Is.EqualTo("tab-2"));
            Assert.That(transport.CreatedTabIds, Is.EqualTo(new[] { "tab-0", "tab-1", "tab-2" }));
            Assert.That(((FakePageCommandClient)windowPage.BridgeCommands!).TabContextApplyCount, Is.EqualTo(1));
            Assert.That(((FakePageCommandClient)extraPage.BridgeCommands!).TabContextApplyCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DisposeUnsubscribesAndDisposesCustomTransport()
    {
        var transport = new FakeBrowserCommandTransport();
        var browser = new WebBrowser(
            new WebBrowserSettings(),
            materializedProfilePath: null,
            browserProcess: null,
            display: null,
            ownsDisplay: false,
            bridgeServer: null,
            bridgeBootstrap: null,
            commandTransport: transport);

        Assert.That(transport.SubscriberCount, Is.EqualTo(4));

        await browser.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(transport.SubscriberCount, Is.Zero);
            Assert.That(transport.IsDisposed, Is.True);
        });
    }

    private static BridgeBootstrapPlan CreatePlan(string sessionId)
    {
        var root = Path.Combine(Path.GetTempPath(), "atom-webdriver-transport-tests", sessionId);
        var managedPolicyPath = Path.Combine(root, "chromium.managed-policy.json");

        return new BridgeBootstrapPlan(
            SessionId: sessionId,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0",
            Strategy: ChromiumBootstrapStrategy.SystemManagedPolicy,
            Host: "127.0.0.1",
            Port: 9000,
            TransportUrl: null,
            ManagedDeliveryPort: 9443,
            ManagedDeliveryRequiresCertificateBypass: false,
            ManagedDeliveryTrustDiagnostics: BridgeManagedDeliveryTrustDiagnostics.Trusted("test"),
            Secret: "test-secret",
            LaunchBinaryPath: string.Empty,
            LocalExtensionPath: Path.Combine(root, "extension"),
            ExtensionId: "abcdefghijklmnopabcdefghijklmnop",
            BundledConfigPath: Path.Combine(root, "config.json"),
            ManagedStorageConfigPath: Path.Combine(root, "storage.managed.json"),
            LocalStorageConfigPath: Path.Combine(root, "storage.local.json"),
            ManagedPolicyPath: managedPolicyPath,
            ManagedPolicyPublishPath: managedPolicyPath,
            ManagedPolicyDiagnostics: BridgeManagedPolicyPublishDiagnostics.ProfileLocal(managedPolicyPath),
            ManagedUpdateUrl: "https://127.0.0.1:9443/chromium/abcdefghijklmnopabcdefghijklmnop/manifest",
            ManagedPackageUrl: "https://127.0.0.1:9443/chromium/abcdefghijklmnopabcdefghijklmnop/extension.crx",
            ManagedPackageArtifactPath: Path.Combine(root, "atom-webdriver-extension.crx"),
            DiscoveryUrl: "http://127.0.0.1:9000/",
            NavigationProxyPort: 0,
            ConnectionTimeout: TimeSpan.FromSeconds(5));
    }

    private sealed class FakeBrowserCommandTransport : IBrowserCommandTransport
    {
        private readonly Lock gate = new();
        private readonly List<BridgeTabChannelSnapshot> tabs = [];
        private int nextTab = 1;
        private int nextWindow = 1;
        private Action<string, BridgeMessage>? runtimeEventReceived;
        private BridgeCallbackHandler? callbackRequested;
        private BridgeRequestInterceptionHandler? requestInterceptionRequested;
        private BridgeResponseInterceptionHandler? responseInterceptionRequested;

        public List<string> CreatedTabIds { get; } = [];

        public bool IsDisposed { get; private set; }

        public int SubscriberCount
            => (runtimeEventReceived?.GetInvocationList().Length ?? 0)
                + (callbackRequested?.GetInvocationList().Length ?? 0)
                + (requestInterceptionRequested?.GetInvocationList().Length ?? 0)
                + (responseInterceptionRequested?.GetInvocationList().Length ?? 0);

        public event Action<string, BridgeMessage>? RuntimeEventReceived
        {
            add => runtimeEventReceived += value;
            remove => runtimeEventReceived -= value;
        }

        public event BridgeCallbackHandler? CallbackRequested
        {
            add => callbackRequested += value;
            remove => callbackRequested -= value;
        }

        public event BridgeRequestInterceptionHandler? RequestInterceptionRequested
        {
            add => requestInterceptionRequested += value;
            remove => requestInterceptionRequested -= value;
        }

        public event BridgeResponseInterceptionHandler? ResponseInterceptionRequested
        {
            add => responseInterceptionRequested += value;
            remove => responseInterceptionRequested -= value;
        }

        public (string TabId, string? WindowId) OpenSurface(string? windowId)
        {
            lock (gate)
            {
                var tabId = $"tab-{nextTab++}";
                var resolvedWindowId = windowId ?? $"window-{nextWindow++}";
                tabs.Add(new BridgeTabChannelSnapshot("session-fake", tabId, resolvedWindowId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, IsRegistered: true));
                return (tabId, resolvedWindowId);
            }
        }

        public ValueTask<BridgeTabChannelSnapshot?> TryGetRegisteredTabAsync(string sessionId, Func<BridgeTabChannelSnapshot, bool> predicate)
        {
            lock (gate)
            {
                return ValueTask.FromResult(tabs.FirstOrDefault(tab => tab.SessionId == sessionId && tab.IsRegistered && predicate(tab)));
            }
        }

        public IPageCommandClient CreatePageCommands(WebPage page, string sessionId, string tabId)
        {
            CreatedTabIds.Add(tabId);
            return new FakePageCommandClient(this);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePageCommandClient(FakeBrowserCommandTransport transport) : IPageCommandClient
    {
        public int TabContextApplyCount { get; private set; }

        public Uri? GetDiscoveryUrl() => null;

        public ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string windowId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(transport.OpenSurface(windowId));

        public ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(Point? position, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(transport.OpenSurface(windowId: null));

        public ValueTask SetTabContextAsync(JsonObject payload, CancellationToken cancellationToken = default)
        {
            TabContextApplyCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string> GetUrlAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> CaptureScreenshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<JsonElement> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<JsonElement> ExecuteScriptAsync(string script, string shadowHostElementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<JsonElement> ExecuteScriptAsync(string script, string? shadowHostElementId, string? frameHostElementId, bool preferPageContextOnNull = false, bool forcePageContextExecution = false, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<JsonElement> ExecuteScriptAsync(string script, string? shadowHostElementId, string? frameHostElementId, string? elementId, bool preferPageContextOnNull = false, bool forcePageContextExecution = false, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<JsonElement> ExecuteScriptInFramesAsync(string script, bool isolatedWorld, bool includeMetadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SetCookieAsync(string contextId, string name, string value, string? domain, string? path, bool secure, bool httpOnly, long? expires, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<JsonElement> GetCookiesAsync(string contextId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DeleteCookiesAsync(string contextId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask CloseWindowAsync(string windowId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask CloseTabAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ActivateWindowAsync(string windowId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ActivateTabAsync(string targetTabId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<Rectangle> GetWindowBoundsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> FindElementAsync(JsonObject payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string[]> FindElementsAsync(JsonObject payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> WaitForElementAsync(JsonObject payload, TimeSpan waitTimeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetElementPropertyAsync(string elementId, string propertyName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> CheckShadowRootAsync(string elementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PointF> ResolveElementScreenPointAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<BridgeDebugPortStatusPayload> GetDebugPortStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<BridgeElementDescriptionPayload> DescribeElementAsync(string elementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<BridgeElementDescriptionPayload?> TryDescribeElementAsync(string elementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask FocusElementAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ScrollElementIntoViewAsync(string elementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
