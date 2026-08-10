using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Бюджет мостового запроса при ожидании элемента.
/// </summary>
/// <remarks>
/// Ожидание элемента — единственная команда, живущая дольше обычного запроса: столько, сколько
/// попросил вызывающий. Раньше весь обмен обрубался общим RequestTimeout (5 с по умолчанию), поэтому
/// ожидание в 30 с молча превращалось в пятисекундное, а обрыв транспорта возвращался вызывающему
/// как <see langword="null"/> — «элемент не найден». Отличить настоящее отсутствие элемента от
/// неотвечающей вкладки было невозможно ни по коду, ни по журналу.
/// </remarks>
[TestFixture]
public sealed class WebDriverWaitForElementTimeoutTests
{
    private static JsonObject CreateWaitPayload()
        => new()
        {
            ["strategy"] = "css",
            ["value"] = "#late",
            ["kind"] = "Attached",
            ["timeoutMs"] = 3000,
        };

    private static async Task<(BridgeServer Server, ClientWebSocket Socket)> StartBridgeAsync(TimeSpan requestTimeout)
    {
        var server = new BridgeServer(new BridgeSettings
        {
            Secret = "test-secret",
            RequestTimeout = requestTimeout,
        });

        await server.StartAsync().ConfigureAwait(false);

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
        await BridgeTestHelpers.WaitForConnectionCountAsync(server, expected: 1).ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, BridgeTestHelpers.CreateEventMessage(
            BridgeEvent.TabConnected, tabId: "tab-1", windowId: "window-1")).ConfigureAwait(false);
        _ = await BridgeTestHelpers.WaitForHealthAsync(server, static snapshot => snapshot.GetProperty("tabs").GetInt32() == 1).ConfigureAwait(false);

        return (server, socket);
    }

    // Ядро регрессии: вкладка отвечает позже общего RequestTimeout, но в пределах таймаута
    // вызывающего. Прежний код успевал бросить запрос и вернуть null.
    [Test]
    public async Task WaitForElementHonoursCallerTimeoutInsteadOfTruncatingItToTheRequestTimeout()
    {
        var (server, socket) = await StartBridgeAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

        await using (server)
        using (socket)
        {
            var client = new PageBridgeCommandClient("session-a", "tab-1", server.Commands);
            var waitTask = client.WaitForElementAsync(CreateWaitPayload(), TimeSpan.FromSeconds(3)).AsTask();

            var request = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
            Assert.That(request?.Command, Is.EqualTo(BridgeCommand.WaitForElement));

            // Втрое дольше RequestTimeout — и втрое короче того, что попросил вызывающий.
            await Task.Delay(750).ConfigureAwait(false);

            using var payload = JsonDocument.Parse("\"element-7\"");
            await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
            {
                Id = request!.Id,
                Type = BridgeMessageType.Response,
                TabId = "tab-1",
                Status = BridgeStatus.Ok,
                Payload = payload.RootElement.Clone(),
            }).ConfigureAwait(false);

            Assert.That(await waitTask.ConfigureAwait(false), Is.EqualTo("element-7"));
        }
    }

    // Истечение ожидания в самой вкладке — законный отрицательный ответ, а не сбой.
    [Test]
    public async Task WaitForElementReturnsNullWhenTheTabReportsItsOwnTimeout()
    {
        var (server, socket) = await StartBridgeAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

        await using (server)
        using (socket)
        {
            var client = new PageBridgeCommandClient("session-a", "tab-1", server.Commands);
            var waitTask = client.WaitForElementAsync(CreateWaitPayload(), TimeSpan.FromSeconds(2)).AsTask();

            var request = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
            await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
            {
                Id = request!.Id,
                Type = BridgeMessageType.Response,
                TabId = "tab-1",
                Status = BridgeStatus.Timeout,
                Error = "Элемент не появился в течение таймаута.",
            }).ConfigureAwait(false);

            Assert.That(await waitTask.ConfigureAwait(false), Is.Null);
        }
    }

    // А вот молчащая вкладка обязана стать исключением, а не тем же самым null: иначе неотвечающий
    // браузер неотличим от отсутствующего элемента.
    [Test]
    public async Task WaitForElementThrowsWhenTheTabNeverAnswersInsteadOfReportingElementNotFound()
    {
        var (server, socket) = await StartBridgeAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

        await using (server)
        using (socket)
        {
            var client = new PageBridgeCommandClient("session-a", "tab-1", server.Commands);
            var waitTask = client.WaitForElementAsync(CreateWaitPayload(), TimeSpan.FromMilliseconds(400)).AsTask();

            var request = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);
            Assert.That(request?.Command, Is.EqualTo(BridgeCommand.WaitForElement));

            var exception = Assert.CatchAsync(async () => await waitTask.ConfigureAwait(false));
            Assert.That(exception, Is.InstanceOf<BridgeCommandException>());
            Assert.That(((BridgeCommandException)exception!).Status, Is.EqualTo(BridgeStatus.Timeout));
        }
    }
}
