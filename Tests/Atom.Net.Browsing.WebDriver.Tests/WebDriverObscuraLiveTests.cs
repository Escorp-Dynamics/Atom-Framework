using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol.Cdp;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Живая проверка против настоящего <c lang="text">obscura serve</c>: скачивание закреплённого релиза,
/// запуск сервера с токеном и сквозной CDP-сценарий. Без сети тесты пропускаются.
/// </summary>
/// <remarks>
/// Явный бинарь можно задать переменной <c lang="text">ATOM_WEBDRIVER_OBSCURA_PATH</c>, иначе используется кэш драйвера.
/// </remarks>
[TestFixture]
[Category("Obscura")]
[NonParallelizable]
public sealed class WebDriverObscuraLiveTests
{
    private static string? executablePath;

    [OneTimeSetUp]
    public async Task ProvisionAsync()
    {
        var explicitPath = Environment.GetEnvironmentVariable(ObscuraProfile.BinaryPathVariableName);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            executablePath = explicitPath;
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            executablePath = await ObscuraBinaryProvisioner.EnsureAsync(ObscuraBuildVariant.Default, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or PlatformNotSupportedException)
        {
            Assert.Ignore("Obscura недоступна (нет сети или платформа не поддерживается): " + exception.Message);
        }
    }

    [Test]
    public async Task ServerStartsWithTokenAndServesCdpSession()
    {
        await using var server = await ObscuraServer.StartAsync(
            executablePath!,
            new ObscuraServerOptions(),
            logger: null,
            CancellationToken.None).ConfigureAwait(false);

        await using var connection = await server.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        var version = await connection.SendAsync("Browser.getVersion").ConfigureAwait(false);
        var context = await connection.SendAsync("Target.createBrowserContext").ConfigureAwait(false);
        var contextId = context.GetProperty("browserContextId").GetString()!;
        var target = await connection.SendAsync("Target.createTarget", new JsonObject { ["url"] = "about:blank", ["browserContextId"] = contextId }).ConfigureAwait(false);
        var targetId = target.GetProperty("targetId").GetString()!;
        var attached = await connection.SendAsync("Target.attachToTarget", new JsonObject { ["targetId"] = targetId, ["flatten"] = true }).ConfigureAwait(false);
        var sessionId = attached.GetProperty("sessionId").GetString()!;

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.EventReceived += cdpEvent =>
        {
            if (cdpEvent.Method == "Page.loadEventFired" && cdpEvent.SessionId == sessionId)
                loaded.TrySetResult();
        };

        await connection.SendAsync("Page.enable", sessionId: sessionId).ConfigureAwait(false);
        await connection.SendAsync("Page.navigate", new JsonObject { ["url"] = "data:text/html,<title>Atom</title><p id=x>obscura</p>" }, sessionId).ConfigureAwait(false);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

        var evaluated = await connection.SendAsync(
            "Runtime.evaluate",
            new JsonObject { ["expression"] = "document.title + '|' + document.getElementById('x').textContent", ["returnByValue"] = true },
            sessionId).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(version.GetProperty("protocolVersion").GetString(), Is.EqualTo("1.3"));
            Assert.That(evaluated.GetProperty("result").GetProperty("value").GetString(), Is.EqualTo("Atom|obscura"));
            Assert.That(Directory.Exists(server.RunDirectory), Is.True);
        });

        await connection.SendAsync("Target.disposeBrowserContext", new JsonObject { ["browserContextId"] = contextId }).ConfigureAwait(false);
    }

    [Test]
    public async Task ServerRejectsConnectionWithoutToken()
    {
        await using var server = await ObscuraServer.StartAsync(
            executablePath!,
            new ObscuraServerOptions(),
            logger: null,
            CancellationToken.None).ConfigureAwait(false);

        _ = await AsyncAssert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(async () =>
        {
            await using var unauthorized = await CdpConnection.ConnectAsync(server.BrowserEndpoint, bearerToken: null, CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task DisposeStopsProcessAndRemovesRunDirectory()
    {
        var server = await ObscuraServer.StartAsync(
            executablePath!,
            new ObscuraServerOptions(),
            logger: null,
            CancellationToken.None).ConfigureAwait(false);
        var processId = server.Process.Id;
        var runDirectory = server.RunDirectory;

        await server.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(IsAlive(processId), Is.False);
            Assert.That(Directory.Exists(runDirectory), Is.False);
        });
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
