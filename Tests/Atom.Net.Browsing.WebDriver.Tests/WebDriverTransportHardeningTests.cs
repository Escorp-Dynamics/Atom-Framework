using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Регрессионные проверки устойчивости транспортного слоя, прокси и реестра proxy-решений
/// (глубокий анализ модуля: подтверждённые дефекты transport/proxy и гонки реестра).
/// </summary>
[TestFixture]
public sealed class WebDriverTransportHardeningTests
{
    private const int SettledRequestIdsBound = 16 * 1024;

    [Test]
    public async Task BridgeServerRespondsPongToKeepAlivePing()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        using var socket = await ConnectHandshakedSocketAsync(server, "session-ping").ConfigureAwait(false);

        await BridgeTestHelpers.SendMessageAsync(socket, new BridgeMessage
        {
            Id = "ping-1",
            Type = BridgeMessageType.Ping,
        }).ConfigureAwait(false);

        var pong = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(pong, Is.Not.Null);
            Assert.That(pong!.Type, Is.EqualTo(BridgeMessageType.Pong));
            Assert.That(pong.Id, Is.EqualTo("ping-1"));
        });
    }

    [Test]
    public async Task BridgeServerAcceptsHandshakeMessageLargerThan16Kilobytes()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);

        // Сообщение больше прежнего фиксированного буфера 16 КБ: ранее урезанный JSON
        // ломал десериализацию и рукопожатие отклонялось/зависало.
        var payload = new JsonObject
        {
            ["sessionId"] = "session-large-handshake",
            ["secret"] = "test-secret",
            ["protocolVersion"] = BridgeHandshakeValidator.CurrentProtocolVersion,
            ["browserFamily"] = "chromium",
            ["extensionVersion"] = "1.0.0",
            ["capabilities"] = new JsonObject
            {
                ["padding"] = new string('x', 20 * 1024),
            },
        };
        var message = new JsonObject
        {
            ["id"] = "handshake-large-1",
            ["type"] = "Handshake",
            ["payload"] = payload,
        };

        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);

        var response = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null, "Ожидался Handshake-ответ, а не закрытие или урезанный JSON");
            Assert.That(response!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(response.Status, Is.EqualTo(BridgeStatus.Ok));
        });
    }

    [Test]
    public async Task BridgeServerInterceptRouteFallsBackToContinueWhenRequestHandlerThrows()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;

        page.Request += (_, _) => throw new InvalidOperationException("hardening-test failure");

        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "crash-request-1",
                ["tabId"] = page.TabId,
                ["url"] = "https://example.test/crash-handler",
                ["method"] = "GET",
                ["type"] = "script",
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("continue"));
        });
    }

    [Test]
    public async Task BridgeServerDiscoveryPageExposesNoSecretAndNoCorsWildcard()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/").ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("atom-bridge-port"));
            Assert.That(body, Does.Not.Contain("atom-bridge-secret"), "Discovery-страница не должна разглашать секрет моста");
            Assert.That(body, Does.Not.Contain("test-secret"), "Discovery-страница не должна содержать значение секрета");
            Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.False, "ACAO-заголовок с wildcard подрывает защиту от DNS rebinding");
        });
    }

    [Test]
    public async Task BridgeServerRejectsRequestWithForeignHostHeader()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        using var client = new HttpClient();

        using (var foreignRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/health"))
        {
            foreignRequest.Headers.Host = "hardening-evil.example.test";
            using var foreignResponse = await client.SendAsync(foreignRequest).ConfigureAwait(false);
            // На Unix HttpListener с точным loopback-prefix может отвергнуть чужой Host
            // ещё до managed handler и вернуть 404; в обоих случаях маршрут недоступен и
            // DNS-rebinding не достигает utility/bridge endpoint-ов.
            Assert.That(foreignResponse.StatusCode,
                Is.EqualTo(HttpStatusCode.Forbidden).Or.EqualTo(HttpStatusCode.NotFound),
                "DNS-rebinding через чужой Host должен отклоняться");
        }

        using var loopbackRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/health");
        loopbackRequest.Headers.Host = $"127.0.0.1:{server.Port}";
        using var loopbackResponse = await client.SendAsync(loopbackRequest).ConfigureAwait(false);
        Assert.That(loopbackResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task BridgeServerReconnectWithSameSessionIdSupersedesStaleSocket()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        using var staleSocket = await ConnectHandshakedSocketAsync(server, "session-dup").ConfigureAwait(false);
        using var freshSocket = new ClientWebSocket();
        await freshSocket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(freshSocket, BridgeTestHelpers.CreateClientPayload(sessionId: "session-dup")).ConfigureAwait(false);

        var accept = await BridgeTestHelpers.ReceiveBridgeMessageAsync(freshSocket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(accept, Is.Not.Null, "Быстрый реконнект с тем же sessionId должен вытеснять зомби-сессию, а не отклоняться");
            Assert.That(accept!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(accept.Status, Is.EqualTo(BridgeStatus.Ok));
        });

        // Прежний сокет вытеснен: его цикл чтения завершается закрытием состояния.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var probeBuffer = new byte[256];
        while (DateTime.UtcNow < deadline && staleSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = await staleSocket.ReceiveAsync(probeBuffer.AsMemory(), receiveTimeout.Token).ConfigureAwait(false);
                if (result.MessageType is WebSocketMessageType.Close)
                    break;
            }
            catch (WebSocketException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
        }

        Assert.That(staleSocket.State, Is.Not.EqualTo(WebSocketState.Open), "Зомби-сокет прежнего коннекта должен закрыться после вытеснения");
    }

    [Test]
    public async Task BridgeServerStateSettledRequestIdsStayBounded()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(new BridgeSessionDescriptor("session-settled", 1, "chromium", "1.0.0")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-settled", "tab-1")).ConfigureAwait(false);

        var total = SettledRequestIdsBound + 128;
        for (var index = 0; index < total; index++)
        {
            var messageId = $"request-{index}";
            await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor(messageId, "session-settled", "tab-1")).ConfigureAwait(false);
            var completion = await state.TryCompletePendingRequestAsync(messageId, new BridgeMessage
            {
                Id = messageId,
                Type = BridgeMessageType.Response,
                Status = BridgeStatus.Ok,
            }).ConfigureAwait(false);

            Assert.That(completion.Outcome, Is.EqualTo(PendingRequestCompletionResultKind.Completed));
        }

        // Самые старые записи вытеснены: повторная регистрация того же messageId снова допустима.
        var evictedReAdd = await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-0", "session-settled", "tab-1")).ConfigureAwait(false);

        // Недавние записи ещё помнятся: дубликаты отклоняются.
        var recentReAdd = await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor($"request-{total - 1}", "session-settled", "tab-1")).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(evictedReAdd.Outcome, Is.EqualTo(PendingRequestAddResultKind.Added),
                "Вытесненный за пределы окна messageId не должен вечно считаться settled");
            Assert.That(recentReAdd.Outcome, Is.EqualTo(PendingRequestAddResultKind.DuplicateMessageId),
                "Недавний messageId всё ещё должен попадать под duplicate-защиту");
        });
    }

    [Test]
    public void ProxyNavigationRegistryKeepsSharedTokenRouteWhenOneContextDetached()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        var routeA = CreateRoute("session-1", "tab-1", "ctx-a", "shared-token", 1);
        var routeB = CreateRoute("session-1", "tab-2", "ctx-b", "shared-token", 2);

        registry.UpsertRoute(routeA);
        registry.UpsertRoute(routeB);

        Assert.Multiple(() =>
        {
            // Один контекст отвязан: маршрут с общим токеном живёт, пока привязан второй.
            Assert.That(registry.RemoveRouteByContextId("ctx-a"), Is.True);
            Assert.That(registry.TryResolveToken("ctx-a", out _), Is.False);
            Assert.That(registry.TryResolveToken("ctx-b", out var tokenB), Is.True);
            Assert.That(tokenB, Is.EqualTo("shared-token"));
            Assert.That(registry.TryResolveRoute("shared-token", out var route), Is.True);
            Assert.That(route!.TabId, Is.EqualTo("tab-2"));
        });
    }

    [Test]
    public void ProxyNavigationRegistryRetargetOfSharedTokenDoesNotRemoveRouteForOtherContext()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("session-1", "tab-1", "ctx-a", "token-old", 1));
        registry.UpsertRoute(CreateRoute("session-1", "tab-2", "ctx-b", "token-old", 2));

        registry.UpsertRoute(CreateRoute("session-1", "tab-1", "ctx-a", "token-new", 3));

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryResolveToken("ctx-a", out var tokenA), Is.True);
            Assert.That(tokenA, Is.EqualTo("token-new"));
            Assert.That(registry.TryResolveToken("ctx-b", out var tokenB), Is.True);
            Assert.That(tokenB, Is.EqualTo("token-old"), "Смена токена одного контекста не должна снимать маршрут другого контекста");
            Assert.That(registry.TryResolveRoute("token-old", out _), Is.True);
            Assert.That(registry.TryResolveRoute("token-new", out _), Is.True);
        });
    }

    [Test]
    public void ProxyNavigationRegistrySurvivesConcurrentUpsertAndRemoveWithoutResurrection()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        const int workers = 8;
        const int iterations = 250;
        Exception? failure = null;

        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Run(() =>
        {
            try
            {
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    var contextId = $"ctx-{worker % 4}";
                    var token = iteration % 2 == 0 ? "token-even" : "token-odd";
                    registry.UpsertRoute(CreateRoute("session-1", $"tab-{worker}", contextId, token, (worker * 1000L) + iteration));

                    if (iteration % 3 == 0)
                        registry.RemoveRouteByContextId(contextId);
                }
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref failure, exception);
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Null);

            // Инвариант: каждое живое отображение contextId -> token обязано вести на живой маршрут
            // (до исправления конкурентный Upsert/Remove мог «воскресить» отображение на удалённый маршрут).
            for (var contextIndex = 0; contextIndex < 4; contextIndex++)
            {
                var contextId = $"ctx-{contextIndex}";
                if (registry.TryResolveToken(contextId, out var token))
                    Assert.That(registry.TryResolveRoute(token, out _), Is.True, $"Отображение {contextId} ведёт на удалённый маршрут {token}");
            }
        });
    }

    [Test]
    public async Task NavigationProxyForwardsRequestWithoutDecisionAsImplicitContinue()
    {
        await using var origin = TestLoopbackOrigin.Start("atom-implicit-continue");
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("session-1", "tab-1", "ctx-proxy", "proxy-token-continue", 1));
        server.ConfigureNavigationProxyDecisions(registry);
        await server.StartAsync().ConfigureAwait(false);

        var response = await SendProxyRequestAsync(
            server.NavigationProxyPort,
            $"GET http://127.0.0.1:{origin.Port}/implicit-continue HTTP/1.1\r\n"
            + $"Host: 127.0.0.1:{origin.Port}\r\n"
            + $"Proxy-Authorization: {CreateProxyAuthorizationHeader("proxy-token-continue")}\r\n"
            + "\r\n").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Does.Contain("HTTP/1.1 200"), "Валидный маршрут без решения должен форвардиться прозрачно, а не отвечать 502 decision-missing");
            Assert.That(response, Does.Contain("atom-implicit-continue"));
            Assert.That(origin.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task NavigationProxyForwardsChunkedRequestBodyIntact()
    {
        await using var origin = TestLoopbackOrigin.Start(readBody: true);
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("session-1", "tab-1", "ctx-proxy-chunked", "proxy-token-chunked", 1));
        server.ConfigureNavigationProxyDecisions(registry);
        await server.StartAsync().ConfigureAwait(false);

        const string body = "chunked-request-payload";
        var chunked = $"POST http://127.0.0.1:{origin.Port}/chunked-upload HTTP/1.1\r\n"
            + $"Host: 127.0.0.1:{origin.Port}\r\n"
            + $"Proxy-Authorization: {CreateProxyAuthorizationHeader("proxy-token-chunked")}\r\n"
            + "Transfer-Encoding: chunked\r\n"
            + "Content-Type: text/plain\r\n"
            + "\r\n"
            + "9\r\nchunked-r\r\n"
            + "e\r\nequest-payload\r\n"
            + "0\r\n\r\n";

        var response = await SendProxyRequestAsync(server.NavigationProxyPort, chunked).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Does.Contain("HTTP/1.1 200"));
            Assert.That(origin.LastRequestBody, Is.EqualTo(body), "Тело, присланное chunked-кодированием, должно доходить до origin без искажений");
        });
    }

    [Test]
    public async Task NavigationProxyFiltersHeaderInjectionInFulfillDecision()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("session-1", "tab-1", "ctx-crlf", "proxy-token-crlf", 1));
        server.ConfigureNavigationProxyDecisions(registry);

        var now = DateTimeOffset.UtcNow;
        var enqueued = registry.EnqueueDecision("ctx-crlf", new ProxyNavigationPendingDecision
        {
            RequestId = "req-crlf-1",
            Method = "GET",
            AbsoluteUrl = "http://example.test/crlf-probe",
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(1),
            Action = ProxyNavigationDecisionAction.Fulfill,
            StatusCode = (int)HttpStatusCode.OK,
            ResponseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Safe"] = "yes",
                ["X-Corrupted"] = "ok\r\nX-Injected: evil",
            },
            ResponseBody = Encoding.UTF8.GetBytes("fulfill-body"),
        }, now);
        Assert.That(enqueued, Is.True);

        await server.StartAsync().ConfigureAwait(false);

        var response = await SendProxyRequestAsync(
            server.NavigationProxyPort,
            "GET http://example.test/crlf-probe HTTP/1.1\r\n"
            + "Host: example.test\r\n"
            + $"Proxy-Authorization: {CreateProxyAuthorizationHeader("proxy-token-crlf")}\r\n"
            + "\r\n").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(response, Does.Contain("fulfill-body"));
            Assert.That(response, Does.Contain("X-Safe: yes"));
            Assert.That(response, Does.Not.Contain("X-Injected"), "CRLF-инъекция через заголовок решения должна отфильтровываться");
            Assert.That(response, Does.Not.Contain("X-Corrupted"), "Невалидный заголовок пропускается целиком");
        });
    }

    [Test]
    public async Task InterceptMainFramePlainContinueEnqueuesNonExpiredProxyDecision()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;
        var contextId = page.GetOrCreateBridgeContextId();

        browser.ProxyNavigationDecisions.UpsertRoute(new ProxyNavigationRoute
        {
            SessionId = "session-1",
            TabId = page.TabId,
            ContextId = contextId,
            RouteToken = "proxy-token-ttl",
            Revision = 1,
        });

        // Обработчиков нет: решение по умолчанию — plain continue. Timestamp запроса умышленно
        // «в прошлом»: TTL отложенного решения отсчитывается от серверных UtcNow, а не от него.
        using var client = new HttpClient();
        using var response = await PostJsonAsync(client,
            $"http://127.0.0.1:{server.Port}/intercept?secret=test-secret",
            new JsonObject
            {
                ["requestId"] = "ttl-request-1",
                ["tabId"] = page.TabId,
                ["url"] = "https://example.test/ttl-navigation",
                ["method"] = "GET",
                ["type"] = "main_frame",
                ["supportsNavigationFulfillment"] = true,
                ["timestamp"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds(),
            }).ConfigureAwait(false);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
        var consumed = browser.ProxyNavigationDecisions.TryConsumeDecision(
            "proxy-token-ttl",
            "GET",
            "https://example.test/ttl-navigation",
            DateTimeOffset.UtcNow,
            out var decision);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload["action"]?.GetValue<string>(), Is.EqualTo("continue"));
            Assert.That(consumed, Is.True, "Plain continue для main_frame в proxy-режиме обязан оставлять съедобное решение, иначе прокси вернул бы 502");
            Assert.That(decision, Is.Not.Null);
            Assert.That(decision!.Action, Is.EqualTo(ProxyNavigationDecisionAction.Continue));
            Assert.That(decision!.IssuedAtUtc, Is.GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)), "IssuedAtUtc вычисляется по серверным часам, а не по timestamp расширения");
        });
    }

    [Test]
    public async Task NavigationInterceptionModeTurnsProxyOnlyWhileInterceptionEnabled()
    {
        await using var server = new BridgeServer(BridgeTestHelpers.CreateSettings());
        await server.StartAsync().ConfigureAwait(false);

        var plan = CreateBridgeBootstrapPlan("session-proxy-mode");
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: server, bridgeBootstrap: plan);
        var page = (WebPage)browser.CurrentPage;

        // Перехват выключен: proxy-маршрут не создаётся.
        var disabledPayload = WebBrowser.BuildSetTabContextPayload(page);
        Assert.Multiple(() =>
        {
            Assert.That(disabledPayload["navigationInterceptionMode"]?.GetValue<string>(), Is.EqualTo("webrequest"));
            Assert.That(disabledPayload.ContainsKey("navigationProxyRouteToken"), Is.False);
        });

        // Перехват включён: автоматически регистрируется proxy-маршрут с непредсказуемым токеном.
        await page.SetRequestInterceptionAsync(true, CancellationToken.None).ConfigureAwait(false);
        var enabledPayload = WebBrowser.BuildSetTabContextPayload(page);
        var contextId = page.GetOrCreateBridgeContextId();

        Assert.Multiple(() =>
        {
            Assert.That(enabledPayload["navigationInterceptionMode"]?.GetValue<string>(), Is.EqualTo("proxy"));
            var token = enabledPayload["navigationProxyRouteToken"]?.GetValue<string>();
            Assert.That(token, Is.Not.Null.And.Not.Empty);
            Assert.That(token!, Does.StartWith("nav-"));
            Assert.That(token!.Length, Is.GreaterThanOrEqualTo(40), "Токен маршрута — ещё и пароль локального прокси: минимум энтропии обязателен");
            Assert.That(browser.ProxyNavigationDecisions.TryResolveToken(contextId, out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(token));
            Assert.That(browser.ProxyNavigationDecisions.TryResolveRoute(token!, out _), Is.True);
        });

        // Перехват выключен снова: автоматический маршрут снимается, режим откатывается.
        await page.SetRequestInterceptionAsync(false, CancellationToken.None).ConfigureAwait(false);
        var revertedPayload = WebBrowser.BuildSetTabContextPayload(page);

        Assert.Multiple(() =>
        {
            Assert.That(revertedPayload["navigationInterceptionMode"]?.GetValue<string>(), Is.EqualTo("webrequest"));
            Assert.That(revertedPayload.ContainsKey("navigationProxyRouteToken"), Is.False);
            Assert.That(browser.ProxyNavigationDecisions.TryResolveToken(contextId, out _), Is.False);
        });
    }

    [Test]
    public void ManagedDeliveryServerCertificateUsesFastEcdsaLeaf()
    {
        var host = $"hardening-{Guid.NewGuid():N}.invalid";

        using var leaf = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateCertificate(host);
        using var authority = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateAuthorityCertificate();

        Assert.Multiple(() =>
        {
            // Per-host сертификаты генерируются на hot-path CONNECT: RSA-2048 keygen на критическом
            // пути заменён на ECDSA P-256, корректная подпись — ECDSA/RSA от корня.
            Assert.That(leaf.GetECDsaPublicKey(), Is.Not.Null, "Leaf-сертификат хоста должен быть ECDSA P-256");
            Assert.That(authority.GetRSAPublicKey(), Is.Not.Null, "Корневой CA остаётся RSA-2048");
        });
    }

    [Test]
    public void ManagedDeliveryCertificateFileIsOwnerOnlyOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Проверка прав 0600 применима только к Unix-хостам");
            return;
        }

        var host = $"hardening-{Guid.NewGuid():N}.invalid";
        using var leaf = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateCertificate(host);

        var directory = Path.Combine(ResolveCertificateBaseDirectory(), "Escorp", "Atom", "WebDriver");
        var sanitizedHost = host.Replace(':', '_').Replace('.', '_');
        var certificatePath = Path.Combine(directory, $"managed-delivery-server-{sanitizedHost}.pfx");

        Assert.Multiple(() =>
        {
            Assert.That(leaf.HasPrivateKey, Is.True);
            Assert.That(File.Exists(certificatePath), Is.True, "Leaf-сертификат должен персиститься на диске");
        });

        var mode = File.GetUnixFileMode(certificatePath);
        Assert.That(mode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite),
            $"PFX с закрытым ключом без пароля обязан быть доступен только владельцу, фактический режим: {mode}");
    }

    private static async Task<ClientWebSocket> ConnectHandshakedSocketAsync(BridgeServer server, string sessionId)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(BridgeTestHelpers.CreateBridgeUri(server), CancellationToken.None).ConfigureAwait(false);
        await BridgeTestHelpers.SendHandshakeAsync(socket, BridgeTestHelpers.CreateClientPayload(sessionId: sessionId)).ConfigureAwait(false);
        var accept = await BridgeTestHelpers.ReceiveBridgeMessageAsync(socket).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(accept, Is.Not.Null);
            Assert.That(accept!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(accept.Status, Is.EqualTo(BridgeStatus.Ok));
        });

        return socket;
    }

    private static ProxyNavigationRoute CreateRoute(string sessionId, string tabId, string contextId, string routeToken, long revision)
        => new()
        {
            SessionId = sessionId,
            TabId = tabId,
            ContextId = contextId,
            RouteToken = routeToken,
            Revision = revision,
        };

    private static string CreateProxyAuthorizationHeader(string routeToken)
        => $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(routeToken, ':')))}";

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, JsonObject payload)
        => client.PostAsync(url, new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));

    private static async Task<string> SendProxyRequestAsync(int proxyPort, string requestText)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort).ConfigureAwait(false);
        await using var stream = client.GetStream();

        var requestBytes = Encoding.ASCII.GetBytes(requestText);
        await stream.WriteAsync(requestBytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);

        using var responseBuffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk).ConfigureAwait(false);
            if (read <= 0)
                break;

            await responseBuffer.WriteAsync(chunk.AsMemory(0, read)).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(responseBuffer.ToArray());
    }

    private static string ResolveCertificateBaseDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(basePath))
            return basePath;

        basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Зеркалит fallback-логику BridgeManagedDeliveryCertificateManager.
        return string.IsNullOrWhiteSpace(basePath)
            ? Path.Combine(Path.GetTempPath(), "Escorp", "Atom")
            : basePath;
    }

    private static BridgeBootstrapPlan CreateBridgeBootstrapPlan(string sessionId)
    {
        var root = Path.Combine(Path.GetTempPath(), "atom-webdriver-hardening-tests", sessionId);
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

    /// <summary>
    /// Минимальный loopback-origin: принимает HTTP-запрос прокси и отвечает фиксированным телом
    /// (с опциональным чтением тела запроса для проверки chunked-декодирования).
    /// </summary>
    private sealed class TestLoopbackOrigin : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly string? fixedBody;
        private readonly bool readBody;
        private readonly CancellationTokenSource shutdown = new();
        private readonly Task acceptLoop;
        private int requestCount;

        private TestLoopbackOrigin(string? fixedBody, bool readBody)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            this.fixedBody = fixedBody;
            this.readBody = readBody;
            listener.Start();
            acceptLoop = Task.Run(AcceptLoopAsync, CancellationToken.None);
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public int RequestCount => Volatile.Read(ref requestCount);

        public string? LastRequestBody { get; private set; }

        public static TestLoopbackOrigin Start(string fixedBody)
            => new(fixedBody, readBody: false);

        public static TestLoopbackOrigin Start(bool readBody)
            => new(fixedBody: null, readBody);

        public async ValueTask DisposeAsync()
        {
            shutdown.Cancel();
            listener.Stop();

            try
            {
                await acceptLoop.ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // Остановка прослушивателя прерывает ожидание.
            }
            catch (OperationCanceledException)
            {
                // Остановка прослушивателя прерывает ожидание.
            }

            shutdown.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!shutdown.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException) when (shutdown.IsCancellationRequested)
                {
                    return;
                }

                _ = Task.Run(() => HandleClientAsync(client), CancellationToken.None);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                try
                {
                    var headerBytes = await ReadHeadersAsync(stream).ConfigureAwait(false);
                    if (headerBytes is null)
                        return;

                    Interlocked.Increment(ref requestCount);

                    if (readBody)
                    {
                        var headers = Encoding.ASCII.GetString(headerBytes);
                        var body = await ReadRequestBodyAsync(stream, headers).ConfigureAwait(false);
                        LastRequestBody = Encoding.UTF8.GetString(body);
                    }

                    var bodyText = fixedBody ?? "atom-origin-echo";
                    var bodyBytes = Encoding.UTF8.GetBytes(bodyText);
                    var responseHead = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n"
                        + "Content-Type: text/plain\r\n"
                        + $"Content-Length: {bodyBytes.Length}\r\n"
                        + "Connection: close\r\n"
                        + "\r\n");

                    await stream.WriteAsync(responseHead).ConfigureAwait(false);
                    await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // Клиент закрыл соединение до конца ответа.
                }
                catch (SocketException)
                {
                    // Клиент закрыл соединение до конца ответа.
                }
            }
        }

        private static async Task<byte[]?> ReadHeadersAsync(System.Net.Sockets.NetworkStream stream)
        {
            using var buffer = new MemoryStream();
            var single = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(single).ConfigureAwait(false);
                if (read <= 0)
                    return buffer.Length == 0 ? null : buffer.ToArray();

                // Не читаем крупным буфером: TCP вправе отдать часть chunked-body в том же
                // ReadAsync, что и заголовки. Эти байты должны остаться в NetworkStream для
                // ReadRequestBodyAsync, иначе loopback-origin ждёт уже потерянный первый chunk.
                buffer.WriteByte(single[0]);
                if (buffer.Length < 4)
                    continue;

                var accumulated = buffer.GetBuffer();
                var length = checked((int)buffer.Length);
                if (accumulated[length - 1] == (byte)'\n'
                    && accumulated[length - 2] == (byte)'\r'
                    && accumulated[length - 3] == (byte)'\n'
                    && accumulated[length - 4] == (byte)'\r')
                {
                    return buffer.ToArray();
                }
            }
        }

        private static async Task<byte[]> ReadRequestBodyAsync(System.Net.Sockets.NetworkStream stream, string headers)
        {
            if (headers.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
                return await ReadChunkedAsync(stream).ConfigureAwait(false);

            var contentLength = ReadContentLength(headers);
            if (contentLength <= 0)
                return [];

            var body = new byte[contentLength];
            var offset = 0;
            while (offset < contentLength)
            {
                var read = await stream.ReadAsync(body.AsMemory(offset)).ConfigureAwait(false);
                if (read <= 0)
                    break;

                offset += read;
            }

            return body;
        }

        private static int ReadContentLength(string headers)
        {
            const string marker = "Content-Length:";
            var markerIndex = headers.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return 0;

            var valueStart = markerIndex + marker.Length;
            var lineEnd = headers.IndexOf("\r\n", valueStart, StringComparison.Ordinal);
            var rawValue = headers[valueStart..(lineEnd < 0 ? headers.Length : lineEnd)].Trim();
            return int.TryParse(rawValue, out var parsed) ? parsed : 0;
        }

        private static async Task<byte[]> ReadChunkedAsync(System.Net.Sockets.NetworkStream stream)
        {
            using var body = new MemoryStream();
            while (true)
            {
                var sizeLine = await ReadAsciiLineAsync(stream).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(sizeLine))
                    break;

                var separatorIndex = sizeLine.IndexOf(';');
                var sizeText = (separatorIndex >= 0 ? sizeLine[..separatorIndex] : sizeLine).Trim();
                if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var chunkSize))
                    break;

                if (chunkSize == 0)
                {
                    // Терминальные trailer-заголовки до пустой строки.
                    while (true)
                    {
                        var trailerLine = await ReadAsciiLineAsync(stream).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(trailerLine))
                            break;
                    }

                    break;
                }

                var chunk = new byte[chunkSize];
                var offset = 0;
                while (offset < chunkSize)
                {
                    var read = await stream.ReadAsync(chunk.AsMemory(offset)).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    offset += read;
                }

                body.Write(chunk.AsSpan(0, offset));
                _ = await ReadAsciiLineAsync(stream).ConfigureAwait(false); // CRLF после чанка
            }

            return body.ToArray();
        }

        private static async Task<string> ReadAsciiLineAsync(System.Net.Sockets.NetworkStream stream)
        {
            using var line = new MemoryStream();
            var single = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(single).ConfigureAwait(false);
                if (read <= 0)
                    break;

                if (single[0] == (byte)'\n')
                    break;

                if (single[0] != (byte)'\r')
                    line.WriteByte(single[0]);
            }

            return Encoding.ASCII.GetString(line.ToArray());
        }
    }
}
