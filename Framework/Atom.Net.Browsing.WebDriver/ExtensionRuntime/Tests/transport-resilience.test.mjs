import test from 'node:test';
import assert from 'node:assert/strict';
import { BackgroundRuntimeHost } from '../Background/BackgroundRuntimeHost.ts';
import { normalizeBootstrapConfig } from '../Background/Bootstrap/BootstrapRuntimeConfigLoader.ts';
import { BridgeSessionCoordinator } from '../Background/Session/BridgeSessionCoordinator.ts';
import { DefaultHandshakeClient } from '../Background/Session/DefaultHandshakeClient.ts';
import { BrowserWebSocketTransportClient } from '../Background/Transport/BrowserWebSocketTransportClient.ts';
import { InMemoryRequestCorrelationStore } from '../Background/Transport/InMemoryRequestCorrelationStore.ts';
import { IntervalKeepAliveController } from '../Background/Transport/IntervalKeepAliveController.ts';

test('IntervalKeepAliveController помечает канал нездоровым ровно после N пропущенных Pong', async (t) => {
    t.mock.timers.enable({ apis: ['setInterval', 'Date'], now: 1000 });

    const unhealthyCalls = [];
    const controller = new IntervalKeepAliveController({
        maxMissedPongCount: 3,
        onUnhealthy: (missedPongCount) => unhealthyCalls.push(missedPongCount),
    });

    controller.start(async () => {
    }, 100);

    try {
        let snapshot = controller.getSnapshot();
        assert.equal(snapshot.missedPongCount, 0);
        assert.equal(snapshot.healthy, true);

        t.mock.timers.tick(100);
        snapshot = controller.getSnapshot();
        assert.equal(snapshot.missedPongCount, 1);
        assert.equal(snapshot.healthy, true);

        t.mock.timers.tick(100);
        snapshot = controller.getSnapshot();
        assert.equal(snapshot.missedPongCount, 2);
        assert.equal(snapshot.healthy, true);

        // Исправленный off-by-one: третий пропущенный Pong сразу делает канал нездоровым,
        // а не только четвёртый (здоровье оценивается по уже увеличенному счётчику).
        t.mock.timers.tick(100);
        snapshot = controller.getSnapshot();
        assert.equal(snapshot.missedPongCount, 3);
        assert.equal(snapshot.healthy, false);
        assert.deepEqual(unhealthyCalls, [3]);

        // Уведомление не дублируется при последующих интервалах.
        t.mock.timers.tick(100);
        assert.deepEqual(unhealthyCalls, [3]);

        // Pong восстанавливает здоровье и разрешает новое уведомление.
        controller.notePong();
        snapshot = controller.getSnapshot();
        assert.equal(snapshot.missedPongCount, 0);
        assert.equal(snapshot.healthy, true);

        t.mock.timers.tick(300);
        assert.equal(controller.getSnapshot().healthy, false);
        assert.deepEqual(unhealthyCalls, [3, 3]);
    } finally {
        controller.stop();
    }
});

test('IntervalKeepAliveController считает ошибку отправки контрольного запроса нездоровым состоянием', async (t) => {
    t.mock.timers.enable({ apis: ['setInterval', 'Date'], now: 1000 });

    const unhealthyCalls = [];
    const controller = new IntervalKeepAliveController({
        onUnhealthy: (missedPongCount) => unhealthyCalls.push(missedPongCount),
    });

    controller.start(async () => {
        throw new Error('канал недоступен');
    }, 50);

    try {
        t.mock.timers.tick(50);
        await flushMicrotasks();

        assert.equal(controller.getSnapshot().healthy, false);
        assert.deepEqual(unhealthyCalls, [1]);
    } finally {
        controller.stop();
    }
});

test('InMemoryRequestCorrelationStore вычищает просроченные запросы через sweepExpired и при register', async (t) => {
    t.mock.timers.enable({ apis: ['Date'], now: 10_000 });

    const store = new InMemoryRequestCorrelationStore();
    store.register({ id: 'req-1', type: 'Request', command: 'GetTitle', tabId: '7', timestamp: 10_000 }, 100);
    assert.equal(store.count(), 1);

    assert.equal(store.sweepExpired(10_050).length, 0);
    assert.equal(store.count(), 1);

    const expired = store.sweepExpired(10_101);
    assert.equal(expired.length, 1);
    assert.equal(expired[0].messageId, 'req-1');
    assert.equal(store.count(), 0);

    // Амортизированная уборка: register вычищает накопленные просроченные записи.
    store.register({ id: 'req-2', type: 'Request', command: 'GetTitle', tabId: '7', timestamp: 10_101 }, 100);
    t.mock.timers.tick(500);
    store.register({ id: 'req-3', type: 'Request', command: 'GetTitle', tabId: '7', timestamp: 10_601 }, 1_000);
    assert.equal(store.count(), 1);
    assert.notEqual(store.get('req-3'), null);
});

test('BrowserWebSocketTransportClient уведомляет подписчиков о закрытии и сбрасывает сокет по error', async () => {
    const webSocketStub = installWebSocketStub();

    try {
        const client = new BrowserWebSocketTransportClient();
        const connectPromise = client.connect({ url: 'ws://127.0.0.1:65535/bridge' });

        const socket = webSocketStub.last();
        socket.readyState = FakeWebSocket.OPEN;
        socket.fire('open', {});
        await connectPromise;
        assert.equal(client.connected, true);

        const closeEvents = [];
        const subscription = client.subscribeClosed((info) => {
            closeEvents.push(info);
        });

        // Ошибка сокета до события close: сокет должен перестать считаться текущим.
        socket.fire('error', {});
        assert.equal(client.connected, false);

        socket.fire('close', { code: 1006, reason: 'network lost', wasClean: false });
        assert.equal(closeEvents.length, 1);
        assert.equal(closeEvents[0].code, 1006);
        assert.equal(closeEvents[0].reason, 'network lost');
        assert.equal(closeEvents[0].wasClean, false);
        assert.equal(closeEvents[0].url, 'ws://127.0.0.1:65535/bridge');

        subscription.dispose();
        socket.fire('close', { code: 1000, reason: '', wasClean: true });
        assert.equal(closeEvents.length, 1);
    } finally {
        webSocketStub.restore();
    }
});

test('BrowserWebSocketTransportClient отклоняет connect при ошибке открытия сокета', async () => {
    const webSocketStub = installWebSocketStub();

    try {
        const client = new BrowserWebSocketTransportClient();
        await assert.rejects(
            async () => {
                const connectPromise = client.connect({ url: 'ws://127.0.0.1:65535/bridge' });
                const socket = webSocketStub.last();
                socket.readyState = FakeWebSocket.CLOSED;
                socket.fire('error', {});
                socket.fire('close', { code: 1006, reason: 'abnormal', wasClean: false });
                await connectPromise;
            },
            /Не удалось открыть мостовой канал/,
        );

        assert.equal(client.connected, false);
    } finally {
        webSocketStub.restore();
    }
});

test('BridgeSessionCoordinator перезапускается из Degraded после обрыва транспорта', async () => {
    const harness = createCoordinatorHarness();
    const config = createRuntimeConfig();

    const firstStart = await harness.coordinator.start(config);
    assert.equal(harness.coordinator.state, 'Ready');
    assert.equal(firstStart.sessionId, config.sessionId);
    assert.equal(harness.keepAliveStartCount, 1);
    assert.equal(harness.transport.connectCalls, 1);

    await harness.coordinator.handleTransportClosed('соединение разорвано');
    assert.equal(harness.coordinator.state, 'Degraded');

    // Повторный запуск из Degraded: раньше автомат состояний отклонял Degraded -> ConfigLoaded.
    const secondStart = await harness.coordinator.start(config);
    assert.equal(harness.coordinator.state, 'Ready');
    assert.equal(secondStart.sessionId, config.sessionId);
    assert.equal(harness.transport.connectCalls, 2);
    assert.equal(harness.keepAliveStartCount, 2);
});

test('BackgroundRuntimeHost маршрутизирует через navigation proxy только main_frame запросы', async () => {
    const { restore, proxyListeners } = installProxyChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.transport.send = async () => {
        };
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            proxyPort: 9443,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);

        await host.tryHandleDirectCommand(createSetTabContextCommand({
            contextId: 'ctx-nav-only',
            tabId: '7',
            proxy: null,
            navigationInterceptionMode: 'proxy',
            navigationProxyRouteToken: 'route-token-1',
        }));
        await host.tryHandleDirectCommand(createSetTabContextCommand({
            contextId: 'ctx-nav-upstream',
            tabId: '8',
            proxy: 'http://user:pass@upstream.example.com:3128',
            navigationInterceptionMode: 'proxy',
            navigationProxyRouteToken: 'route-token-2',
        }));

        assert.equal(proxyListeners.onRequest.length, 1);

        // main_frame вкладки в proxy-режиме идёт через локальный навигационный прокси.
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 7, url: 'https://example.com/', type: 'main_frame' }), {
            type: 'http',
            host: '127.0.0.1',
            port: 9443,
        });
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 8, url: 'https://example.com/', type: 'main_frame' }), {
            type: 'http',
            host: '127.0.0.1',
            port: 9443,
        });

        // Сабресурсы в proxy-режиме больше не падают в прокси-сервер с decision-missing:
        // без upstream proxy — напрямую, с upstream proxy — через него.
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 7, url: 'https://example.com/app.js', type: 'script' }), {
            type: 'direct',
        });
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 8, url: 'https://example.com/logo.png', type: 'image' }), {
            type: 'http',
            host: 'upstream.example.com',
            port: 3128,
        });

        // Вызовы без типа (обратная совместимость) трактуются как навигационные.
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 7, url: 'https://example.com/' }), {
            type: 'http',
            host: '127.0.0.1',
            port: 9443,
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost отвечает route token только на 407 от локального навигационного прокси', async () => {
    const { restore, webRequestListeners } = installProxyChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.transport.send = async () => {
        };
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            proxyPort: 9443,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);

        await host.tryHandleDirectCommand(createSetTabContextCommand({
            contextId: 'ctx-nav-only',
            tabId: '7',
            proxy: null,
            navigationInterceptionMode: 'proxy',
            navigationProxyRouteToken: 'route-token-1',
        }));
        await host.tryHandleDirectCommand(createSetTabContextCommand({
            contextId: 'ctx-nav-upstream',
            tabId: '8',
            proxy: 'http://user:pass@upstream.example.com:3128',
            navigationInterceptionMode: 'proxy',
            navigationProxyRouteToken: 'route-token-2',
        }));

        const authListener = webRequestListeners.authRequired[0];

        // 407 от локального навигационного прокси (proxyInfo указывает на него) → route token.
        assert.deepEqual(authListener({
            tabId: 7,
            url: 'https://example.com/',
            isProxy: true,
            proxyInfo: { type: 'http', host: '127.0.0.1', port: 9443 },
        }), {
            authCredentials: {
                username: 'route-token-1',
                password: '',
            },
        });

        // 407 от upstream прокси → его собственные учётные данные, а не route token.
        assert.deepEqual(authListener({
            tabId: 8,
            url: 'https://example.com/logo.png',
            isProxy: true,
            proxyInfo: { type: 'http', host: 'upstream.example.com', port: 3128 },
        }), {
            authCredentials: {
                username: 'user',
                password: 'pass',
            },
        });

        // 407 от чужого прокси при отсутствии upstream: route token наружу не передаётся.
        assert.deepEqual(authListener({
            tabId: 7,
            url: 'https://example.com/',
            isProxy: true,
            proxyInfo: { type: 'http', host: 'other.example.com', port: 3128 },
        }), {});

        // Обратная совместимость: браузер не передал proxyInfo, upstream нет → route token.
        assert.deepEqual(authListener({
            tabId: 7,
            url: 'https://example.com/',
            isProxy: true,
        }), {
            authCredentials: {
                username: 'route-token-1',
                password: '',
            },
        });

        // Серверный WWW-Authenticate (не прокси) → без учётных данных.
        assert.deepEqual(authListener({
            tabId: 7,
            url: 'https://example.com/',
            isProxy: false,
        }), {});
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost переподключается после аварийного закрытия мостового канала', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 0 });
    const { restore } = installProxyChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.started = true;
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);

        const closedReasons = [];
        const startCalls = [];
        host.coordinator = {
            state: 'Ready',
            async handleTransportClosed(reason) {
                closedReasons.push(reason);
            },
            async start(config) {
                startCalls.push(config.sessionId);
                return { sessionId: config.sessionId, startedAt: 1 };
            },
            async stop() {
            },
        };

        host.handleTransportConnectionClosed({
            url: 'ws://127.0.0.1:9222/',
            code: 1006,
            reason: 'network lost',
            wasClean: false,
        });

        assert.equal(closedReasons.length, 1);
        assert.match(closedReasons[0], /1006/);
        assert.equal(startCalls.length, 0, 'переподключение должно идти с backoff, а не мгновенно');

        t.mock.timers.tick(499);
        assert.equal(startCalls.length, 0);

        t.mock.timers.tick(1);
        await flushMicrotasks();
        assert.equal(startCalls.length, 1);

        // Успешный запуск сбрасывает backoff: следующий обрыв снова ждёт базовую задержку.
        host.handleTransportConnectionClosed({
            url: 'ws://127.0.0.1:9222/',
            code: 1001,
            reason: 'going away',
            wasClean: true,
        });
        t.mock.timers.tick(500);
        await flushMicrotasks();
        assert.equal(startCalls.length, 2);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost не переподключается после штатной остановки', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 0 });
    const { restore } = installProxyChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.started = true;
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);

        const coordinatorCalls = { closed: 0, start: 0 };
        host.coordinator = {
            state: 'Ready',
            async handleTransportClosed() {
                coordinatorCalls.closed += 1;
            },
            async start(config) {
                coordinatorCalls.start += 1;
                return { sessionId: config.sessionId, startedAt: 1 };
            },
            async stop() {
            },
        };

        host.started = false;
        host.handleTransportConnectionClosed({
            url: 'ws://127.0.0.1:9222/',
            code: 1000,
            reason: 'normal shutdown',
            wasClean: true,
        });

        t.mock.timers.tick(60_000);
        await flushMicrotasks();
        assert.equal(coordinatorCalls.closed, 0);
        assert.equal(coordinatorCalls.start, 0);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost при нездоровом keepalive закрывает канал и запускает переподключение', async (t) => {
    t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 0 });
    const { restore } = installProxyChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.started = true;
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);

        const disconnectReasons = [];
        host.transport.disconnect = async (reason) => {
            disconnectReasons.push(reason);
        };

        const coordinatorCalls = { closed: 0, start: 0 };
        host.coordinator = {
            state: 'Ready',
            async handleTransportClosed() {
                coordinatorCalls.closed += 1;
            },
            async start(config) {
                coordinatorCalls.start += 1;
                return { sessionId: config.sessionId, startedAt: 1 };
            },
            async stop() {
            },
        };

        host.handleKeepAliveUnhealthy(3);
        await flushMicrotasks();

        assert.equal(disconnectReasons.length, 1);
        assert.equal(coordinatorCalls.closed, 1);

        t.mock.timers.tick(500);
        await flushMicrotasks();
        assert.equal(coordinatorCalls.start, 1);
    } finally {
        restore();
    }
});

async function flushMicrotasks() {
    for (let index = 0; index < 10; index += 1) {
        await Promise.resolve();
    }
}

function createRuntimeConfig(overrides = {}) {
    return {
        host: '127.0.0.1',
        port: 9222,
        secret: 'top-secret',
        sessionId: 'session-1',
        protocolVersion: 1,
        browserFamily: 'chromium',
        extensionVersion: '0.3.0-test',
        featureFlags: {
            enableKeepAlive: true,
        },
        ...overrides,
    };
}

function createSetTabContextCommand(payload) {
    return {
        id: `ctx_${payload.contextId}`,
        type: 'Request',
        command: 'SetTabContext',
        payload: {
            sessionId: 'session-1',
            connectedAt: 123,
            isReady: true,
            ...payload,
        },
    };
}

function createCoordinatorHarness() {
    let inboundHandler = null;
    const transport = {
        connectCalls: 0,
        get connected() {
            return true;
        },
        async connect() {
            transport.connectCalls += 1;
        },
        async disconnect() {
        },
        async send(message) {
            if (message.type === 'Handshake') {
                queueMicrotask(() => {
                    inboundHandler?.({
                        id: message.id,
                        type: 'Handshake',
                        status: 'Ok',
                        payload: {
                            sessionId: 'session-1',
                            negotiatedProtocolVersion: 1,
                            requestTimeoutMs: 5_000,
                            pingIntervalMs: 1_000,
                            maxMessageSize: 1024 * 1024,
                        },
                        timestamp: Date.now(),
                    });
                });
            }
        },
        subscribe(handler) {
            inboundHandler = handler;
            return { dispose() {
            } };
        },
        subscribeClosed() {
            return { dispose() {
            } };
        },
    };

    const healthStub = {
        reportState() {
        },
        reportTransportConnected() {
        },
        reportTabCount() {
        },
        reportPendingRequestCount() {
        },
        reportInboundMessage() {
        },
        createSnapshot() {
            return null;
        },
    };

    const harness = {
        transport,
        keepAliveStartCount: 0,
        coordinator: null,
    };

    harness.coordinator = new BridgeSessionCoordinator({
        transport,
        handshake: new DefaultHandshakeClient(),
        tabs: {
            count: () => 0,
        },
        commandRouter: {
            route: async () => {
            },
        },
        eventRouter: {
            route: async () => {
            },
        },
        health: healthStub,
        correlation: new InMemoryRequestCorrelationStore(),
        keepAlive: {
            start() {
                harness.keepAliveStartCount += 1;
            },
            stop() {
            },
            notePong() {
            },
            getSnapshot: () => ({ missedPongCount: 0, healthy: true }),
        },
    }, 'session-1');

    return harness;
}

class FakeWebSocket {
    static CONNECTING = 0;
    static OPEN = 1;
    static CLOSING = 2;
    static CLOSED = 3;

    constructor(url) {
        this.url = url;
        this.readyState = FakeWebSocket.CONNECTING;
        this.listeners = new Map();
        FakeWebSocket.instances.push(this);
    }

    addEventListener(type, listener) {
        const listeners = this.listeners.get(type) ?? [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
    }

    fire(type, event) {
        for (const listener of this.listeners.get(type) ?? []) {
            listener(event);
        }
    }

    send() {
    }

    close(code = 1000, reason = '') {
        this.readyState = FakeWebSocket.CLOSED;
        this.fire('close', { code, reason, wasClean: code === 1000 });
    }
}
FakeWebSocket.instances = [];

function installWebSocketStub() {
    const previousWebSocket = globalThis.WebSocket;
    FakeWebSocket.instances = [];
    globalThis.WebSocket = FakeWebSocket;

    return {
        last: () => FakeWebSocket.instances[FakeWebSocket.instances.length - 1],
        restore() {
            if (previousWebSocket === undefined) {
                delete globalThis.WebSocket;
                return;
            }

            globalThis.WebSocket = previousWebSocket;
        },
    };
}

function installProxyChromeStub() {
    const previousChrome = globalThis.chrome;
    const proxyListeners = { onRequest: [] };
    const webRequestListeners = { authRequired: [] };

    globalThis.chrome = {
        runtime: {
            getURL: (path) => `extension://${path}`,
            getManifest: () => ({ version: '0.3.0-test' }),
            onConnect: {
                addListener() {
                },
                removeListener() {
                },
            },
            onMessage: {
                addListener() {
                },
                removeListener() {
                },
            },
        },
        tabs: {
            get: async (tabId) => ({ id: tabId, windowId: 3, url: 'https://example.com/', title: 'Example' }),
            query: async () => [],
            create: async (createProperties) => ({ id: 41, windowId: 3, url: createProperties.url ?? 'about:blank' }),
            update: async (tabId, updateProperties) => ({ id: tabId, url: updateProperties.url ?? 'about:blank' }),
            reload: async () => {
            },
            remove: async () => {
            },
            executeScript: async () => [{ state: 'blocked' }],
        },
        windows: {
            create: async (createData) => ({ id: 12, tabs: [{ id: 77, windowId: 12, url: createData.url ?? 'about:blank' }] }),
            get: async (windowId) => ({ id: windowId, left: 10, top: 20, width: 800, height: 600, state: 'normal' }),
            getAll: async () => [],
            update: async (windowId) => ({ id: windowId }),
            remove: async () => {
            },
        },
        cookies: {
            getAll: async () => [],
            set: async (details) => details,
            remove: async (details) => details,
        },
        webRequest: {
            onBeforeSendHeaders: {
                addListener() {
                },
            },
            onHeadersReceived: {
                addListener() {
                },
            },
            onAuthRequired: {
                addListener(listener) {
                    webRequestListeners.authRequired.push(listener);
                },
            },
        },
        proxy: {
            onRequest: {
                addListener(listener) {
                    proxyListeners.onRequest.push(listener);
                },
            },
        },
        storage: {
            managed: {
                get: async () => ({}),
            },
            local: {
                get: async () => ({}),
            },
        },
        scripting: {
            executeScript: async () => [{ result: { ok: true, value: 'ok' } }],
        },
    };

    return {
        proxyListeners,
        webRequestListeners,
        restore() {
            if (previousChrome === undefined) {
                delete globalThis.chrome;
                return;
            }

            globalThis.chrome = previousChrome;
        },
    };
}
