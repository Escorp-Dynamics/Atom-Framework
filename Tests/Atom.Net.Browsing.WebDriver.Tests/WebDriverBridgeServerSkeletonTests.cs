using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverBridgeServerSkeletonTests
{
    private static string CreateProxyAuthorizationHeader(string routeToken)
        => $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(routeToken, ':')))}";

    private static async Task WriteAsciiAsync(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<string> ReadToEndAsync(Stream stream)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[4096];

        while (true)
        {
            var read = await stream.ReadAsync(chunk).ConfigureAwait(false);
            if (read <= 0)
                break;

            await buffer.WriteAsync(chunk.AsMemory(0, read)).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task<string> ReadHttpHeadersAsync(Stream stream)
    {
        List<byte> bytes = [];
        byte[] buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read <= 0)
                break;

            bytes.Add(buffer[0]);
            var count = bytes.Count;
            if (count >= 4
                && bytes[count - 4] == '\r'
                && bytes[count - 3] == '\n'
                && bytes[count - 2] == '\r'
                && bytes[count - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static BridgeBootstrapPlan CreateDiagnosticBridgeBootstrapPlan(string sessionId)
    {
        var root = Path.Combine(Path.GetTempPath(), "atom-webdriver-tests", sessionId);
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
            ConnectionTimeout: TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task BridgeServerStartAsyncAllocatesLoopbackPortAndServesDiscoveryEndpoint()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var discoveryResponse = await client.GetAsync($"http://127.0.0.1:{server.Port}/").ConfigureAwait(false);
        using var missingResponse = await client.GetAsync($"http://127.0.0.1:{server.Port}/missing").ConfigureAwait(false);
        var discoveryBody = await discoveryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(server.Port, Is.GreaterThan(0));
            Assert.That(server.ConnectionCount, Is.Zero);
            Assert.That(discoveryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(discoveryResponse.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(discoveryBody, Does.Contain("Atom Bridge Discovery"));
            Assert.That(discoveryBody, Does.Contain("atom-bridge-port"));
            Assert.That(discoveryBody, Does.Contain("atom-bridge-proxy-port"));
            Assert.That(discoveryBody, Does.Contain("atom-bridge-secret"));
            Assert.That(missingResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientNavigateAsyncWaitsUntilDebugPortStatusBecomesReady()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var pageClient = new PageBridgeCommandClient("session-a", "tab-1", server.Commands);
        var responseTask = pageClient.NavigateAsync(new Uri("https://example.test/ready")).AsTask();

        var navigateRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var navigateResponsePayload = JsonDocument.Parse("{" + "\"tabId\":\"tab-1\",\"url\":\"https://example.test/ready\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = navigateRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = navigateResponsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var notReadyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":false,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = notReadyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var secondStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = secondStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(navigateRequest, Is.Not.Null);
            Assert.That(navigateRequest!.Command, Is.EqualTo(BridgeCommand.Navigate));
            Assert.That(navigateRequest.Payload?.GetProperty("url").GetString(), Is.EqualTo("https://example.test/ready"));
            Assert.That(firstStatusRequest, Is.Not.Null);
            Assert.That(firstStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(secondStatusRequest, Is.Not.Null);
            Assert.That(secondStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientNavigateAsyncReappliesTabContextWhenPortStaysAliveButNotReady()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        JsonObject payload = new()
        {
            ["sessionId"] = "session-a",
            ["contextId"] = "ctx-1",
            ["tabId"] = "tab-1",
            ["windowId"] = "window-1",
            ["connectedAt"] = 123,
            ["readyAt"] = 123,
            ["isReady"] = true,
        };

        var pageClient = new PageBridgeCommandClient(
            "session-a",
            "tab-1",
            server.Commands,
            cancellationToken => server.Commands.SetTabContextAsync("session-a", "tab-1", payload, cancellationToken));
        var responseTask = pageClient.NavigateAsync(new Uri("https://example.test/ready")).AsTask();

        var navigateRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var navigateResponsePayload = JsonDocument.Parse("{" + "\"tabId\":\"tab-1\",\"url\":\"https://example.test/ready\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = navigateRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = navigateResponsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var notReadyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":false,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = notReadyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var setTabContextRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = setTabContextRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        var secondStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = secondStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(navigateRequest, Is.Not.Null);
            Assert.That(navigateRequest!.Command, Is.EqualTo(BridgeCommand.Navigate));
            Assert.That(firstStatusRequest, Is.Not.Null);
            Assert.That(firstStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(setTabContextRequest, Is.Not.Null);
            Assert.That(setTabContextRequest!.Command, Is.EqualTo(BridgeCommand.SetTabContext));
            Assert.That(setTabContextRequest.Payload?.GetProperty("contextId").GetString(), Is.EqualTo("ctx-1"));
            Assert.That(secondStatusRequest, Is.Not.Null);
            Assert.That(secondStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientNavigateAsyncReappliesTabContextBeforePortReturnsWhenDebugStatusShowsUrlMismatch()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        JsonObject payload = new()
        {
            ["sessionId"] = "session-a",
            ["contextId"] = "ctx-1",
            ["tabId"] = "tab-1",
            ["windowId"] = "window-1",
            ["connectedAt"] = 123,
            ["readyAt"] = 123,
            ["isReady"] = true,
            ["url"] = "https://example.test/ready",
        };

        var pageClient = new PageBridgeCommandClient(
            "session-a",
            "tab-1",
            server.Commands,
            cancellationToken => server.Commands.SetTabContextAsync("session-a", "tab-1", payload, cancellationToken));
        var responseTask = pageClient.NavigateAsync(new Uri("https://example.test/ready")).AsTask();

        var navigateRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var navigateResponsePayload = JsonDocument.Parse("{" + "\"tabId\":\"tab-1\",\"url\":\"https://example.test/ready\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = navigateRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = navigateResponsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var noPortPayload = JsonDocument.Parse("{" +
            "\"tabId\":101," +
            "\"hasPort\":false," +
            "\"queueLength\":0," +
            "\"hasSocket\":false," +
            "\"isReady\":false," +
            "\"interceptEnabled\":false," +
            "\"hasTabContext\":true," +
            "\"contextId\":\"ctx-1\"," +
            "\"contextUserAgent\":\"Atom.TestAgent/1.0\"," +
            "\"runtimeCheckStatus\":\"url-mismatch\"," +
            "\"runtimeHref\":\"https://example.test/initial\"," +
            "\"runtimeReadyState\":\"complete\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = noPortPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var setTabContextRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = setTabContextRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        var secondStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = secondStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(navigateRequest, Is.Not.Null);
            Assert.That(navigateRequest!.Command, Is.EqualTo(BridgeCommand.Navigate));
            Assert.That(firstStatusRequest, Is.Not.Null);
            Assert.That(firstStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(setTabContextRequest, Is.Not.Null);
            Assert.That(setTabContextRequest!.Command, Is.EqualTo(BridgeCommand.SetTabContext));
            Assert.That(setTabContextRequest.Payload?.GetProperty("contextId").GetString(), Is.EqualTo("ctx-1"));
            Assert.That(secondStatusRequest, Is.Not.Null);
            Assert.That(secondStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientNavigateAsyncRetriesTabContextReapplyAfterReconnectWhenFirstReapplyFails()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        JsonObject payload = new()
        {
            ["sessionId"] = "session-a",
            ["contextId"] = "ctx-1",
            ["tabId"] = "tab-1",
            ["windowId"] = "window-1",
            ["connectedAt"] = 123,
            ["readyAt"] = 123,
            ["isReady"] = true,
        };

        var pageClient = new PageBridgeCommandClient(
            "session-a",
            "tab-1",
            server.Commands,
            cancellationToken => server.Commands.SetTabContextAsync("session-a", "tab-1", payload, cancellationToken));
        var responseTask = pageClient.NavigateAsync(new Uri("https://example.test/ready")).AsTask();

        var navigateRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var navigateResponsePayload = JsonDocument.Parse("{" + "\"tabId\":\"tab-1\",\"url\":\"https://example.test/ready\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = navigateRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = navigateResponsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var notReadyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":false,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = notReadyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstSetTabContextRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstSetTabContextRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Error,
            Error = BridgeProtocolErrorCodes.TabDisconnected,
        }).ConfigureAwait(false);

        var recoveredStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":false,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = recoveredStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var secondSetTabContextRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = secondSetTabContextRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(navigateRequest, Is.Not.Null);
            Assert.That(navigateRequest!.Command, Is.EqualTo(BridgeCommand.Navigate));
            Assert.That(firstStatusRequest, Is.Not.Null);
            Assert.That(firstStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(firstSetTabContextRequest, Is.Not.Null);
            Assert.That(firstSetTabContextRequest!.Command, Is.EqualTo(BridgeCommand.SetTabContext));
            Assert.That(recoveredStatusRequest, Is.Not.Null);
            Assert.That(recoveredStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(secondSetTabContextRequest, Is.Not.Null);
            Assert.That(secondSetTabContextRequest!.Command, Is.EqualTo(BridgeCommand.SetTabContext));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientNavigateAsyncRetriesTabContextReapplyAfterTransientUnregisteredTabException()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var reapplyAttemptCount = 0;
        var pageClient = new PageBridgeCommandClient(
            "session-a",
            "tab-1",
            server.Commands,
            _ =>
            {
                reapplyAttemptCount++;
                if (reapplyAttemptCount == 1)
                    throw new InvalidOperationException("Вкладка 'tab-1' не зарегистрирована для сеанса 'session-a'");

                return ValueTask.CompletedTask;
            });
        var responseTask = pageClient.NavigateAsync(new Uri("https://example.test/ready")).AsTask();

        var navigateRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var navigateResponsePayload = JsonDocument.Parse("{" + "\"tabId\":\"tab-1\",\"url\":\"https://example.test/ready\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = navigateRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = navigateResponsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var notReadyPayload = JsonDocument.Parse("{" +
            "\"tabId\":101," +
            "\"hasPort\":false," +
            "\"queueLength\":0," +
            "\"hasSocket\":false," +
            "\"isReady\":false," +
            "\"interceptEnabled\":false," +
            "\"hasTabContext\":true," +
            "\"contextId\":\"ctx-1\"," +
            "\"contextUserAgent\":\"Atom.TestAgent/1.0\"," +
            "\"runtimeCheckStatus\":\"url-mismatch\"," +
            "\"runtimeHref\":\"https://example.test/initial\"," +
            "\"runtimeReadyState\":\"complete\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = notReadyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var secondStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" +
            "\"tabId\":101," +
            "\"hasPort\":true," +
            "\"queueLength\":0," +
            "\"hasSocket\":true," +
            "\"isReady\":true," +
            "\"interceptEnabled\":false," +
            "\"hasTabContext\":true," +
            "\"contextId\":\"ctx-1\"," +
            "\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = secondStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(navigateRequest, Is.Not.Null);
            Assert.That(navigateRequest!.Command, Is.EqualTo(BridgeCommand.Navigate));
            Assert.That(firstStatusRequest, Is.Not.Null);
            Assert.That(firstStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(secondStatusRequest, Is.Not.Null);
            Assert.That(secondStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(reapplyAttemptCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientNavigateAsyncReturnsWhenBrowserNavigationRemainsPending()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        JsonObject payload = new()
        {
            ["sessionId"] = "session-a",
            ["contextId"] = "ctx-1",
            ["tabId"] = "tab-1",
            ["windowId"] = "window-1",
            ["connectedAt"] = 123,
            ["readyAt"] = 123,
            ["isReady"] = true,
            ["url"] = "https://example.test/ready",
        };

        var pageClient = new PageBridgeCommandClient(
            "session-a",
            "tab-1",
            server.Commands,
            cancellationToken => server.Commands.SetTabContextAsync("session-a", "tab-1", payload, cancellationToken));
        var responseTask = pageClient.NavigateAsync(new Uri("https://example.test/ready")).AsTask();

        var navigateRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var navigateResponsePayload = JsonDocument.Parse("{" + "\"tabId\":\"tab-1\",\"url\":\"https://example.test/ready\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = navigateRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = navigateResponsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var hiddenPendingPayload = JsonDocument.Parse("{" +
            "\"tabId\":101," +
            "\"hasPort\":false," +
            "\"queueLength\":0," +
            "\"hasSocket\":false," +
            "\"isReady\":false," +
            "\"interceptEnabled\":false," +
            "\"hasTabContext\":true," +
            "\"contextId\":\"ctx-1\"," +
            "\"contextUserAgent\":\"Atom.TestAgent/1.0\"," +
            "\"hasBrowserTab\":true," +
            "\"browserTabUrl\":\"https://example.test/initial\"," +
            "\"browserTabPendingUrl\":\"https://example.test/ready\"," +
            "\"browserTabStatus\":\"loading\"," +
            "\"runtimeCheckStatus\":\"url-mismatch\"," +
            "\"runtimeHref\":\"https://example.test/initial\"," +
            "\"runtimeReadyState\":\"complete\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = hiddenPendingPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(navigateRequest, Is.Not.Null);
            Assert.That(navigateRequest!.Command, Is.EqualTo(BridgeCommand.Navigate));
            Assert.That(firstStatusRequest, Is.Not.Null);
            Assert.That(firstStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientReloadAsyncRecoversAfterExpectedDisconnectUntilReady()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var pageClient = new PageBridgeCommandClient("session-a", "tab-1", server.Commands);
        var responseTask = pageClient.ReloadAsync().AsTask();

        var reloadRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = reloadRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        var disconnectedStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = disconnectedStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Error,
            Error = BridgeProtocolErrorCodes.TabDisconnected,
        }).ConfigureAwait(false);

        var recoveredStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = recoveredStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(reloadRequest, Is.Not.Null);
            Assert.That(reloadRequest!.Command, Is.EqualTo(BridgeCommand.Reload));
            Assert.That(disconnectedStatusRequest, Is.Not.Null);
            Assert.That(disconnectedStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(recoveredStatusRequest, Is.Not.Null);
            Assert.That(recoveredStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientReloadAsyncReappliesTabContextAfterReconnectWhenStatusStaysNotReady()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        JsonObject payload = new()
        {
            ["sessionId"] = "session-a",
            ["contextId"] = "ctx-1",
            ["tabId"] = "tab-1",
            ["windowId"] = "window-1",
            ["connectedAt"] = 123,
            ["readyAt"] = 123,
            ["isReady"] = true,
        };

        var pageClient = new PageBridgeCommandClient(
            "session-a",
            "tab-1",
            server.Commands,
            cancellationToken => server.Commands.SetTabContextAsync("session-a", "tab-1", payload, cancellationToken));
        var responseTask = pageClient.ReloadAsync().AsTask();

        var reloadRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = reloadRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        var disconnectedStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = disconnectedStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Error,
            Error = BridgeProtocolErrorCodes.TabDisconnected,
        }).ConfigureAwait(false);

        var recoveredStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var notReadyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":false,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = recoveredStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = notReadyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var setTabContextRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = setTabContextRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        var readyStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = readyStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(reloadRequest, Is.Not.Null);
            Assert.That(reloadRequest!.Command, Is.EqualTo(BridgeCommand.Reload));
            Assert.That(disconnectedStatusRequest, Is.Not.Null);
            Assert.That(disconnectedStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(recoveredStatusRequest, Is.Not.Null);
            Assert.That(recoveredStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(setTabContextRequest, Is.Not.Null);
            Assert.That(setTabContextRequest!.Command, Is.EqualTo(BridgeCommand.SetTabContext));
            Assert.That(setTabContextRequest.Payload?.GetProperty("contextId").GetString(), Is.EqualTo("ctx-1"));
            Assert.That(readyStatusRequest, Is.Not.Null);
            Assert.That(readyStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
        });
    }

    [Test]
    public async Task PageBridgeCommandClientReloadAsyncRetriesTabContextReapplyAfterReconnectWhenFirstReapplyFails()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        JsonObject payload = new()
        {
            ["sessionId"] = "session-a",
            ["contextId"] = "ctx-1",
            ["tabId"] = "tab-1",
            ["windowId"] = "window-1",
            ["connectedAt"] = 123,
            ["readyAt"] = 123,
            ["isReady"] = true,
        };

        var pageClient = new PageBridgeCommandClient(
            "session-a",
            "tab-1",
            server.Commands,
            cancellationToken => server.Commands.SetTabContextAsync("session-a", "tab-1", payload, cancellationToken));
        var responseTask = pageClient.ReloadAsync().AsTask();

        var reloadRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = reloadRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        var disconnectedStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var notReadyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":false,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = disconnectedStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = notReadyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var firstSetTabContextRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = firstSetTabContextRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Error,
            Error = BridgeProtocolErrorCodes.TabDisconnected,
        }).ConfigureAwait(false);

        var recoveredStatusRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var readyPayload = JsonDocument.Parse("{" + "\"tabId\":101,\"hasPort\":true,\"queueLength\":0,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":false,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = recoveredStatusRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = readyPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var secondSetTabContextRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = secondSetTabContextRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(reloadRequest, Is.Not.Null);
            Assert.That(reloadRequest!.Command, Is.EqualTo(BridgeCommand.Reload));
            Assert.That(disconnectedStatusRequest, Is.Not.Null);
            Assert.That(disconnectedStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(firstSetTabContextRequest, Is.Not.Null);
            Assert.That(firstSetTabContextRequest!.Command, Is.EqualTo(BridgeCommand.SetTabContext));
            Assert.That(recoveredStatusRequest, Is.Not.Null);
            Assert.That(recoveredStatusRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(secondSetTabContextRequest, Is.Not.Null);
            Assert.That(secondSetTabContextRequest!.Command, Is.EqualTo(BridgeCommand.SetTabContext));
        });
    }

    [Test]
    public async Task BridgeServerDisposeAsyncIsIdempotent()
    {
        var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);
        await server.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await server.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
    }

    [Test]
    public async Task BridgeServerHealthEndpointReturnsJsonStatusSnapshot()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/health").ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var json = JsonDocument.Parse(body);
        var managedDelivery = json.RootElement.GetProperty("managedDelivery");
        var secureTransport = json.RootElement.GetProperty("secureTransport");
        var navigationProxy = json.RootElement.GetProperty("navigationProxy");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(json.RootElement.GetProperty("status").GetString(), Is.EqualTo("ok"));
            Assert.That(json.RootElement.GetProperty("server").GetString(), Is.EqualTo("bridge-server-skeleton"));
            Assert.That(json.RootElement.GetProperty("host").GetString(), Is.EqualTo("127.0.0.1"));
            Assert.That(json.RootElement.GetProperty("port").GetInt32(), Is.EqualTo(server.Port));
            Assert.That(managedDelivery.GetProperty("port").GetInt32(), Is.EqualTo(server.ManagedDeliveryPort));
            Assert.That(managedDelivery.GetProperty("requiresCertificateBypass").GetBoolean(), Is.EqualTo(server.ManagedDeliveryRequiresCertificateBypass));
            Assert.That(managedDelivery.GetProperty("status").GetString(), Is.EqualTo(server.ManagedDeliveryRequiresCertificateBypass ? "bypass-required" : "trusted"));
            Assert.That(managedDelivery.GetProperty("method").GetString(), Is.Not.Empty);
            Assert.That(secureTransport.GetProperty("port").GetInt32(), Is.EqualTo(server.SecureTransportPort));
            Assert.That(secureTransport.GetProperty("status").GetString(), Is.EqualTo("enabled"));
            Assert.That(secureTransport.GetProperty("scheme").GetString(), Is.EqualTo("wss"));
            Assert.That(navigationProxy.GetProperty("port").GetInt32(), Is.EqualTo(server.NavigationProxyPort));
            Assert.That(navigationProxy.GetProperty("status").GetString(), Is.EqualTo("enabled"));
            Assert.That(navigationProxy.GetProperty("scheme").GetString(), Is.EqualTo("http"));
            Assert.That(json.RootElement.GetProperty("connections").GetInt32(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("tabs").GetInt32(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("completedRequests").GetInt64(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("failedRequests").GetInt64(), Is.Zero);
        });
    }

    [Test]
    public void BridgeExtensionBootstrapRuntimeConfigIncludesNavigationProxyPort()
    {
        var method = typeof(BridgeExtensionBootstrap).GetMethod("BuildRuntimeConfig", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);

        var config = (JsonObject?)method!.Invoke(null,
        [
            new BridgeSettings
            {
                Host = "127.0.0.1",
                Port = 9222,
                NavigationProxyPort = 9443,
                Secret = "test-secret",
            },
            "session-a",
            "firefox",
            "1.2.3",
            null,
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(config, Is.Not.Null);
            Assert.That(config!["port"]?.GetValue<int>(), Is.EqualTo(9222));
            Assert.That(config["proxyPort"]?.GetValue<int>(), Is.EqualTo(9443));
        });
    }

    [Test]
    public async Task BridgeServerNavigationProxyFulfillReturnsRawHttpResponse()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(new ProxyNavigationRoute
        {
            SessionId = "session-a",
            TabId = "tab-1",
            ContextId = "ctx-1",
            RouteToken = "route-token-1",
            Revision = 1,
        });

        var now = DateTimeOffset.UtcNow;
        var enqueued = registry.EnqueueDecision("ctx-1", new ProxyNavigationPendingDecision
        {
            RequestId = "req-http-1",
            Method = "GET",
            AbsoluteUrl = "http://example.com/page/1?mode=proxy",
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(1),
            Action = ProxyNavigationDecisionAction.Fulfill,
            StatusCode = (int)HttpStatusCode.Created,
            ResponseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "text/plain; charset=utf-8",
                ["X-Atom-Proxy"] = "http",
            },
            ResponseBody = Encoding.UTF8.GetBytes("proxy-http-fulfilled"),
        }, now);

        Assert.That(enqueued, Is.True);

        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });
        server.ConfigureNavigationProxyDecisions(registry);

        await server.StartAsync().ConfigureAwait(false);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.NavigationProxyPort).ConfigureAwait(false);
        using var stream = client.GetStream();

        await WriteAsciiAsync(
            stream,
            "GET http://example.com/page/1?mode=proxy HTTP/1.1\r\n"
            + "Host: example.com\r\n"
            + $"Proxy-Authorization: {CreateProxyAuthorizationHeader("route-token-1")}\r\n"
            + "\r\n").ConfigureAwait(false);

        var response = await ReadToEndAsync(stream).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Does.Contain("HTTP/1.1 201 Created"));
            Assert.That(response, Does.Contain("X-Atom-Proxy: http"));
            Assert.That(response, Does.Contain("proxy-http-fulfilled"));
        });
    }

    [Test]
    public async Task BridgeServerNavigationProxyFulfillReturnsMitmHttpsResponse()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(new ProxyNavigationRoute
        {
            SessionId = "session-a",
            TabId = "tab-1",
            ContextId = "ctx-2",
            RouteToken = "route-token-2",
            Revision = 1,
        });

        var now = DateTimeOffset.UtcNow;
        var enqueued = registry.EnqueueDecision("ctx-2", new ProxyNavigationPendingDecision
        {
            RequestId = "req-https-1",
            Method = "GET",
            AbsoluteUrl = "https://example.com/secure/page?mode=proxy",
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(1),
            Action = ProxyNavigationDecisionAction.Fulfill,
            StatusCode = (int)HttpStatusCode.OK,
            ResponseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "text/plain; charset=utf-8",
                ["X-Atom-Proxy"] = "https",
            },
            ResponseBody = Encoding.UTF8.GetBytes("proxy-https-fulfilled"),
        }, now);

        Assert.That(enqueued, Is.True);

        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });
        server.ConfigureNavigationProxyDecisions(registry);

        await server.StartAsync().ConfigureAwait(false);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.NavigationProxyPort).ConfigureAwait(false);
        using var stream = client.GetStream();

        await WriteAsciiAsync(
            stream,
            "CONNECT example.com:443 HTTP/1.1\r\n"
            + "Host: example.com:443\r\n"
            + $"Proxy-Authorization: {CreateProxyAuthorizationHeader("route-token-2")}\r\n"
            + "\r\n").ConfigureAwait(false);

        var connectResponse = await ReadHttpHeadersAsync(stream).ConfigureAwait(false);

        using var sslStream = new SslStream(stream, leaveInnerStreamOpen: true, static (_, _, _, _) => true);
        await sslStream.AuthenticateAsClientAsync("example.com").ConfigureAwait(false);
        await WriteAsciiAsync(
            sslStream,
            "GET /secure/page?mode=proxy HTTP/1.1\r\n"
            + "Host: example.com\r\n"
            + "\r\n").ConfigureAwait(false);

        var response = await ReadToEndAsync(sslStream).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(connectResponse, Does.Contain("HTTP/1.1 200 Connection Established"));
            Assert.That(response, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(response, Does.Contain("X-Atom-Proxy: https"));
            Assert.That(response, Does.Contain("proxy-https-fulfilled"));
        });
    }

    [Test]
    public async Task BridgeNavigationProxyDirectHandlerReturnsRawHttpResponse()
    {
        await using var server = new BridgeNavigationProxyServer(
            "127.0.0.1",
            0,
            static () => null,
            static (request, _) =>
            {
                if (!string.Equals(request.Path, "/callback", StringComparison.Ordinal))
                    return ValueTask.FromResult<BridgeNavigationProxyDirectResponse?>(null);

                Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Access-Control-Allow-Origin"] = "*",
                    ["Content-Type"] = "text/plain; charset=utf-8",
                };

                return ValueTask.FromResult<BridgeNavigationProxyDirectResponse?>(new(
                    StatusCode: (int)HttpStatusCode.OK,
                    ReasonPhrase: null,
                    Headers: headers,
                    Body: Encoding.UTF8.GetBytes("proxy-direct-ok")));
            });

        await server.StartAsync().ConfigureAwait(false);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port).ConfigureAwait(false);
        using var stream = client.GetStream();

        const string body = "{\"requestId\":\"req-direct-1\"}";
        await WriteAsciiAsync(
            stream,
            "POST /callback?secret=test-secret HTTP/1.1\r\n"
            + "Host: 127.0.0.1\r\n"
            + "Content-Type: text/plain; charset=utf-8\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
            + "\r\n"
            + body).ConfigureAwait(false);

        var response = await ReadToEndAsync(stream).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(response, Does.Contain("Access-Control-Allow-Origin: *"));
            Assert.That(response, Does.Contain("proxy-direct-ok"));
        });
    }

    [Test]
    public async Task BridgeServerNavigationProxyAcceptsDirectUtilityCallbackPost()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });
        server.CallbackRequested += static (_, _) => ValueTask.FromResult(BridgeCallbackHttpResponse.Continue());

        await server.StartAsync().ConfigureAwait(false);

        const string body = "{\"requestId\":\"req-callback-1\",\"tabId\":\"tab-1\",\"name\":\"bridge-callback\",\"args\":[]}";

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.NavigationProxyPort).ConfigureAwait(false);
        using var stream = client.GetStream();

        await WriteAsciiAsync(
            stream,
            "POST /callback?secret=test-secret HTTP/1.1\r\n"
            + "Host: 127.0.0.1\r\n"
            + "Content-Type: text/plain; charset=utf-8\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
            + "\r\n"
            + body).ConfigureAwait(false);

        var response = await ReadToEndAsync(stream).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(response, Does.Contain("Access-Control-Allow-Origin: *"));
            Assert.That(response, Does.Contain("Access-Control-Allow-Methods: POST,OPTIONS"));
            Assert.That(response, Does.Contain("\"action\":\"continue\""));
        });
    }

    [Test]
    public async Task BridgeServerSecureTransportHandshakeAcceptsValidClientPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

        await socket.ConnectAsync(BridgeTestHelpers.CreateSecureBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-secure",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(response.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(response.Error, Is.Null);
        });
    }

    [Test]
    public async Task BridgeServerSecureTransportRejectsUpgradeWhenSecretIsInvalid()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

        Exception? failure = null;

        try
        {
            await socket.ConnectAsync(BridgeTestHelpers.CreateSecureBridgeUri(server, secret: "wrong-secret"), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure, Is.InstanceOf<WebSocketException>().Or.InstanceOf<HttpRequestException>());
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerStartAsyncLogsManagedDeliveryTrustDiagnostics()
    {
        using var provider = new TestLoggerProvider();

        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
            Logger = provider.CreateLogger("bridge-tests"),
        });

        await server.StartAsync().ConfigureAwait(false);

        var diagnostics = server.ManagedDeliveryTrustDiagnostics;
        var matchingEntry = provider.Entries.LastOrDefault(static entry => entry.Message.Contains("Managed-delivery TLS trust", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(matchingEntry, Is.Not.Null);
            Assert.That(matchingEntry!.Message, Does.Contain(diagnostics.Method));
            Assert.That(matchingEntry.Message, Does.Contain(server.ManagedDeliveryPort.ToString()));
            Assert.That(matchingEntry.Level, Is.EqualTo(diagnostics.RequiresCertificateBypass ? LogLevel.Warning : LogLevel.Information));
        });
    }

    [Test]
    public async Task BridgeServerManagedManifestEndpointServesOmahaUpdateDocument()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        var extensionId = "abcdefghijklmnopabcdefghijklmnop";
        var packageUrl = $"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/extension.crx";
        var updateUrl = $"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/manifest";
        server.ConfigureManagedExtensionDelivery(BridgeManagedExtensionDelivery.CreateDiagnosticStub(
            extensionId,
            "1.2.3",
            updateUrl,
            packageUrl));

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync($"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/manifest").ConfigureAwait(false);
        using var wrongExtensionResponse = await client.GetAsync($"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/wrong/manifest").ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/xml"));
            Assert.That(body, Does.Contain("<gupdate"));
            Assert.That(body, Does.Contain($"appid=\"{extensionId}\""));
            Assert.That(body, Does.Contain($"codebase=\"{packageUrl}\""));
            Assert.That(body, Does.Contain("version=\"1.2.3\""));
            Assert.That(wrongExtensionResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task BridgeServerManagedPackageEndpointServesDiagnosticCrxStub()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        var extensionId = "abcdefghijklmnopabcdefghijklmnop";
        var packageUrl = $"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/extension.crx";
        var updateUrl = $"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/manifest";
        server.ConfigureManagedExtensionDelivery(BridgeManagedExtensionDelivery.CreateDiagnosticStub(
            extensionId,
            "1.2.3",
            updateUrl,
            packageUrl));

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync($"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/extension.crx").ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(body);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/x-chrome-extension"));
            Assert.That(body.Length, Is.GreaterThan(0));
            Assert.That(text, Does.Contain("ATOM-CHROMIUM-EXTENSION-STUB"));
            Assert.That(text, Does.Contain($"extensionId={extensionId}"));
        });
    }

    [Test]
    public async Task BridgeServerManagedPackageEndpointServesConfiguredCrxBytes()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        var extensionId = "abcdefghijklmnopabcdefghijklmnop";
        var packageUrl = $"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/extension.crx";
        var updateUrl = $"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/manifest";
        var packageBytes = new byte[] { (byte)'C', (byte)'r', (byte)'2', (byte)'4', 2, 0, 0, 0, 17, 23, 42 };
        server.ConfigureManagedExtensionDelivery(new BridgeManagedExtensionDelivery(
            extensionId,
            "1.2.3",
            updateUrl,
            packageUrl,
            packageBytes));

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync($"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/{extensionId}/extension.crx").ConfigureAwait(false);
        using var wrongExtensionResponse = await client.GetAsync($"https://127.0.0.1:{server.ManagedDeliveryPort}/chromium/wrong/extension.crx").ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/x-chrome-extension"));
            Assert.That(body, Is.EqualTo(packageBytes));
            Assert.That(wrongExtensionResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeAcceptsValidClientPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(response.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(response.Error, Is.Null);
        });
    }

    [Test]
    public async Task BridgeServerHealthEndpointReflectsAcceptedSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        var health = await BridgeTestHelpers.GetHealthAsync(server).ConfigureAwait(false);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(health.GetProperty("connections").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeTabConnectedUpdatesHealthSnapshot()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        var connected = BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1");
        await BridgeTestHelpers.SendMessageAsync(socket, connected).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 1
            && snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(health.GetProperty("connections").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeTabDisconnectedRemovesTabFromHealthSnapshot()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabDisconnected, tabId: "tab-1")).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 1
            && snapshot.GetProperty("tabs").GetInt32() == 0).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(health.GetProperty("connections").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeRequestRoutingCompletesPendingResponse()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-1",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("\"Example Title\"");

        var inboundResponse = new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        };
        await BridgeTestHelpers.SendMessageAsync(socket, inboundResponse).ConfigureAwait(false);

        var resolvedResponse = await responseTask.ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("completedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Type, Is.EqualTo(BridgeMessageType.Request));
            Assert.That(receivedRequest.Command, Is.EqualTo(BridgeCommand.GetTitle));
            Assert.That(receivedRequest.TabId, Is.EqualTo("tab-1"));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(resolvedResponse.Payload?.GetString(), Is.EqualTo("Example Title"));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("completedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [TestCase("title", "Example Title")]
    [TestCase("url", "https://example.test/")]
    [TestCase("content", "<html></html>")]
    public async Task BridgeServerStringCommandBuildersReturnStringPayload(string commandName, string expectedPayload)
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var expectedCommand = commandName switch
        {
            "title" => BridgeCommand.GetTitle,
            "url" => BridgeCommand.GetUrl,
            "content" => BridgeCommand.GetContent,
            _ => throw new InvalidOperationException($"Unsupported string builder selector '{commandName}'."),
        };

        var responseTask = expectedCommand switch
        {
            BridgeCommand.GetTitle => server.GetTitleAsync("session-a", "tab-1").AsTask(),
            BridgeCommand.GetUrl => server.GetUrlAsync("session-a", "tab-1").AsTask(),
            BridgeCommand.GetContent => server.GetContentAsync("session-a", "tab-1").AsTask(),
            _ => throw new InvalidOperationException($"Unsupported string builder command '{expectedCommand}'."),
        };

        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse($"\"{expectedPayload}\"");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Type, Is.EqualTo(BridgeMessageType.Request));
            Assert.That(receivedRequest.Command, Is.EqualTo(expectedCommand));
            Assert.That(receivedRequest.TabId, Is.EqualTo("tab-1"));
            Assert.That(result, Is.EqualTo(expectedPayload));
        });
    }

    [TestCase(nameof(BridgeCommand.GetTitle), "title", "Example Title")]
    [TestCase(nameof(BridgeCommand.GetUrl), "url", "https://example.test/")]
    public async Task BridgeServerStringCommandBuildersAcceptObjectPayloadFromDirectCommands(string commandName, string propertyName, string expectedPayload)
    {
        var command = Enum.Parse<BridgeCommand>(commandName, ignoreCase: false);

        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = command switch
        {
            BridgeCommand.GetTitle => server.GetTitleAsync("session-a", "tab-1").AsTask(),
            BridgeCommand.GetUrl => server.GetUrlAsync("session-a", "tab-1").AsTask(),
            _ => throw new InvalidOperationException($"Unsupported command '{command}'."),
        };

        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var payloadDocument = JsonDocument.Parse(new JsonObject
        {
            [propertyName] = expectedPayload,
        }.ToJsonString());
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = payloadDocument.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest.Command, Is.EqualTo(command));
            Assert.That(result, Is.EqualTo(expectedPayload));
        });
    }

    [Test]
    public async Task BridgeServerGetWindowBoundsBuilderReturnsRectanglePayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.GetWindowBoundsAsync("session-a", "tab-1").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("{\"left\":10,\"top\":20,\"width\":300,\"height\":200}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.GetWindowBounds));
            Assert.That(result.X, Is.EqualTo(10));
            Assert.That(result.Y, Is.EqualTo(20));
            Assert.That(result.Width, Is.EqualTo(300));
            Assert.That(result.Height, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task BridgeServerResolveElementScreenPointBuilderReturnsPointPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.ResolveElementScreenPointAsync("session-a", "tab-1", "element-1", scrollIntoView: true).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("{\"viewportX\":10.5,\"viewportY\":20.25}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.ResolveElementScreenPoint));
            Assert.That(receivedRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("element-1"));
            Assert.That(receivedRequest.Payload?.GetProperty("scrollIntoView").GetBoolean(), Is.True);
            Assert.That(result.X, Is.EqualTo(10.5f));
            Assert.That(result.Y, Is.EqualTo(20.25f));
        });
    }

    [Test]
    public async Task BridgeServerGetDebugPortStatusBuilderReturnsTypedPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.GetDebugPortStatusAsync("session-a", "tab-1").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("{\"tabId\":101,\"hasPort\":true,\"queueLength\":2,\"hasSocket\":true,\"isReady\":true,\"interceptEnabled\":false,\"hasTabContext\":true,\"contextId\":\"ctx-1\",\"contextUserAgent\":\"Atom.TestAgent/1.0\",\"hasBrowserTab\":true,\"browserTabUrl\":\"https://example.test/current\",\"browserTabPendingUrl\":\"https://example.test/next\",\"browserTabStatus\":\"loading\",\"runtimeCheckStatus\":\"url-mismatch\",\"runtimeHref\":\"https://example.test/current\",\"runtimeReadyState\":\"complete\",\"runtimeCheckError\":\"runtime probe stale\"}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.DebugPortStatus));
            Assert.That(result.TabId, Is.EqualTo(101));
            Assert.That(result.HasPort, Is.True);
            Assert.That(result.QueueLength, Is.EqualTo(2));
            Assert.That(result.HasSocket, Is.True);
            Assert.That(result.IsReady, Is.True);
            Assert.That(result.InterceptEnabled, Is.False);
            Assert.That(result.HasTabContext, Is.True);
            Assert.That(result.ContextId, Is.EqualTo("ctx-1"));
            Assert.That(result.ContextUserAgent, Is.EqualTo("Atom.TestAgent/1.0"));
            Assert.That(result.HasBrowserTab, Is.True);
            Assert.That(result.BrowserTabUrl, Is.EqualTo("https://example.test/current"));
            Assert.That(result.BrowserTabPendingUrl, Is.EqualTo("https://example.test/next"));
            Assert.That(result.BrowserTabStatus, Is.EqualTo("loading"));
            Assert.That(result.RuntimeCheckStatus, Is.EqualTo("url-mismatch"));
            Assert.That(result.RuntimeHref, Is.EqualTo("https://example.test/current"));
            Assert.That(result.RuntimeReadyState, Is.EqualTo("complete"));
            Assert.That(result.RuntimeCheckError, Is.EqualTo("runtime probe stale"));
        });
    }

    [Test]
    public async Task BridgeCommandClientGetWindowBoundsAsyncUsesTypedBuilderLayer()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.GetWindowBoundsAsync("session-a", "tab-1").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("{\"left\":1,\"top\":2,\"width\":3,\"height\":4}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.GetWindowBounds));
            Assert.That(result.X, Is.EqualTo(1));
            Assert.That(result.Y, Is.EqualTo(2));
            Assert.That(result.Width, Is.EqualTo(3));
            Assert.That(result.Height, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task BridgeCommandClientExecuteScriptInFramesAsyncSendsFrameExecutionPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);

        var responseTask = server.Commands.ExecuteScriptInFramesAsync("session-a", "tab-1", "return 1;", isolatedWorld: true, includeMetadata: true).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("[{\"ordinal\":0,\"status\":\"ok\",\"value\":\"1\",\"error\":null}]");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.ExecuteScriptInFrames));
            Assert.That(receivedRequest.Payload?.GetProperty("script").GetString(), Is.EqualTo("return 1;"));
            Assert.That(receivedRequest.Payload?.GetProperty("world").GetString(), Is.EqualTo("ISOLATED"));
            Assert.That(receivedRequest.Payload?.GetProperty("includeMetadata").GetBoolean(), Is.True);
            Assert.That(result.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(result.GetArrayLength(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task WebPageEvaluateAsyncSendsPreferPageContextOnNullForNonGenericScripts()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await using var browser = new WebBrowser(
            new WebBrowserSettings(),
            materializedProfilePath: null,
            browserProcess: null,
            display: null,
            ownsDisplay: false,
            bridgeServer: server,
            bridgeBootstrap: CreateDiagnosticBridgeBootstrapPlan("session-a"));
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)browser.CurrentPage;
        window.BindBridgeWindowId("window-1");
        page.BindBridgeCommands("session-a", "tab-1", server.Commands);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = page.EvaluateAsync("document.cookie").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("\"session=alpha\"");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);
        var hasForcePageContextExecution = receivedRequest.Payload is JsonElement payload
            && payload.TryGetProperty("forcePageContextExecution", out _);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.ExecuteScript));
            Assert.That(receivedRequest.Payload?.GetProperty("script").GetString(), Is.EqualTo("document.cookie"));
            Assert.That(receivedRequest.Payload?.GetProperty("preferPageContextOnNull").GetBoolean(), Is.True);
            Assert.That(hasForcePageContextExecution, Is.False);
            Assert.That(result?.GetString(), Is.EqualTo("session=alpha"));
        });
    }

    [Test]
    public async Task BridgeCommandClientSetCookieAsyncSendsObjectPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.SetCookieAsync("session-a", "tab-1", "ctx-1", "session", "abc", "example.com", "/", secure: true, httpOnly: true, expires: 1735689600).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.SetCookie));
            Assert.That(receivedRequest.Payload?.GetProperty("contextId").GetString(), Is.EqualTo("ctx-1"));
            Assert.That(receivedRequest.Payload?.GetProperty("name").GetString(), Is.EqualTo("session"));
            Assert.That(receivedRequest.Payload?.GetProperty("value").GetString(), Is.EqualTo("abc"));
            Assert.That(receivedRequest.Payload?.GetProperty("domain").GetString(), Is.EqualTo("example.com"));
            Assert.That(receivedRequest.Payload?.GetProperty("path").GetString(), Is.EqualTo("/"));
            Assert.That(receivedRequest.Payload?.GetProperty("secure").GetBoolean(), Is.True);
            Assert.That(receivedRequest.Payload?.GetProperty("httpOnly").GetBoolean(), Is.True);
            Assert.That(receivedRequest.Payload?.GetProperty("expires").GetInt64(), Is.EqualTo(1735689600));
        });
    }

    [Test]
    public async Task BridgeCommandClientGetCookiesAsyncReturnsJsonArrayPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.GetCookiesAsync("session-a", "tab-1", "ctx-1").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("""
            [{
              "name": "session",
              "value": "abc",
              "domain": "example.com",
              "path": "/",
              "secure": true,
              "httpOnly": false,
              "expires": 1735689600
            }]
            """);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.GetCookies));
            Assert.That(receivedRequest.Payload?.GetProperty("contextId").GetString(), Is.EqualTo("ctx-1"));
            Assert.That(result.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(result.GetArrayLength(), Is.EqualTo(1));
            Assert.That(result[0].GetProperty("name").GetString(), Is.EqualTo("session"));
            Assert.That(result[0].GetProperty("value").GetString(), Is.EqualTo("abc"));
            Assert.That(result[0].GetProperty("secure").GetBoolean(), Is.True);
            Assert.That(result[0].GetProperty("httpOnly").GetBoolean(), Is.False);
            Assert.That(result[0].GetProperty("expires").GetInt64(), Is.EqualTo(1735689600));
        });
    }

    [Test]
    public async Task BridgeCommandClientDeleteCookiesAsyncUsesStatusOnlyCommand()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.DeleteCookiesAsync("session-a", "tab-1", "ctx-1").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.DeleteCookies));
            Assert.That(receivedRequest.Payload?.GetProperty("contextId").GetString(), Is.EqualTo("ctx-1"));
        });
    }

    [Test]
    public async Task BridgeCommandClientCloseWindowAsyncUsesStatusOnlyCommandWithWindowPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.CloseWindowAsync("session-a", "tab-1", "window-9").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.CloseWindow));
            Assert.That(receivedRequest.Payload?.GetProperty("windowId").GetString(), Is.EqualTo("window-9"));
        });
    }

    [Test]
    public async Task BridgeCommandClientActivateWindowAsyncUsesStatusOnlyCommandWithWindowPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.ActivateWindowAsync("session-a", "tab-1", "window-9").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.ActivateWindow));
            Assert.That(receivedRequest.Payload?.GetProperty("windowId").GetString(), Is.EqualTo("window-9"));
        });
    }

    [Test]
    public async Task BridgeCommandClientCloseWindowAsyncThrowsWhenBridgeReturnsErrorStatus()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.CloseWindowAsync("session-a", "tab-1", "window-9").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Error,
            Error = BridgeProtocolErrorCodes.InvalidPayload,
        }).ConfigureAwait(false);

        Assert.That(
            async () => await responseTask.ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.Contains("ошибка"));

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
    }

    [Test]
    public async Task BridgeCommandClientActivateWindowAsyncThrowsWhenBridgeReturnsErrorStatus()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.ActivateWindowAsync("session-a", "tab-1", "window-9").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Error,
            Error = BridgeProtocolErrorCodes.InvalidPayload,
        }).ConfigureAwait(false);

        Assert.That(
            async () => await responseTask.ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.Contains("ошибка"));

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
    }

    [Test]
    public async Task BridgeCommandClientCloseWindowAsyncLateResponseAfterTimeoutDoesNotCloseProtocol()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
            RequestTimeout = TimeSpan.FromMilliseconds(200),
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.CloseWindowAsync("session-a", "tab-1", "window-9").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.That(
            async () => await responseTask.ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.Contains("истекло время ожидания"));

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-2", windowId: "window-1")).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 1
            && snapshot.GetProperty("tabs").GetInt32() == 2
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.CloseWindow));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.EqualTo(2));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeCommandClientActivateWindowAsyncLateResponseAfterCancellationDoesNotCloseProtocol()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        using var requestCts = new CancellationTokenSource();
        var responseTask = server.Commands.ActivateWindowAsync("session-a", "tab-1", "window-9", requestCts.Token).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        requestCts.Cancel();

        Assert.That(
            async () => await responseTask.ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-2", windowId: "window-1")).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 1
            && snapshot.GetProperty("tabs").GetInt32() == 2
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.ActivateWindow));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.EqualTo(2));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeCommandClientDescribeElementAsyncReturnsTypedPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var responseTask = server.Commands.DescribeElementAsync("session-a", "tab-1", "element-1").AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var responsePayload = JsonDocument.Parse("{\"tagName\":\"DIV\",\"checked\":true,\"selectedIndex\":2,\"isActive\":true,\"isVisible\":true,\"associatedControlId\":\"control-7\",\"boundingBox\":{\"left\":1.5,\"top\":2.5,\"width\":3.5,\"height\":4.5},\"computedStyle\":{\"display\":\"block\",\"visibility\":\"visible\"},\"options\":[{\"value\":\"en\",\"text\":\"English\"},{\"value\":\"fr\",\"text\":\"French\"}]}");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = receivedRequest!.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = responsePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        var result = await responseTask.ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Command, Is.EqualTo(BridgeCommand.DescribeElement));
            Assert.That(receivedRequest.Payload?.GetProperty("elementId").GetString(), Is.EqualTo("element-1"));
            Assert.That(result.TagName, Is.EqualTo("DIV"));
            Assert.That(result.Checked, Is.True);
            Assert.That(result.SelectedIndex, Is.EqualTo(2));
            Assert.That(result.IsActive, Is.True);
            Assert.That(result.IsVisible, Is.True);
            Assert.That(result.AssociatedControlId, Is.EqualTo("control-7"));
            Assert.That(result.BoundingBox.X, Is.EqualTo(1.5f));
            Assert.That(result.BoundingBox.Y, Is.EqualTo(2.5f));
            Assert.That(result.BoundingBox.Width, Is.EqualTo(3.5f));
            Assert.That(result.BoundingBox.Height, Is.EqualTo(4.5f));
            Assert.That(result.ComputedStyle["display"], Is.EqualTo("block"));
            Assert.That(result.ComputedStyle["visibility"], Is.EqualTo("visible"));
            Assert.That(result.Options, Has.Length.EqualTo(2));
            Assert.That(result.Options[0].Value, Is.EqualTo("en"));
            Assert.That(result.Options[0].Text, Is.EqualTo("English"));
            Assert.That(result.Options[1].Value, Is.EqualTo("fr"));
            Assert.That(result.Options[1].Text, Is.EqualTo("French"));
        });
    }

    [Test]
    public async Task BridgeServerSendRequestAsyncRejectsMissingCommand()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        var outboundRequest = new BridgeMessage
        {
            Id = "request-missing-command",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
        };

        Assert.That(
            async () => await server.SendRequestAsync("session-a", outboundRequest).ConfigureAwait(false),
            Throws.ArgumentException.With.Message.Contains("содержать команду"));
    }

    [Test]
    public async Task BridgeServerSendRequestAsyncRejectsUnknownSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        var outboundRequest = new BridgeMessage
        {
            Id = "request-missing-session",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        Assert.That(
            async () => await server.SendRequestAsync("missing-session", outboundRequest).ConfigureAwait(false),
            Throws.InvalidOperationException.With.Message.Contains("не подключён"));
    }

    [Test]
    public async Task BridgeServerSendRequestAsyncRejectsUnregisteredTab()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-missing-tab",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        InvalidOperationException? observedException = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                await server.SendRequestAsync("session-a", outboundRequest).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("не подключён", StringComparison.Ordinal))
            {
                observedException = exception;
                await Task.Delay(25).ConfigureAwait(false);
                continue;
            }
            catch (InvalidOperationException exception)
            {
                observedException = exception;
                break;
            }

            Assert.Fail("SendRequestAsync unexpectedly succeeded for an unregistered tab.");
        }

        Assert.That(observedException, Is.Not.Null);
        Assert.That(observedException!.Message, Does.Contain("не зарегистрирована"));

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
    }

    [Test]
    public async Task BridgeServerPostHandshakeRequestRoutingTimesOutPendingResponse()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
            RequestTimeout = TimeSpan.FromMilliseconds(200),
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-timeout",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Type, Is.EqualTo(BridgeMessageType.Request));
            Assert.That(resolvedResponse.Id, Is.EqualTo(outboundRequest.Id));
            Assert.That(resolvedResponse.Type, Is.EqualTo(BridgeMessageType.Response));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Timeout));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.RequestTimeout));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
            Assert.That(health.GetProperty("completedRequests").GetInt64(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeLateResponseAfterTimeoutDoesNotCloseProtocol()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
            RequestTimeout = TimeSpan.FromMilliseconds(200),
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-late-timeout",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        var timedOutResponse = await responseTask.ConfigureAwait(false);
        using var latePayload = JsonDocument.Parse("\"Late Title\"");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = latePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-2", windowId: "window-1")).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 1
            && snapshot.GetProperty("tabs").GetInt32() == 2
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(timedOutResponse.Status, Is.EqualTo(BridgeStatus.Timeout));
            Assert.That(timedOutResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.RequestTimeout));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.EqualTo(2));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeGetTitleResponseRequiresStringPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-title-payload",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("42");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeGetUrlResponseRequiresStringPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-url-payload",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetUrl,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("42");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeGetContentResponseRequiresStringPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-content-payload",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetContent,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("42");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeRequestRoutingCancellationFailsPendingResponse()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-canceled",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        using var requestCts = new CancellationTokenSource();
        var responseTask = server.SendRequestAsync("session-a", outboundRequest, requestCts.Token).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        requestCts.Cancel();

        Assert.That(
            async () => await responseTask.ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Type, Is.EqualTo(BridgeMessageType.Request));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
            Assert.That(health.GetProperty("completedRequests").GetInt64(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeLateResponseAfterCancellationDoesNotCloseProtocol()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-late-cancel",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        using var requestCts = new CancellationTokenSource();
        var responseTask = server.SendRequestAsync("session-a", outboundRequest, requestCts.Token).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        requestCts.Cancel();

        Assert.That(
            async () => await responseTask.ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>());

        using var latePayload = JsonDocument.Parse("\"Late After Cancel\"");
        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = latePayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-2", windowId: "window-1")).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 1
            && snapshot.GetProperty("tabs").GetInt32() == 2
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.EqualTo(1));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.EqualTo(2));
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeGetWindowBoundsResponseRequiresRectanglePayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-window-bounds-payload",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetWindowBounds,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("{\"left\":10,\"top\":20,\"width\":300}");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeResolveElementScreenPointResponseRequiresPointPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-screen-point-payload",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.ResolveElementScreenPoint,
            Payload = JsonDocument.Parse("{\"elementId\":\"element-1\",\"scrollIntoView\":true}").RootElement.Clone(),
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("{\"viewportX\":10.5}");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeDebugPortStatusResponseRequiresTypedPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-debug-port-payload",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.DebugPortStatus,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("{\"tabId\":101,\"hasPort\":true,\"queueLength\":2,\"interceptEnabled\":false}");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeDescribeElementResponseRequiresRichPayload()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-describe-element-payload",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.DescribeElement,
            Payload = JsonDocument.Parse("{\"elementId\":\"element-1\"}").RootElement.Clone(),
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("{\"tagName\":\"DIV\",\"isVisible\":true}");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeDescribeElementResponseRejectsMalformedRichMembers()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-invalid-describe-element-rich-members",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.DescribeElement,
            Payload = JsonDocument.Parse("{\"elementId\":\"element-1\"}").RootElement.Clone(),
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        using var invalidPayload = JsonDocument.Parse("{\"tagName\":\"DIV\",\"isVisible\":true,\"boundingBox\":{\"left\":1.5,\"top\":2.5,\"width\":3.5,\"height\":4.5},\"options\":[{\"value\":\"en\"}]}");

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
            Status = BridgeStatus.Ok,
            Payload = invalidPayload.RootElement.Clone(),
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeTabDisconnectFailsPendingResponse()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-tab-disconnect",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabDisconnected, tabId: "tab-1")).ConfigureAwait(false);

        var resolvedResponse = await responseTask.ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("tabs").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Type, Is.EqualTo(BridgeMessageType.Request));
            Assert.That(resolvedResponse.Id, Is.EqualTo(outboundRequest.Id));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.TabDisconnected));
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeSessionDisconnectFailsPendingResponse()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-session-disconnect",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        var receivedRequest = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);

        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(receivedRequest, Is.Not.Null);
            Assert.That(receivedRequest!.Type, Is.EqualTo(BridgeMessageType.Request));
            Assert.That(resolvedResponse.Id, Is.EqualTo(outboundRequest.Id));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeUnknownResponseIdClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = "unknown-response",
            Type = BridgeMessageType.Response,
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeResponseWithoutStatusClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-missing-status",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-1",
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeResponseWithMismatchedTabIdClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: "tab-2", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 2).ConfigureAwait(false);

        var outboundRequest = new BridgeMessage
        {
            Id = "request-tab-mismatch",
            Type = BridgeMessageType.Request,
            TabId = "tab-1",
            Command = BridgeCommand.GetTitle,
        };

        var responseTask = server.SendRequestAsync("session-a", outboundRequest).AsTask();
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = outboundRequest.Id,
            Type = BridgeMessageType.Response,
            TabId = "tab-2",
            Status = BridgeStatus.Ok,
        }).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        var resolvedResponse = await responseTask.ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);
        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("pendingRequests").GetInt32() == 0
            && snapshot.GetProperty("failedRequests").GetInt64() == 1).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(resolvedResponse.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(resolvedResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("pendingRequests").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("failedRequests").GetInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeTabConnectedWithoutTabIdClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabConnected, tabId: null, windowId: "window-1")).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("tabs").GetInt32() == 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeTabDisconnectedWithoutTabIdClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(BridgeEvent.TabDisconnected)).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("tabs").GetInt32() == 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerPostHandshakeEventWithoutEventKindClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        var invalidEvent = new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Event,
        };

        await BridgeTestHelpers.SendMessageAsync(socket, invalidEvent).ConfigureAwait(false);

        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        var health = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot =>
            snapshot.GetProperty("sessions").GetInt32() == 0
            && snapshot.GetProperty("tabs").GetInt32() == 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(health.GetProperty("sessions").GetInt32(), Is.Zero);
            Assert.That(health.GetProperty("tabs").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeRejectsSecretMismatch()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "wrong-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(response.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(response.Error, Is.EqualTo(BridgeProtocolErrorCodes.SecretMismatch));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeRejectsDuplicateSessionId()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var firstSocket = new ClientWebSocket();
        await firstSocket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(firstSocket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(firstSocket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        using var secondSocket = new ClientWebSocket();
        await secondSocket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(secondSocket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);

        var secondResponse = await BridgeTestHelpers.ReceiveBridgeMessageAsync(secondSocket).ConfigureAwait(false);

        await firstSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(secondResponse, Is.Not.Null);
            Assert.That(secondResponse!.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(secondResponse.Error, Is.EqualTo(BridgeProtocolErrorCodes.DuplicateSessionId));
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeRejectsSecondHandshakeOnSameConnection()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        var secondHandshake = new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Handshake,
            Payload = JsonSerializer.SerializeToElement(new BridgeHandshakeClientPayload(
                SessionId: "session-a",
                Secret: "test-secret",
                ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
                BrowserFamily: "chromium",
                ExtensionVersion: "1.0.0"), BridgeJsonContext.Default.BridgeHandshakeClientPayload),
        };

        await BridgeTestHelpers.SendMessageAsync(socket, secondHandshake).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(response.Error, Is.EqualTo(BridgeProtocolErrorCodes.InvalidPayload));
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeRejectsUnsupportedProtocolVersion()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: 99,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        var rejectPayload = response?.Payload?.Deserialize(BridgeJsonContext.Default.BridgeHandshakeRejectPayload);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(response.Error, Is.EqualTo(BridgeProtocolErrorCodes.UnsupportedProtocolVersion));
            Assert.That(rejectPayload?.Retryable, Is.True);
            Assert.That(rejectPayload?.SupportedProtocolVersion, Is.EqualTo(BridgeHandshakeValidator.CurrentProtocolVersion));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeRejectsNonHandshakeFirstMessage()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);

        var message = new BridgeMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = BridgeMessageType.Request,
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, BridgeJsonContext.Default.BridgeMessage);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(response.Error, Is.EqualTo(BridgeProtocolErrorCodes.InvalidPayload));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeRejectsMissingSessionId()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: string.Empty,
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(response.Error, Is.EqualTo(BridgeProtocolErrorCodes.MissingSessionId));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeRejectsMissingExtensionVersion()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: string.Empty)).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(response.Error, Is.EqualTo(BridgeProtocolErrorCodes.InvalidPayload));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerWebSocketHandshakeClosesWithoutRejectWhenCorrelationIdMissing()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);

        var message = new BridgeMessage
        {
            Id = string.Empty,
            Type = BridgeMessageType.Handshake,
            Payload = JsonSerializer.SerializeToElement(new BridgeHandshakeClientPayload(
                SessionId: "session-a",
                Secret: "test-secret",
                ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
                BrowserFamily: "chromium",
                ExtensionVersion: "1.0.0"), BridgeJsonContext.Default.BridgeHandshakeClientPayload),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, BridgeJsonContext.Default.BridgeMessage);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);

        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
        if (result.MessageType is WebSocketMessageType.Close)
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Close));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerWebSocketCloseBeforeHandshakeDoesNotRegisterSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/bridge"), CancellationToken.None).ConfigureAwait(false);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);

        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(server.ConnectionCount, Is.Zero);
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
        });
    }

    [Test]
    public async Task BridgeServerAcceptedConnectionCloseCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerAcceptedConnectionInvalidJsonClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        var invalidBytes = Encoding.UTF8.GetBytes("not-json");
        await socket.SendAsync(invalidBytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }

    [Test]
    public async Task BridgeServerAcceptedConnectionBinaryFrameClosesProtocolAndCleansUpSession()
    {
        await using var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
        });

        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        var binaryPayload = new byte[] { 1, 2, 3, 4 };
        await socket.SendAsync(binaryPayload.AsMemory(), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.ReceiveCloseAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.ProtocolError));
            Assert.That(server.ConnectionCount, Is.Zero);
        });
    }
}

file sealed record TestLogEntry(LogLevel Level, string Message);

file sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly List<TestLogEntry> entries = [];

    internal IReadOnlyList<TestLogEntry> Entries => entries;

    public ILogger CreateLogger(string categoryName)
        => new TestLogger(entries);

    public void Dispose()
    {
    }

    private sealed class TestLogger(List<TestLogEntry> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Add(new TestLogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            internal static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}