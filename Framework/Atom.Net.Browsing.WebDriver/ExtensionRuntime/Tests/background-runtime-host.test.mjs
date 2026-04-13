import test from 'node:test';
import assert from 'node:assert/strict';
import { BackgroundRuntimeHost } from '../Background/BackgroundRuntimeHost.ts';
import { handleClientHintsRequestInterception } from '../Background/ClientHintsRequestInterception.ts';
import { loadBootstrapRuntimeConfigWithSource, normalizeBootstrapConfig } from '../Background/Bootstrap/BootstrapRuntimeConfigLoader.ts';

test('normalizeBootstrapConfig достраивает staged config до полного runtime контракта', () => {
    const config = normalizeBootstrapConfig({
        host: '127.0.0.1',
        port: 9222,
        secret: 'top-secret',
    }, {
        getManifest: () => ({ version: '0.3.0-test' }),
    });

    assert.equal(config.host, '127.0.0.1');
    assert.equal(config.port, 9222);
    assert.equal(config.secret, 'top-secret');
    assert.equal(config.protocolVersion, 1);
    assert.equal(config.extensionVersion, '0.3.0-test');
    assert.equal(typeof config.sessionId, 'string');
    assert.ok(config.sessionId.length > 0);
    assert.equal(config.browserFamily, 'chromium');
    assert.equal(config.featureFlags.enableKeepAlive, true);
    assert.equal(config.featureFlags.enableInterception, true);
});

test('normalizeBootstrapConfig сохраняет optional proxyPort в runtime контракте', () => {
    const config = normalizeBootstrapConfig({
        host: '127.0.0.1',
        port: 9222,
        proxyPort: 9443,
        secret: 'top-secret',
    }, {
        getManifest: () => ({ version: '0.3.0-test' }),
    });

    assert.equal(config.port, 9222);
    assert.equal(config.proxyPort, 9443);
});

test('loadBootstrapRuntimeConfig предпочитает managed storage над остальными источниками', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub({
        getManagedStorage: async () => ({
            host: '127.0.0.1',
            port: 7331,
            secret: 'managed-secret',
            sessionId: 'managed-session',
            protocolVersion: 2,
            browserFamily: 'chromium',
            extensionVersion: '9.9.9',
            featureFlags: {
                enableNavigationEvents: true,
                enableCallbackHooks: true,
                enableInterception: true,
                enableDiagnostics: false,
                enableKeepAlive: false,
            },
        }),
    });

    globalThis.fetch = async () => {
        throw new Error('fetch не должен вызываться при наличии managed storage');
    };

    try {
        const { config } = await loadBootstrapRuntimeConfigWithSource(globalThis.chrome.runtime, globalThis.chrome);

        assert.equal(config.host, '127.0.0.1');
        assert.equal(config.port, 7331);
        assert.equal(config.secret, 'managed-secret');
        assert.equal(config.sessionId, 'managed-session');
        assert.equal(config.protocolVersion, 2);
        assert.equal(config.extensionVersion, '9.9.9');
        assert.equal(config.featureFlags.enableDiagnostics, false);
        assert.equal(config.featureFlags.enableKeepAlive, false);
    } finally {
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('loadBootstrapRuntimeConfig использует local storage как fallback перед config.json', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub({
        getManagedStorage: async () => ({}),
        getLocalStorage: async () => ({
            config: {
                host: '127.0.0.1',
                port: 9444,
                secret: 'local-secret',
            },
        }),
    });

    globalThis.fetch = async () => {
        throw new Error('fetch не должен вызываться при наличии local storage fallback');
    };

    try {
        const { config } = await loadBootstrapRuntimeConfigWithSource(globalThis.chrome.runtime, globalThis.chrome);

        assert.equal(config.host, '127.0.0.1');
        assert.equal(config.port, 9444);
        assert.equal(config.secret, 'local-secret');
        assert.equal(config.browserFamily, 'chromium');
        assert.equal(typeof config.sessionId, 'string');
        assert.ok(config.sessionId.length > 0);
    } finally {
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('loadBootstrapRuntimeConfig переживает reject из managed storage и переходит к local storage', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub({
        getManagedStorage: async () => {
            throw new Error('managed storage недоступен');
        },
        getLocalStorage: async () => ({
            config: {
                host: '127.0.0.1',
                port: 9555,
                secret: 'fallback-local-secret',
            },
        }),
    });

    globalThis.fetch = async () => {
        throw new Error('fetch не должен вызываться при доступном local storage fallback');
    };

    try {
        const { config, source } = await loadBootstrapRuntimeConfigWithSource(globalThis.chrome.runtime, globalThis.chrome);

        assert.equal(source, 'local-storage');
        assert.equal(config.port, 9555);
        assert.equal(config.secret, 'fallback-local-secret');
    } finally {
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('loadBootstrapRuntimeConfig переживает reject из storage и переходит к bundled config.json', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub({
        getManagedStorage: async () => {
            throw new Error('managed storage недоступен');
        },
        getLocalStorage: async () => {
            throw new Error('local storage недоступен');
        },
    });

    globalThis.fetch = async () => ({
        ok: true,
        async json() {
            return {
                host: '127.0.0.1',
                port: 9666,
                secret: 'bundled-secret',
            };
        },
    });

    try {
        const { config, source } = await loadBootstrapRuntimeConfigWithSource(globalThis.chrome.runtime, globalThis.chrome);

        assert.equal(source, 'bundled-file');
        assert.equal(config.port, 9666);
        assert.equal(config.secret, 'bundled-secret');
    } finally {
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('loadBootstrapRuntimeConfig читает bundled config.json через fetch.text когда ответ уже строковый', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub({
        getManagedStorage: async () => {
            throw new Error('managed storage недоступен');
        },
        getLocalStorage: async () => {
            throw new Error('local storage недоступен');
        },
    });

    globalThis.fetch = async () => ({
        ok: true,
        async text() {
            return JSON.stringify({
                host: '127.0.0.1',
                port: 9777,
                secret: 'bundled-text-secret',
            });
        },
        async json() {
            throw new Error('json не должен вызываться при доступном text');
        },
    });

    try {
        const { config, source } = await loadBootstrapRuntimeConfigWithSource(globalThis.chrome.runtime, globalThis.chrome);

        assert.equal(source, 'bundled-file');
        assert.equal(config.port, 9777);
        assert.equal(config.secret, 'bundled-text-secret');
    } finally {
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('loadBootstrapRuntimeConfig переключается на XHR fallback если fetch не даёт text/json reader', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub({
        getManagedStorage: async () => {
            throw new Error('managed storage недоступен');
        },
        getLocalStorage: async () => {
            throw new Error('local storage недоступен');
        },
    });
    const xhr = installXmlHttpRequestStub(() => ({
        status: 200,
        body: {
            host: '127.0.0.1',
            port: 9888,
            secret: 'bundled-xhr-secret',
        },
    }));

    globalThis.fetch = async () => ({
        ok: true,
    });

    try {
        const { config, source } = await loadBootstrapRuntimeConfigWithSource(globalThis.chrome.runtime, globalThis.chrome);

        assert.equal(source, 'bundled-file');
        assert.equal(config.port, 9888);
        assert.equal(config.secret, 'bundled-xhr-secret');
        assert.equal(xhr.requests.length, 1);
        assert.equal(xhr.requests[0].method, 'GET');
        assert.match(xhr.requests[0].url, /config\.json$/);
    } finally {
        xhr.restore();
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('BackgroundRuntimeHost публикует debug events для runtime port replace и disconnect path', async () => {
    const previousFetch = globalThis.fetch;
    const { restore, runtimeListeners } = installChromeStub({
        getManagedStorage: async () => ({
            host: '127.0.0.1',
            port: 7331,
            secret: 'diagnostics-secret',
            sessionId: 'session-1',
            protocolVersion: 2,
            browserFamily: 'chromium',
            extensionVersion: '9.9.9',
            featureFlags: {
                enableNavigationEvents: true,
                enableCallbackHooks: true,
                enableInterception: true,
                enableDiagnostics: true,
                enableKeepAlive: false,
            },
        }),
        getTab: async (tabId) => ({ id: tabId, windowId: 3, url: 'https://example.com/', title: 'Example' }),
    });
    const debugEvents = [];

    globalThis.fetch = async (url, options) => {
        if (String(url).includes('/debug-event?')) {
            debugEvents.push(JSON.parse(String(options?.body ?? 'null')));
        }

        return {
            ok: true,
            async text() {
                return '';
            },
            async json() {
                return {};
            },
        };
    };

    try {
        const host = new BackgroundRuntimeHost();
        host.createCoordinator = () => ({
            state: 'Ready',
            async start() {
                return { started: true };
            },
            async stop() {
            },
        });
        host.transport.send = async () => {
        };

        await host.start();
        assert.equal(runtimeListeners.onConnect.length, 1);

        const initialPort = createRuntimePortStub(7, 3);
        runtimeListeners.onConnect[0](initialPort.port);
        await new Promise((resolve) => setTimeout(resolve, 0));

        const replacementPort = createRuntimePortStub(7, 3);
        runtimeListeners.onConnect[0](replacementPort.port);
        await new Promise((resolve) => setTimeout(resolve, 0));

        replacementPort.simulateDisconnect();
        await new Promise((resolve) => setTimeout(resolve, 0));

        const runtimePortEvents = debugEvents.filter((event) => String(event?.kind).startsWith('runtime-port-'));
        assert.deepEqual(runtimePortEvents.map((event) => event.kind), [
            'runtime-port-connected',
            'runtime-port-replacement-start',
            'runtime-port-disconnect-ignored',
            'runtime-port-connected',
            'runtime-port-disconnected',
        ]);

        assert.deepEqual(runtimePortEvents[0].details, {
            tabId: '7',
            windowId: '3',
            replaced: false,
            connectedTabCount: 1,
            contextId: runtimePortEvents[0].details.contextId,
            url: 'https://example.com/',
        });
        assert.equal(typeof runtimePortEvents[0].details.contextId, 'string');
        assert.ok(runtimePortEvents[0].details.contextId.length > 0);
        assert.deepEqual(runtimePortEvents[1].details, {
            tabId: '7',
            windowId: '3',
            previousWindowId: '3',
            previousConnected: true,
            hadPreviousContext: true,
        });
        assert.deepEqual(runtimePortEvents[2].details, {
            tabId: '7',
            windowId: '3',
            reason: 'superseded-by-new-connection',
            hasCurrentRuntime: false,
        });
        assert.equal(runtimePortEvents[3].details.replaced, true);
        assert.equal(runtimePortEvents[3].details.connectedTabCount, 1);
        assert.equal(runtimePortEvents[3].details.url, 'https://example.com/');
        assert.deepEqual(runtimePortEvents[4].details, {
            tabId: '7',
            windowId: '3',
            connectedTabCount: 0,
        });
        assert.equal(initialPort.disconnectCallCount, 1);
        assert.equal(replacementPort.disconnectCallCount, 1);
    } finally {
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('BackgroundRuntimeHost публикует debug event если runtime port applyContext падает после connect', async () => {
    const previousFetch = globalThis.fetch;
    const previousConsoleError = console.error;
    const { restore, runtimeListeners } = installChromeStub({
        getManagedStorage: async () => ({
            host: '127.0.0.1',
            port: 7331,
            secret: 'diagnostics-secret',
            sessionId: 'session-1',
            protocolVersion: 2,
            browserFamily: 'chromium',
            extensionVersion: '9.9.9',
            featureFlags: {
                enableNavigationEvents: true,
                enableCallbackHooks: true,
                enableInterception: true,
                enableDiagnostics: true,
                enableKeepAlive: false,
            },
        }),
        getTab: async (tabId) => ({ id: tabId, windowId: 3, url: 'https://example.com/', title: 'Example' }),
    });
    const debugEvents = [];

    globalThis.fetch = async (url, options) => {
        if (String(url).includes('/debug-event?')) {
            debugEvents.push(JSON.parse(String(options?.body ?? 'null')));
        }

        return {
            ok: true,
            async text() {
                return '';
            },
            async json() {
                return {};
            },
        };
    };
    console.error = () => {
    };

    try {
        const host = new BackgroundRuntimeHost();
        host.createCoordinator = () => ({
            state: 'Ready',
            async start() {
                return { started: true };
            },
            async stop() {
            },
        });
        host.transport.send = async () => {
        };

        await host.start();
        assert.equal(runtimeListeners.onConnect.length, 1);

        const failingPort = createRuntimePortStub(7, 3, {
            postMessage(message) {
                if (message?.command === 'ApplyContext') {
                    throw new Error('apply-context failed');
                }
            },
        });

        runtimeListeners.onConnect[0](failingPort.port);
        await new Promise((resolve) => setTimeout(resolve, 0));

        const runtimePortEvents = debugEvents.filter((event) => String(event?.kind).startsWith('runtime-port-'));
        assert.deepEqual(runtimePortEvents.map((event) => event.kind), [
            'runtime-port-connected',
            'runtime-port-apply-context-failed',
        ]);
        assert.deepEqual(runtimePortEvents[1].details, {
            tabId: '7',
            windowId: '3',
            replaced: false,
            contextId: runtimePortEvents[1].details.contextId,
            url: 'https://example.com/',
            error: 'apply-context failed',
        });
        assert.equal(typeof runtimePortEvents[1].details.contextId, 'string');
        assert.ok(runtimePortEvents[1].details.contextId.length > 0);
        assert.equal(failingPort.disconnectCallCount, 0);
    } finally {
        restore();
        globalThis.fetch = previousFetch;
        console.error = previousConsoleError;
    }
});

test('BackgroundRuntimeHost направляет Navigate напрямую через tabs.update', async () => {
    const { restore, tabsCalls, windowCalls } = installChromeStub({
        update: async (tabId, updateProperties) => ({
            id: tabId,
            windowId: 9,
            url: updateProperties.url,
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const handled = await host.tryHandleDirectCommand({
            id: 'nav_1',
            type: 'Request',
            tabId: '7',
            command: 'Navigate',
            payload: { url: 'https://example.com/' },
        });

        assert.equal(handled, true);
        assert.equal(tabsCalls.update.length, 2);
        assert.equal(tabsCalls.update[0].tabId, 7);
        assert.deepEqual(tabsCalls.update[0].updateProperties, { url: 'https://example.com/' });
        assert.equal(windowCalls.update.length, 1);
        assert.deepEqual(windowCalls.update[0], {
            windowId: 3,
            updateInfo: { focused: true },
        });
        assert.equal(tabsCalls.update[1].tabId, 7);
        assert.deepEqual(tabsCalls.update[1].updateProperties, { url: 'https://example.com/', active: true });
        assert.equal(forwarded[0].status, 'Ok');
        assert.deepEqual(forwarded[0].payload, { tabId: '7', url: 'https://example.com/' });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost запускает Navigate с active tab для device emulation context', async () => {
    const { restore, tabsCalls, windowCalls } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://bootstrap.example/',
            status: 'complete',
        }),
        update: async (tabId, updateProperties) => ({
            id: tabId,
            windowId: 9,
            url: updateProperties.url,
            pendingUrl: updateProperties.url,
            status: 'loading',
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        host.tabContexts.set('7', {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: true,
            url: 'https://bootstrap.example/',
            windowId: '9',
            userAgent: 'Mobile UA',
            isMobile: true,
            hasTouch: true,
            maxTouchPoints: 5,
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'nav_device_ctx_1',
            type: 'Request',
            tabId: '7',
            command: 'Navigate',
            payload: { url: 'https://example.com/' },
        });

        assert.equal(handled, true);
        assert.equal(tabsCalls.update.length, 1);
        assert.equal(tabsCalls.update[0].tabId, 7);
        assert.equal(windowCalls.update.length, 1);
        assert.deepEqual(windowCalls.update[0], {
            windowId: 9,
            updateInfo: { focused: true },
        });
        assert.deepEqual(tabsCalls.update[0].updateProperties, { url: 'https://example.com/', active: true });
        assert.equal(forwarded[0].status, 'Ok');
        assert.deepEqual(forwarded[0].payload, { tabId: '7', url: 'https://example.com/' });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost обновляет tracked tab context url при Navigate', async () => {
    const { restore } = installChromeStub({
        update: async (tabId, updateProperties) => ({
            id: tabId,
            windowId: 9,
            url: updateProperties.url,
        }),
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://example.com/next',
            title: 'Example',
            status: 'loading',
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: true,
            url: 'https://initial.example/',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register({
            tabId: '7',
            windowId: '3',
            connected: true,
            async send() {
            },
            async applyContext() {
            },
            async disconnect() {
            },
        }, context);

        const handled = await host.tryHandleDirectCommand({
            id: 'nav_ctx_1',
            type: 'Request',
            tabId: '7',
            command: 'Navigate',
            payload: { url: 'https://example.com/next' },
        });

        assert.equal(handled, true);
        assert.equal(host.tabContexts.get('7')?.url, 'https://example.com/next');
        assert.equal(host.tabContexts.get('7')?.windowId, '9');
        assert.equal(host.tabContexts.get('7')?.isReady, false);
        assert.notEqual(host.tabs.get('7'), null);
        assert.equal(forwarded[0].status, 'Ok');
        assert.equal(forwarded.length, 1);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost сохраняет текущий runtime при Navigate и публикует not-ready статус', async () => {
    const { restore } = installChromeStub({
        update: async (tabId, updateProperties) => ({
            id: tabId,
            windowId: 9,
            url: updateProperties.url,
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: true,
            url: 'https://initial.example/',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register({
            tabId: '7',
            windowId: '3',
            connected: true,
            async send() {
            },
            async applyContext() {
            },
            async disconnect() {
            },
        }, context);

        const handled = await host.tryHandleDirectCommand({
            id: 'nav_ctx_2',
            type: 'Request',
            tabId: '7',
            command: 'Navigate',
            payload: { url: 'https://example.com/next' },
        });

        assert.equal(handled, true);
        assert.notEqual(host.tabs.get('7'), null);
        assert.equal(forwarded[0].status, 'Ok');

        const statusHandled = await host.tryHandleDirectCommand({
            id: 'debug_status_1',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(statusHandled, true);
        assert.equal(forwarded[1].payload.tabId, 7);
        assert.equal(forwarded[1].payload.hasPort, false);
        assert.equal(forwarded[1].payload.queueLength, 0);
        assert.equal(forwarded[1].payload.hasSocket, false);
        assert.equal(forwarded[1].payload.isReady, false);
        assert.equal(forwarded[1].payload.interceptEnabled, false);
        assert.equal(forwarded[1].payload.hasTabContext, true);
        assert.equal(forwarded[1].payload.hasBrowserTab, true);
        assert.equal(forwarded[1].payload.contextId, 'ctx-1');
        assert.equal(forwarded[1].payload.browserTabUrl, 'https://example.com/');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost поднимает ready в DebugPortStatus когда browser tab уже complete', async () => {
    const { restore } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://example.com/next',
            title: 'Example',
            status: 'complete',
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        const appliedContexts = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: false,
            url: 'https://example.com/next',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register({
            ...createConnectedRuntimeEndpoint('7'),
            async applyContext(nextContext) {
                appliedContexts.push(nextContext);
            },
        }, context);

        const handled = await host.tryHandleDirectCommand({
            id: 'debug_status_complete_1',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(handled, true);
        assert.equal(host.tabContexts.get('7')?.isReady, true);
        assert.equal(typeof host.tabContexts.get('7')?.readyAt, 'number');
        assert.equal(host.tabContexts.get('7')?.windowId, '9');
        assert.equal(host.tabContexts.get('7')?.url, 'https://example.com/next');
        assert.equal(appliedContexts.length, 0);
        assert.equal(forwarded[0].payload.tabId, 7);
        assert.equal(forwarded[0].payload.hasPort, true);
        assert.equal(forwarded[0].payload.queueLength, 0);
        assert.equal(forwarded[0].payload.hasSocket, false);
        assert.equal(forwarded[0].payload.isReady, true);
        assert.equal(forwarded[0].payload.interceptEnabled, false);
        assert.equal(forwarded[0].payload.hasTabContext, true);
        assert.equal(forwarded[0].payload.hasBrowserTab, true);
        assert.equal(forwarded[0].payload.contextId, 'ctx-1');
        assert.equal(forwarded[0].payload.browserTabUrl, 'https://example.com/next');
        assert.equal(forwarded[0].payload.browserTabStatus, 'complete');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost открывает порт pending navigation когда runtime уже на expected url', async () => {
    const { restore } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://initial.example/',
            pendingUrl: 'https://example.com/next',
            title: 'Example',
            status: 'loading',
        }),
    });

    try {
        globalThis.chrome.scripting.executeScript = async () => ([{
            result: {
                ok: true,
                value: JSON.stringify({
                    href: 'https://example.com/next',
                    readyState: 'interactive',
                }),
            },
        }]);

        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: false,
            url: 'https://example.com/next',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register(createConnectedRuntimeEndpoint('7'), context);
        host.pendingNavigations.set('7', {
            command: 'Navigate',
            expectedUrl: 'https://example.com/next',
            previousUrl: 'https://initial.example/',
            previousEndpoint: {},
            startedAt: Date.now() - 400,
            retryCount: 0,
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'debug_status_pending_ready_1',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(handled, true);
        assert.equal(forwarded[0].payload.tabId, 7);
        assert.equal(forwarded[0].payload.hasPort, true);
        assert.equal(forwarded[0].payload.hasSocket, false);
        assert.equal(forwarded[0].payload.isReady, false);
        assert.equal(forwarded[0].payload.hasBrowserTab, true);
        assert.equal(forwarded[0].payload.browserTabUrl, 'https://initial.example/');
        assert.equal(forwarded[0].payload.browserTabPendingUrl, 'https://example.com/next');
        assert.equal(forwarded[0].payload.browserTabStatus, 'loading');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost даёт Navigate grace period до runtime inspection на pending target', async () => {
    const { restore } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://initial.example/',
            pendingUrl: 'https://example.com/next',
            title: 'Example',
            status: 'loading',
        }),
    });

    try {
        let executeScriptCalls = 0;
        globalThis.chrome.scripting.executeScript = async () => {
            executeScriptCalls++;
            return ([{
                result: {
                    ok: true,
                    value: JSON.stringify({
                        href: 'https://example.com/next',
                        readyState: 'interactive',
                    }),
                },
            }]);
        };

        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: false,
            url: 'https://example.com/next',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register(createConnectedRuntimeEndpoint('7'), context);
        host.pendingNavigations.set('7', {
            command: 'Navigate',
            expectedUrl: 'https://example.com/next',
            previousUrl: 'https://initial.example/',
            previousEndpoint: {},
            startedAt: Date.now() - 50,
            retryCount: 0,
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'debug_status_pending_grace_1',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(handled, true);
        assert.equal(executeScriptCalls, 0);
        assert.equal(forwarded[0].payload.hasPort, false);
        assert.equal(forwarded[0].payload.hasSocket, false);
        assert.equal(forwarded[0].payload.browserTabUrl, 'https://initial.example/');
        assert.equal(forwarded[0].payload.browserTabPendingUrl, 'https://example.com/next');
        assert.equal(forwarded[0].payload.runtimeCheckStatus, 'navigate-in-flight-grace');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost не снимает pending Navigate только по ready context на target без runtime rebootstrap', async () => {
    const { restore } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://initial.example/',
            pendingUrl: 'https://example.com/next',
            title: 'Example',
            status: 'loading',
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: true,
            readyAt: 123,
            url: 'https://example.com/next',
            windowId: '3',
            geolocation: {
                latitude: 55.7558,
                longitude: 37.6176,
                accuracy: 15,
            },
        };

        host.tabContexts.set('7', context);
        const runtimeEndpoint = createConnectedRuntimeEndpoint('7');
        host.tabs.register(runtimeEndpoint, context);
        host.pendingNavigations.set('7', {
            command: 'Navigate',
            expectedUrl: 'https://example.com/next',
            previousUrl: 'https://initial.example/',
            previousEndpoint: runtimeEndpoint,
            startedAt: Date.now() - 50,
            retryCount: 0,
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'debug_status_pending_ready_without_rebootstrap_1',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(handled, true);
        assert.equal(host.pendingNavigations.has('7'), true);
        assert.equal(forwarded[0].payload.hasPort, false);
        assert.equal(forwarded[0].payload.hasSocket, false);
        assert.equal(forwarded[0].payload.browserTabUrl, 'https://initial.example/');
        assert.equal(forwarded[0].payload.browserTabPendingUrl, 'https://example.com/next');
        assert.equal(forwarded[0].payload.runtimeCheckStatus, 'navigate-in-flight-grace');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost делает один late reissue Navigate при подтверждённом stale runtime на pending target', async () => {
    const { restore, tabsCalls, windowCalls, scriptingCalls } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://initial.example/',
            pendingUrl: 'https://example.com/next',
            title: 'Example',
            status: 'loading',
        }),
        update: async (tabId, updateProperties) => ({
            id: tabId,
            windowId: 9,
            url: updateProperties.url,
            pendingUrl: updateProperties.url,
            title: 'Example',
            status: 'loading',
        }),
    });

    try {
        globalThis.chrome.scripting.executeScript = async (details) => {
            scriptingCalls.executeScript.push(details);
            const script = details?.args?.[0];
            if (typeof script === 'string' && script.includes('navigate-requested')) {
                return [{
                    result: {
                        ok: true,
                        value: 'navigate-requested',
                    },
                }];
            }

            return [{
                result: {
                    ok: true,
                    value: JSON.stringify({
                        href: 'https://initial.example/',
                        readyState: 'complete',
                    }),
                },
            }];
        };

        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        const retryStartedAt = Date.now();
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: false,
            url: 'https://example.com/next',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register(createConnectedRuntimeEndpoint('7'), context);
        host.pendingNavigations.set('7', {
            command: 'Navigate',
            expectedUrl: 'https://example.com/next',
            previousUrl: 'https://initial.example/',
            previousEndpoint: {},
            startedAt: Date.now() - 800,
            retryCount: 0,
            runtimeConfirmationAttemptedAt: Date.now() - 400,
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'debug_status_pending_retry_1',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(handled, true);
        assert.equal(windowCalls.update.length, 1);
        assert.deepEqual(windowCalls.update[0], {
            windowId: 9,
            updateInfo: { focused: true },
        });
        assert.equal(tabsCalls.update.length, 1);
        assert.equal(tabsCalls.update[0].tabId, 7);
        assert.deepEqual(tabsCalls.update[0].updateProperties, { url: 'https://example.com/next', active: true });
        assert.equal(scriptingCalls.executeScript.length, 3);
        assert.equal(scriptingCalls.executeScript[0].target.tabId, 7);
        assert.equal(scriptingCalls.executeScript[1].target.tabId, 7);
        assert.equal(scriptingCalls.executeScript[2].target.tabId, 7);
        assert.equal(
            scriptingCalls.executeScript.some((call) => /location\.replace\("https:\/\/example\.com\/next"\)/.test(call.args[0])),
            true,
        );
        assert.equal(host.pendingNavigations.get('7')?.retryCount, 1);
        assert.ok(host.pendingNavigations.get('7')?.lastRetryAt >= retryStartedAt);
        assert.equal(forwarded[0].payload.hasPort, false);
        assert.equal(forwarded[0].payload.hasBrowserTab, true);
        assert.equal(forwarded[0].payload.browserTabUrl, 'https://example.com/next');
        assert.equal(forwarded[0].payload.browserTabPendingUrl, 'https://example.com/next');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost не повторяет late reissue Navigate после достижения лимита stale retry', async () => {
    const { restore, tabsCalls, windowCalls } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://initial.example/',
            pendingUrl: 'https://example.com/next',
            title: 'Example',
            status: 'loading',
        }),
        update: async (tabId, updateProperties) => ({
            id: tabId,
            windowId: 9,
            url: updateProperties.url,
            pendingUrl: updateProperties.url,
            title: 'Example',
            status: 'loading',
        }),
    });

    try {
        globalThis.chrome.scripting.executeScript = async () => ([{
            result: {
                ok: true,
                value: JSON.stringify({
                    href: 'https://initial.example/',
                    readyState: 'complete',
                }),
            },
        }]);

        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        const lastRetryAt = Date.now() - 2000;
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: false,
            url: 'https://example.com/next',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register(createConnectedRuntimeEndpoint('7'), context);
        host.pendingNavigations.set('7', {
            command: 'Navigate',
            expectedUrl: 'https://example.com/next',
            previousUrl: 'https://initial.example/',
            previousEndpoint: {},
            startedAt: Date.now() - 4000,
            retryCount: 4,
            lastRetryAt,
            runtimeConfirmationAttemptedAt: Date.now() - 400,
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'debug_status_pending_retry_throttled_1',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(handled, true);
        assert.equal(windowCalls.update.length, 0);
        assert.equal(tabsCalls.update.length, 0);
        assert.equal(host.pendingNavigations.get('7')?.retryCount, 4);
        assert.equal(host.pendingNavigations.get('7')?.lastRetryAt, lastRetryAt);
        assert.equal(forwarded[0].payload.hasPort, false);
        assert.equal(forwarded[0].payload.hasBrowserTab, true);
        assert.equal(forwarded[0].payload.browserTabUrl, 'https://initial.example/');
        assert.equal(forwarded[0].payload.browserTabPendingUrl, 'https://example.com/next');
        assert.equal(forwarded[0].payload.runtimeCheckStatus, 'url-mismatch');
        assert.equal(forwarded[0].payload.runtimeHref, 'https://initial.example/');
        assert.equal(forwarded[0].payload.runtimeReadyState, 'complete');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost сохраняет extended context в DebugPortStatus когда browser tab уже complete', async () => {
    const { restore } = installChromeStub({
        getTab: async (tabId) => ({
            id: tabId,
            windowId: 9,
            url: 'https://example.com/next',
            title: 'Example',
            status: 'complete',
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        const appliedContexts = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            isReady: false,
            url: 'https://example.com/next',
            windowId: '3',
            userAgent: 'Mobile UA',
            isMobile: true,
            hasTouch: true,
            maxTouchPoints: 5,
        };

        host.tabContexts.set('7', context);
        host.tabs.register({
            ...createConnectedRuntimeEndpoint('7'),
            async applyContext(nextContext) {
                appliedContexts.push(nextContext);
            },
        }, context);

        const handled = await host.tryHandleDirectCommand({
            id: 'debug_status_complete_2',
            type: 'Request',
            tabId: '7',
            command: 'DebugPortStatus',
        });

        assert.equal(handled, true);
        assert.equal(host.tabContexts.get('7')?.isReady, true);
        assert.equal(host.tabContexts.get('7')?.windowId, '9');
        assert.equal(host.tabContexts.get('7')?.url, 'https://example.com/next');
        assert.equal(host.tabContexts.get('7')?.userAgent, 'Mobile UA');
        assert.equal(host.tabContexts.get('7')?.isMobile, true);
        assert.equal(host.tabContexts.get('7')?.hasTouch, true);
        assert.equal(host.tabContexts.get('7')?.maxTouchPoints, 5);
        assert.equal(appliedContexts.length, 0);
        assert.equal(forwarded[0].payload.isReady, true);
        assert.equal(forwarded[0].payload.hasBrowserTab, true);
        assert.equal(forwarded[0].payload.browserTabUrl, 'https://example.com/next');
        assert.equal(forwarded[0].payload.browserTabStatus, 'complete');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost сериализует ошибку Navigate в direct response', async () => {
    const { restore } = installChromeStub({
        update: async () => {
            throw new Error('navigate failed');
        },
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const handled = await host.tryHandleDirectCommand({
            id: 'nav_timeout_1',
            type: 'Request',
            tabId: '7',
            command: 'Navigate',
            payload: { url: 'https://example.com/timeout' },
        });

        assert.equal(handled, true);
        assert.equal(forwarded[0].status, 'Error');
        assert.equal(forwarded[0].error, 'navigate failed');
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost направляет Reload напрямую через tabs.reload', async () => {
    const { restore, tabsCalls } = installChromeStub({
        reload: async () => {
        },
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            connectedAt: 123,
            readyAt: 456,
            isReady: true,
            url: 'https://example.com/current',
            windowId: '3',
        };

        host.tabContexts.set('7', context);
        host.tabs.register({
            tabId: '7',
            windowId: '3',
            connected: true,
            async send() {
            },
            async applyContext() {
            },
            async disconnect() {
            },
        }, context);

        const handled = await host.tryHandleDirectCommand({
            id: 'reload_1',
            type: 'Request',
            tabId: '7',
            command: 'Reload',
        });

        assert.equal(handled, true);
        assert.equal(tabsCalls.reload.length, 1);
        assert.equal(tabsCalls.reload[0].tabId, 7);
        assert.equal(host.tabContexts.get('7')?.isReady, false);
        assert.equal(host.tabContexts.get('7')?.readyAt, undefined);
        assert.notEqual(host.tabs.get('7'), null);
        assert.equal(forwarded[0].status, 'Ok');
        assert.equal(forwarded.length, 1);
        assert.deepEqual(forwarded[0].payload, { tabId: '7' });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost применяет SetTabContext к уже подключённой вкладке', async () => {
    const { restore } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        const appliedContexts = [];
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        host.tabs.register({
            tabId: '7',
            connected: true,
            async send() {
            },
            async applyContext(context) {
                appliedContexts.push(context);
            },
            async disconnect() {
            },
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'ctx_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-1',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
            },
        });

        assert.equal(handled, true);
        assert.equal(appliedContexts.length, 1);
        assert.equal(appliedContexts[0].contextId, 'ctx-1');
        assert.deepEqual(forwarded[0].payload, { tabId: '7', isReady: true });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost публикует debug event и Error response если SetTabContext applyContext падает', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub();
    const debugEvents = [];

    globalThis.fetch = async (url, options) => {
        if (String(url).includes('/debug-event?')) {
            debugEvents.push(JSON.parse(String(options?.body ?? 'null')));
        }

        return {
            ok: true,
            async text() {
                return '';
            },
            async json() {
                return {};
            },
        };
    };

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 7331,
            secret: 'diagnostics-secret',
            featureFlags: {
                enableDiagnostics: true,
            },
        }, globalThis.chrome.runtime);

        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        host.tabs.register({
            tabId: '7',
            connected: true,
            async send() {
            },
            async applyContext() {
                throw new Error('set-tab-context apply failed');
            },
            async disconnect() {
            },
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'ctx_apply_fail_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-1',
                tabId: '7',
                windowId: '3',
                connectedAt: 123,
                isReady: true,
                url: 'https://example.com/',
            },
        });

        assert.equal(handled, true);
        assert.equal(forwarded[0].status, 'Error');
        assert.equal(forwarded[0].error, 'set-tab-context apply failed');

        const applyFailedEvent = debugEvents.find((event) => event.kind === 'set-tab-context-apply-failed');
        assert.deepEqual(applyFailedEvent?.details, {
            tabId: '7',
            windowId: '3',
            contextId: 'ctx-1',
            url: 'https://example.com/',
            isReady: true,
            pendingNavigation: false,
            error: 'set-tab-context apply failed',
        });
    } finally {
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('BackgroundRuntimeHost маршрутизирует per-tab proxy и proxy auth через tab context', async () => {
    const { restore, proxyListeners, webRequestListeners } = installChromeStub({}, {
        includeProxyApi: true,
        includeProxyAuthRequired: true,
    });

    try {
        const host = new BackgroundRuntimeHost();
        host.transport.send = async () => {
        };

        const handled = await host.tryHandleDirectCommand({
            id: 'ctx_proxy_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-proxy',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
                proxy: 'socks5://user:pass@127.0.0.1:1080',
            },
        });

        await host.tryHandleDirectCommand({
            id: 'ctx_proxy_2',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-direct',
                tabId: '8',
                connectedAt: 124,
                isReady: true,
                proxy: null,
            },
        });

        assert.equal(handled, true);
        assert.equal(proxyListeners.onRequest.length, 1);
        assert.equal(webRequestListeners.authRequired.length, 1);
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 7, url: 'https://example.com/' }), {
            type: 'socks',
            host: '127.0.0.1',
            port: 1080,
            proxyDNS: true,
        });
        assert.deepEqual(webRequestListeners.authRequired[0]({ tabId: 7, url: 'https://example.com/', isProxy: true }), {
            authCredentials: {
                username: 'user',
                password: 'pass',
            },
        });
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 8, url: 'https://example.com/' }), {
            type: 'direct',
        });
        assert.equal(proxyListeners.onRequest[0]({ tabId: 99, url: 'https://example.com/' }), undefined);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost использует dedicated proxyPort для navigation proxy mode', async () => {
    const { restore, proxyListeners, webRequestListeners } = installChromeStub({}, {
        includeProxyApi: true,
        includeProxyAuthRequired: true,
    });

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            proxyPort: 9443,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.transport.send = async () => {
        };

        await host.tryHandleDirectCommand({
            id: 'ctx_proxy_navigation_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-proxy-navigation',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
                proxy: null,
                navigationInterceptionMode: 'proxy',
                navigationProxyRouteToken: 'route-token-1',
            },
        });

        assert.equal(proxyListeners.onRequest.length, 1);
        assert.equal(webRequestListeners.authRequired.length, 1);
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 7, url: 'https://example.com/' }), {
            type: 'http',
            host: '127.0.0.1',
            port: 9443,
        });
        assert.deepEqual(webRequestListeners.authRequired[0]({ tabId: 7, url: 'https://example.com/', isProxy: true }), {
            authCredentials: {
                username: 'route-token-1',
                password: '',
            },
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost возвращается к основному port если proxyPort для navigation proxy mode не задан', async () => {
    const { restore, proxyListeners, webRequestListeners } = installChromeStub({}, {
        includeProxyApi: true,
        includeProxyAuthRequired: true,
    });

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.transport.send = async () => {
        };

        await host.tryHandleDirectCommand({
            id: 'ctx_proxy_navigation_fallback_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-proxy-navigation-fallback',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
                proxy: null,
                navigationInterceptionMode: 'proxy',
                navigationProxyRouteToken: 'route-token-1',
            },
        });

        assert.equal(proxyListeners.onRequest.length, 1);
        assert.equal(webRequestListeners.authRequired.length, 1);
        assert.deepEqual(proxyListeners.onRequest[0]({ tabId: 7, url: 'https://example.com/' }), {
            type: 'http',
            host: '127.0.0.1',
            port: 9222,
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost использует tabs.executeScript как legacy fallback при отсутствии scripting', async () => {
    const { restore, tabsCalls } = installChromeStub({
        executeScript: async () => ([{ state: 'ready', result: JSON.stringify({ ok: true, value: '4' }) }]),
    }, { includeScripting: false });

    try {
        const host = new BackgroundRuntimeHost();
        const result = await host.executeInMainWorld('7', 'req_1', '2 + 2');

        assert.equal(result.status, 'ok');
        assert.equal(result.value, '4');
        assert.equal(tabsCalls.executeScript.length, 1);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost открывает окно напрямую через windows.create', async () => {
    const { restore, windowCalls } = installChromeStub({
        createWindow: async (createData) => ({
            id: 12,
            tabs: [{ id: 77, windowId: 12, url: createData.url }],
        }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const handled = await host.tryHandleDirectCommand({
            id: 'window_open_1',
            type: 'Request',
            command: 'OpenWindow',
            payload: {
                url: 'https://example.com/window',
                windowPosition: { x: 640, y: 120 },
            },
        });

        assert.equal(handled, true);
        assert.equal(windowCalls.create.length, 1);
        assert.deepEqual(windowCalls.create[0], {
            url: 'https://example.com/window',
            focused: true,
            left: 640,
            top: 120,
        });
        assert.deepEqual(forwarded[0].payload, {
            windowId: '12',
            tabId: '77',
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost возвращает границы окна напрямую через windows.get', async () => {
    const { restore, windowCalls } = installChromeStub({
        get: async () => ({ id: 3, left: 10, top: 20, width: 1280, height: 720, state: 'normal' }),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const handled = await host.tryHandleDirectCommand({
            id: 'window_bounds_1',
            type: 'Request',
            tabId: '7',
            command: 'GetWindowBounds',
            payload: { tabId: '7' },
        });

        assert.equal(handled, true);
        assert.equal(windowCalls.get.length, 1);
        assert.deepEqual(forwarded[0].payload, {
            windowId: '3',
            left: 10,
            top: 20,
            width: 1280,
            height: 720,
            state: 'normal',
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost изолирует cookies по tab context без обращения к browser.cookies', async () => {
    const { restore, cookieCalls } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        await host.tryHandleDirectCommand({
            id: 'ctx_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-1',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
            },
        });

        await host.tryHandleDirectCommand({
            id: 'ctx_2',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-2',
                tabId: '8',
                connectedAt: 124,
                isReady: true,
            },
        });

        await host.tryHandleDirectCommand({
            id: 'cookie_set_1',
            type: 'Request',
            tabId: '7',
            command: 'SetCookie',
            payload: {
                name: 'session',
                value: 'alpha',
                domain: '.example.com',
                path: '/',
            },
        });

        await host.tryHandleDirectCommand({
            id: 'cookie_set_2',
            type: 'Request',
            tabId: '8',
            command: 'SetCookie',
            payload: {
                name: 'session',
                value: 'beta',
                domain: '.example.com',
                path: '/',
            },
        });

        const getFirstHandled = await host.tryHandleDirectCommand({
            id: 'cookie_get_1',
            type: 'Request',
            tabId: '7',
            command: 'GetCookies',
        });

        const getSecondHandled = await host.tryHandleDirectCommand({
            id: 'cookie_get_2',
            type: 'Request',
            tabId: '8',
            command: 'GetCookies',
        });

        assert.equal(getFirstHandled, true);
        assert.equal(getSecondHandled, true);
        assert.equal(cookieCalls.set.length, 0);
        assert.equal(cookieCalls.getAll.length, 0);
        assert.deepEqual(forwarded.at(-2).payload, [
            {
                name: 'session',
                value: 'alpha',
                domain: 'example.com',
                path: '/',
                secure: false,
                httpOnly: false,
            },
        ]);
        assert.deepEqual(forwarded.at(-1).payload, [
            {
                name: 'session',
                value: 'beta',
                domain: 'example.com',
                path: '/',
                secure: false,
                httpOnly: false,
            },
        ]);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost не регистрирует legacy runtime.onMessage listener', async () => {
    const { restore, cookieCalls, runtimeListeners } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.transport.send = async () => {
        };
        assert.equal(runtimeListeners.onMessage.length, 0);
        assert.equal(cookieCalls.set.length, 0);
        assert.equal(cookieCalls.getAll.length, 0);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost подменяет Cookie header из изолированного jar', async () => {
    const { restore, webRequestListeners } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.ensureCookieIsolationListeners();
        host.transport.send = async () => {
        };

        await host.tryHandleDirectCommand({
            id: 'ctx_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-1',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
            },
        });

        await host.tryHandleDirectCommand({
            id: 'cookie_set_1',
            type: 'Request',
            tabId: '7',
            command: 'SetCookie',
            payload: {
                name: 'session',
                value: 'alpha',
                domain: '.example.com',
                path: '/',
            },
        });

        const result = webRequestListeners.beforeSendHeaders[0]({
            tabId: 7,
            url: 'https://example.com/path',
            requestHeaders: [
                { name: 'Cookie', value: 'shared=leak' },
                { name: 'Accept', value: 'text/html' },
            ],
        });

        assert.deepEqual(result, {
            requestHeaders: [
                { name: 'Cookie', value: 'session=alpha' },
                { name: 'Accept', value: 'text/html' },
            ],
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost перехватывает Set-Cookie и сохраняет его в изолированный jar', async () => {
    const { restore, webRequestListeners } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.ensureCookieIsolationListeners();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        await host.tryHandleDirectCommand({
            id: 'ctx_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-1',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
            },
        });

        const result = webRequestListeners.headersReceived[0]({
            tabId: 7,
            url: 'https://example.com/account',
            responseHeaders: [
                { name: 'Set-Cookie', value: 'session=server; Path=/; HttpOnly' },
                { name: 'Content-Type', value: 'text/html' },
            ],
        });

        const handled = await host.tryHandleDirectCommand({
            id: 'cookie_get_1',
            type: 'Request',
            tabId: '7',
            command: 'GetCookies',
        });

        assert.equal(handled, true);
        assert.deepEqual(result, {
            responseHeaders: [
                { name: 'Content-Type', value: 'text/html' },
            ],
        });
        assert.deepEqual(forwarded.at(-1).payload, [
            {
                name: 'session',
                value: 'server',
                domain: 'example.com',
                path: '/',
                secure: false,
                httpOnly: true,
            },
        ]);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost синхронизирует document.cookie после bridge cookie команд', async () => {
    const { restore, scriptingCalls } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.transport.send = async () => {
        };

        host.tabs.register({
            tabId: '7',
            connected: true,
            async send() {
            },
            async applyContext() {
            },
            async disconnect() {
            },
        });

        await host.tryHandleDirectCommand({
            id: 'ctx_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-1',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
                url: 'https://example.com/',
            },
        });

        await host.tryHandleDirectCommand({
            id: 'cookie_set_1',
            type: 'Request',
            tabId: '7',
            command: 'SetCookie',
            payload: {
                contextId: 'ctx-1',
                name: 'session',
                value: 'alpha',
                domain: '.example.com',
                path: '/',
            },
        });

        await host.tryHandleDirectCommand({
            id: 'cookie_delete_1',
            type: 'Request',
            tabId: '7',
            command: 'DeleteCookies',
            payload: {
                contextId: 'ctx-1',
            },
        });

        assert.equal(scriptingCalls.executeScript.length, 3);
        const setScript = scriptingCalls.executeScript[1].args[0];
        const deleteScript = scriptingCalls.executeScript[2].args[0];
        assert.equal(typeof setScript, 'string');
        assert.equal(typeof deleteScript, 'string');
        assert.match(setScript, /session=alpha/);
        assert.match(deleteScript, /syncCookieHeader/);
        assert.doesNotMatch(deleteScript, /session=alpha/);
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost делает page-context fallback если document.cookie sync возвращает null из main world', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub();
    const debugEvents = [];
    const scriptingCalls = [];
    const previousExecuteScript = globalThis.chrome.scripting.executeScript;

    globalThis.fetch = async (url, options) => {
        if (String(url).includes('/debug-event?')) {
            debugEvents.push(JSON.parse(String(options?.body ?? 'null')));
        }

        return {
            ok: true,
            async text() {
                return '';
            },
            async json() {
                return {};
            },
        };
    };

    try {
        globalThis.chrome.scripting.executeScript = async (details) => {
            scriptingCalls.push(details);

            if (details.world === 'MAIN') {
                return [{ result: { ok: true, value: 'null' } }];
            }

            return [{ result: { ok: true, value: 'session=alpha' } }];
        };

        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 7331,
            secret: 'diagnostics-secret',
            featureFlags: {
                enableDiagnostics: true,
            },
        }, globalThis.chrome.runtime);

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            windowId: '3',
            connectedAt: 123,
            isReady: true,
            url: 'https://example.com/',
        };

        host.tabs.register(createConnectedRuntimeEndpoint('7'), context);

        await host.syncDocumentCookieSurface('7', context, context.url);

        assert.equal(scriptingCalls.length, 2);
        assert.equal(scriptingCalls[0].world, 'MAIN');
        assert.equal(scriptingCalls[1].world, undefined);
        assert.equal(debugEvents.find((event) => event.kind === 'cookie-sync-failed'), undefined);
    } finally {
        globalThis.chrome.scripting.executeScript = previousExecuteScript;
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('BackgroundRuntimeHost публикует cookie-sync-failed если document.cookie sync остаётся null после fallback', async () => {
    const previousFetch = globalThis.fetch;
    const { restore } = installChromeStub();
    const debugEvents = [];
    const previousExecuteScript = globalThis.chrome.scripting.executeScript;

    globalThis.fetch = async (url, options) => {
        if (String(url).includes('/debug-event?')) {
            debugEvents.push(JSON.parse(String(options?.body ?? 'null')));
        }

        return {
            ok: true,
            async text() {
                return '';
            },
            async json() {
                return {};
            },
        };
    };

    try {
        globalThis.chrome.scripting.executeScript = async () => [{ result: { ok: true, value: 'null' } }];

        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 7331,
            secret: 'diagnostics-secret',
            featureFlags: {
                enableDiagnostics: true,
            },
        }, globalThis.chrome.runtime);

        const context = {
            sessionId: 'session-1',
            contextId: 'ctx-1',
            tabId: '7',
            windowId: '3',
            connectedAt: 123,
            isReady: true,
            url: 'https://example.com/',
        };

        host.tabs.register(createConnectedRuntimeEndpoint('7'), context);

        await host.syncDocumentCookieSurface('7', context, context.url);

        const syncFailedEvent = debugEvents.find((event) => event.kind === 'cookie-sync-failed');
        assert.deepEqual(syncFailedEvent?.details, {
            tabId: '7',
            contextId: 'ctx-1',
            error: 'document-cookie-sync-returned-null',
        });
    } finally {
        globalThis.chrome.scripting.executeScript = previousExecuteScript;
        restore();
        globalThis.fetch = previousFetch;
    }
});

test('BackgroundRuntimeHost включает interception и публикует RequestIntercepted event из webRequest listener', async () => {
    const { restore, webRequestListeners } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.ensureCookieIsolationListeners();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const handled = await host.tryHandleDirectCommand({
            id: 'intercept_1',
            type: 'Request',
            tabId: '7',
            command: 'InterceptRequest',
            payload: {
                enabled: true,
                patterns: ['*://example.com/api/*'],
            },
        });

        const result = webRequestListeners.beforeSendHeaders[0]({
            tabId: 7,
            url: 'https://example.com/api/items',
            method: 'POST',
            requestId: 'req-1',
            type: 'xmlhttprequest',
            timeStamp: 1730000000123,
            requestHeaders: [
                { name: 'Accept', value: 'application/json' },
            ],
        });

        await new Promise((resolve) => setTimeout(resolve, 0));

        assert.equal(handled, true);
        assert.equal(result, undefined);
        assert.equal(forwarded.at(-2).status, 'Ok');
        assert.equal(forwarded.at(-1).event, 'RequestIntercepted');
        assert.equal(forwarded.at(-1).tabId, '7');
        assert.deepEqual(forwarded.at(-1).payload, {
            url: 'https://example.com/api/items',
            method: 'POST',
            headers: {
                Accept: 'application/json',
            },
            requestId: 'req-1',
            type: 'xmlhttprequest',
            supportsNavigationFulfillment: false,
            ts: 1730000000123,
            isNavigate: false,
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost публикует ResponseReceived event для включённого interception tab', async () => {
    const { restore, webRequestListeners } = installChromeStub();

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.ensureCookieIsolationListeners();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        await host.tryHandleDirectCommand({
            id: 'ctx_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-1',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
            },
        });

        await host.tryHandleDirectCommand({
            id: 'intercept_1',
            type: 'Request',
            tabId: '7',
            command: 'InterceptRequest',
            payload: {
                enabled: true,
            },
        });

        const result = webRequestListeners.headersReceived[0]({
            tabId: 7,
            url: 'https://example.com/api/items',
            method: 'GET',
            requestId: 'req-2',
            type: 'xmlhttprequest',
            timeStamp: 1730000000456,
            statusCode: 204,
            statusLine: 'HTTP/1.1 204 No Content',
            responseHeaders: [
                { name: 'Content-Type', value: 'application/json' },
            ],
        });

        await new Promise((resolve) => setTimeout(resolve, 0));

        assert.equal(result, undefined);
        assert.equal(forwarded.at(-1).event, 'ResponseReceived');
        assert.equal(forwarded.at(-1).tabId, '7');
        assert.deepEqual(forwarded.at(-1).payload, {
            url: 'https://example.com/api/items',
            method: 'GET',
            headers: {
                'Content-Type': 'application/json',
            },
            requestId: 'req-2',
            type: 'xmlhttprequest',
            ts: 1730000000456,
            isNavigate: false,
            statusCode: 204,
            reasonPhrase: 'HTTP/1.1 204 No Content',
        });
    } finally {
        restore();
    }
});

test('BackgroundRuntimeHost использует blocking /intercept и /observed-request-headers для request listener', async () => {
    const { restore, webRequestListeners } = installChromeStub();
    const xhr = installXmlHttpRequestStub((request) => {
        if (request.url.includes('/intercept?')) {
            return {
                status: 200,
                body: {
                    action: 'continue',
                    headers: {
                        'X-Intercepted': 'true',
                    },
                },
            };
        }

        if (request.url.includes('/observed-request-headers?')) {
            return {
                status: 204,
                body: '',
            };
        }

        return {
            status: 404,
            body: '',
        };
    });

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.ensureCookieIsolationListeners();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        await host.tryHandleDirectCommand({
            id: 'intercept_blocking_1',
            type: 'Request',
            tabId: '7',
            command: 'InterceptRequest',
            payload: {
                enabled: true,
                patterns: ['*://example.com/api/*'],
            },
        });

        const result = webRequestListeners.beforeSendHeaders[0]({
            tabId: 7,
            url: 'https://example.com/api/items',
            method: 'POST',
            requestId: 'req-blocking-1',
            type: 'xmlhttprequest',
            timeStamp: 1730000000789,
            requestHeaders: [
                { name: 'Accept', value: 'application/json' },
            ],
        });

        assert.deepEqual(result, {
            requestHeaders: [
                { name: 'Accept', value: 'application/json' },
                { name: 'X-Intercepted', value: 'true' },
            ],
        });
        assert.equal(forwarded.length, 1);
        assert.equal(forwarded[0].status, 'Ok');
        assert.equal(xhr.requests.length, 2);
        assert.match(xhr.requests[0].url, /\/intercept\?secret=top-secret$/);
        assert.equal(xhr.requests[0].payload.requestId, 'req-blocking-1');
        assert.equal(xhr.requests[0].payload.headers.Accept, 'application/json');
        assert.match(xhr.requests[1].url, /\/observed-request-headers\?secret=top-secret$/);
        assert.equal(xhr.requests[1].payload.headers['X-Intercepted'], 'true');
    } finally {
        xhr.restore();
        restore();
    }
});

test('BackgroundRuntimeHost добавляет low/high entropy Client Hints HTTP headers из tab context до observed-request-headers', async () => {
    const { restore, webRequestListeners } = installChromeStub();
    const xhr = installXmlHttpRequestStub((request) => {
        if (request.url.includes('/intercept?')) {
            return {
                status: 200,
                body: {
                    action: 'continue',
                },
            };
        }

        if (request.url.includes('/observed-request-headers?')) {
            return {
                status: 204,
                body: '',
            };
        }

        return {
            status: 404,
            body: '',
        };
    });

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.ensureCookieIsolationListeners();
        host.transport.send = async () => {
        };

        await host.tryHandleDirectCommand({
            id: 'ctx_client_hints_1',
            type: 'Request',
            command: 'SetTabContext',
            payload: {
                sessionId: 'session-1',
                contextId: 'ctx-client-hints',
                tabId: '7',
                connectedAt: 123,
                isReady: true,
                clientHints: {
                    platform: 'Android',
                    platformVersion: '14.0.0',
                    mobile: true,
                    architecture: 'arm',
                    model: 'Pixel 8',
                    bitness: '64',
                    brands: [
                        { brand: 'Chromium', version: '131' },
                        { brand: 'Not_A Brand', version: '24' },
                    ],
                    fullVersionList: [
                        { brand: 'Chromium', version: '131.0.6778.70' },
                        { brand: 'Not_A Brand', version: '24.0.0.0' },
                    ],
                },
            },
        });

        await host.tryHandleDirectCommand({
            id: 'intercept_client_hints_1',
            type: 'Request',
            tabId: '7',
            command: 'InterceptRequest',
            payload: {
                enabled: true,
            },
        });

        const result = webRequestListeners.beforeSendHeaders[0]({
            tabId: 7,
            url: 'https://example.com/api/items',
            method: 'GET',
            requestId: 'req-client-hints-1',
            type: 'xmlhttprequest',
            timeStamp: 1730000000888,
            requestHeaders: [
                { name: 'Accept', value: 'application/json' },
            ],
        });

        assert.deepEqual(result, {
            requestHeaders: [
                { name: 'Accept', value: 'application/json' },
                { name: 'Sec-CH-UA', value: '"Chromium";v="131", "Not_A Brand";v="24"' },
                { name: 'Sec-CH-UA-Full-Version-List', value: '"Chromium";v="131.0.6778.70", "Not_A Brand";v="24.0.0.0"' },
                { name: 'Sec-CH-UA-Platform', value: '"Android"' },
                { name: 'Sec-CH-UA-Platform-Version', value: '"14.0.0"' },
                { name: 'Sec-CH-UA-Mobile', value: '?1' },
                { name: 'Sec-CH-UA-Arch', value: '"arm"' },
                { name: 'Sec-CH-UA-Model', value: '"Pixel 8"' },
                { name: 'Sec-CH-UA-Bitness', value: '"64"' },
            ],
        });
        assert.equal(xhr.requests.length, 2);
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA'], '"Chromium";v="131", "Not_A Brand";v="24"');
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA-Full-Version-List'], '"Chromium";v="131.0.6778.70", "Not_A Brand";v="24.0.0.0"');
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA-Platform'], '"Android"');
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA-Platform-Version'], '"14.0.0"');
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA-Mobile'], '?1');
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA-Arch'], '"arm"');
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA-Model'], '"Pixel 8"');
        assert.equal(xhr.requests[0].payload.headers['Sec-CH-UA-Bitness'], '"64"');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA'], '"Chromium";v="131", "Not_A Brand";v="24"');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA-Full-Version-List'], '"Chromium";v="131.0.6778.70", "Not_A Brand";v="24.0.0.0"');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA-Platform'], '"Android"');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA-Platform-Version'], '"14.0.0"');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA-Mobile'], '?1');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA-Arch'], '"arm"');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA-Model'], '"Pixel 8"');
        assert.equal(xhr.requests[1].payload.headers['Sec-CH-UA-Bitness'], '"64"');
    } finally {
        xhr.restore();
        restore();
    }
});

test('handleClientHintsRequestInterception использует brands как fallback для Sec-CH-UA-Full-Version-List', () => {
    const mutation = handleClientHintsRequestInterception({
        tabId: 7,
        requestHeaders: [
            { name: 'Accept', value: 'application/json' },
        ],
    }, () => ({
        sessionId: 'session-1',
        contextId: 'ctx-client-hints',
        tabId: '7',
        connectedAt: 123,
        isReady: true,
        clientHints: {
            brands: [
                { brand: 'Chromium', version: '131' },
                { brand: 'Not_A Brand', version: '24' },
            ],
        },
    }));

    assert.deepEqual(mutation, {
        requestHeaders: [
            { name: 'Accept', value: 'application/json' },
            { name: 'Sec-CH-UA', value: '"Chromium";v="131", "Not_A Brand";v="24"' },
            { name: 'Sec-CH-UA-Full-Version-List', value: '"Chromium";v="131", "Not_A Brand";v="24"' },
        ],
    });
});

test('BackgroundRuntimeHost отменяет request-side main_frame fulfill вместо network fallback', async () => {
    const { restore, webRequestListeners } = installChromeStub();
    const xhr = installXmlHttpRequestStub((request) => {
        if (request.url.includes('/intercept?')) {
            return {
                status: 200,
                body: {
                    action: 'fulfill',
                    url: 'http://127.0.0.1:9222/fulfill/1',
                },
            };
        }

        if (request.url.includes('/observed-request-headers?')) {
            return {
                status: 204,
                body: '',
            };
        }

        return {
            status: 404,
            body: '',
        };
    });

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.ensureCookieIsolationListeners();
        host.transport.send = async () => {
        };

        await host.tryHandleDirectCommand({
            id: 'intercept_main_frame_1',
            type: 'Request',
            tabId: '7',
            command: 'InterceptRequest',
            payload: {
                enabled: true,
                patterns: ['*://example.com/page/*'],
            },
        });

        const result = webRequestListeners.beforeSendHeaders[0]({
            tabId: 7,
            url: 'https://example.com/page/1',
            method: 'GET',
            requestId: 'req-main-frame-fulfill',
            type: 'main_frame',
            timeStamp: 1730000000999,
            requestHeaders: [
                { name: 'Accept', value: 'text/html' },
            ],
        });

        assert.deepEqual(result, { cancel: true });
        assert.equal(xhr.requests.length, 2);
        assert.match(xhr.requests[0].url, /\/intercept\?secret=top-secret$/);
        assert.match(xhr.requests[1].url, /\/observed-request-headers\?secret=top-secret$/);
        assert.equal(xhr.requests.some((request) => request.url.includes('/fulfill/1')), false);
    } finally {
        xhr.restore();
        restore();
    }
});

test('BackgroundRuntimeHost использует blocking /intercept-response для response listener', async () => {
    const { restore, webRequestListeners } = installChromeStub();
    const xhr = installXmlHttpRequestStub((request) => {
        if (request.url.includes('/intercept-response?')) {
            return {
                status: 200,
                body: {
                    action: 'continue',
                    responseHeaders: {
                        'X-Intercepted-Response': 'yes',
                    },
                },
            };
        }

        return {
            status: 404,
            body: '',
        };
    });

    try {
        const host = new BackgroundRuntimeHost();
        host.config = normalizeBootstrapConfig({
            host: '127.0.0.1',
            port: 9222,
            secret: 'top-secret',
        }, globalThis.chrome.runtime);
        host.ensureCookieIsolationListeners();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        await host.tryHandleDirectCommand({
            id: 'intercept_blocking_2',
            type: 'Request',
            tabId: '7',
            command: 'InterceptRequest',
            payload: {
                enabled: true,
            },
        });

        const result = webRequestListeners.headersReceived[0]({
            tabId: 7,
            url: 'https://example.com/api/items',
            method: 'GET',
            requestId: 'resp-blocking-1',
            type: 'xmlhttprequest',
            timeStamp: 1730000000999,
            statusCode: 200,
            statusLine: 'HTTP/1.1 200 OK',
            responseHeaders: [
                { name: 'Content-Type', value: 'application/json' },
            ],
        });

        assert.deepEqual(result, {
            responseHeaders: [
                { name: 'Content-Type', value: 'application/json' },
                { name: 'X-Intercepted-Response', value: 'yes' },
            ],
        });
        assert.equal(forwarded.length, 1);
        assert.equal(forwarded[0].status, 'Ok');
        assert.equal(xhr.requests.length, 1);
        assert.match(xhr.requests[0].url, /\/intercept-response\?secret=top-secret$/);
        assert.equal(xhr.requests[0].payload.statusCode, 200);
        assert.equal(xhr.requests[0].payload.headers['Content-Type'], 'application/json');
    } finally {
        xhr.restore();
        restore();
    }
});

test('BackgroundRuntimeHost удаляет cookies напрямую через browser.cookies.remove', async () => {
    const { restore, cookieCalls } = installChromeStub({
        getAllCookies: async () => ([
            {
                name: 'session',
                value: 'abc',
                domain: '.example.com',
                path: '/',
                secure: true,
            },
            {
                name: 'prefs',
                value: 'dark',
                path: '/ui',
            },
        ]),
    });

    try {
        const host = new BackgroundRuntimeHost();
        const forwarded = [];
        host.transport.send = async (message) => {
            forwarded.push(message);
        };

        const handled = await host.tryHandleDirectCommand({
            id: 'cookie_delete_1',
            type: 'Request',
            tabId: '7',
            command: 'DeleteCookies',
        });

        assert.equal(handled, true);
        assert.equal(cookieCalls.remove.length, 2);
        assert.deepEqual(cookieCalls.remove[0], {
            url: 'https://example.com/',
            name: 'session',
        });
        assert.deepEqual(cookieCalls.remove[1], {
            url: 'https://example.com/',
            name: 'prefs',
        });
        assert.equal(forwarded[0].status, 'Ok');
    } finally {
        restore();
    }
});

function installChromeStub(tabOverrides = {}, options = {}) {
    const resolvedOptions = {
        includeScripting: true,
        includeProxyApi: false,
        includeProxyAuthRequired: false,
        ...options,
    };
    const previousChrome = globalThis.chrome;
    const tabsCalls = {
        update: [],
        reload: [],
        executeScript: [],
    };
    const scriptingCalls = {
        executeScript: [],
    };
    const windowCalls = {
        create: [],
        get: [],
        update: [],
        remove: [],
    };
    const cookieCalls = {
        getAll: [],
        set: [],
        remove: [],
    };
    const storageCalls = {
        managedGet: [],
        localGet: [],
    };
    const webRequestListeners = {
        beforeSendHeaders: [],
        headersReceived: [],
        authRequired: [],
    };
    const proxyListeners = {
        onRequest: [],
    };
    const runtimeListeners = {
        onConnect: [],
        onMessage: [],
    };

    const chromeStub = {
        runtime: {
            getURL(path) {
                return `extension://${path}`;
            },
            getManifest() {
                return { version: '0.3.0-test' };
            },
            onConnect: {
                addListener(listener) {
                    runtimeListeners.onConnect.push(listener);
                },
                removeListener(listener) {
                    const index = runtimeListeners.onConnect.indexOf(listener);
                    if (index >= 0) {
                        runtimeListeners.onConnect.splice(index, 1);
                    }
                },
            },
            onMessage: {
                addListener(listener) {
                    runtimeListeners.onMessage.push(listener);
                },
                removeListener(listener) {
                    const index = runtimeListeners.onMessage.indexOf(listener);
                    if (index >= 0) {
                        runtimeListeners.onMessage.splice(index, 1);
                    }
                },
            },
        },
        tabs: {
            get: async (tabId) => {
                if (typeof tabOverrides.getTab === 'function') {
                    return tabOverrides.getTab(tabId);
                }

                return { id: tabId, windowId: 3, url: 'https://example.com/', title: 'Example' };
            },
            query: async () => [],
            create: async (createProperties) => ({ id: 41, windowId: 3, url: createProperties.url ?? 'about:blank' }),
            update: async (tabId, updateProperties) => {
                tabsCalls.update.push({ tabId, updateProperties });
                if (typeof tabOverrides.update === 'function') {
                    return tabOverrides.update(tabId, updateProperties);
                }

                return { id: tabId, url: updateProperties.url ?? 'about:blank' };
            },
            reload: async (tabId) => {
                tabsCalls.reload.push({ tabId });
                if (typeof tabOverrides.reload === 'function') {
                    return tabOverrides.reload(tabId);
                }
            },
            remove: async () => {
            },
            executeScript: async (tabId, details) => {
                tabsCalls.executeScript.push({ tabId, details });
                if (typeof tabOverrides.executeScript === 'function') {
                    return tabOverrides.executeScript(tabId, details);
                }

                return [{ state: 'blocked' }];
            },
        },
        windows: {
            create: async (createData) => {
                windowCalls.create.push(createData);
                if (typeof tabOverrides.createWindow === 'function') {
                    return tabOverrides.createWindow(createData);
                }

                return { id: 12, tabs: [{ id: 77, windowId: 12, url: createData.url ?? 'about:blank' }] };
            },
            get: async (windowId, getInfo) => {
                windowCalls.get.push({ windowId, getInfo });
                if (typeof tabOverrides.get === 'function') {
                    return tabOverrides.get(windowId, getInfo);
                }

                return { id: windowId, left: 10, top: 20, width: 800, height: 600, state: 'normal' };
            },
            getAll: async () => [],
            update: async (windowId, updateInfo) => {
                windowCalls.update.push({ windowId, updateInfo });
                if (typeof tabOverrides.updateWindow === 'function') {
                    return tabOverrides.updateWindow(windowId, updateInfo);
                }

                return { id: windowId };
            },
            remove: async (windowId) => {
                windowCalls.remove.push(windowId);
                if (typeof tabOverrides.removeWindow === 'function') {
                    return tabOverrides.removeWindow(windowId);
                }
            },
        },
        cookies: {
            getAll: async (details) => {
                cookieCalls.getAll.push(details);
                if (typeof tabOverrides.getAllCookies === 'function') {
                    return tabOverrides.getAllCookies(details);
                }

                return [];
            },
            set: async (details) => {
                cookieCalls.set.push(details);
                if (typeof tabOverrides.setCookie === 'function') {
                    return tabOverrides.setCookie(details);
                }

                return details;
            },
            remove: async (details) => {
                cookieCalls.remove.push(details);
                if (typeof tabOverrides.removeCookie === 'function') {
                    return tabOverrides.removeCookie(details);
                }

                return details;
            },
        },
        webRequest: {
            onBeforeSendHeaders: {
                addListener(listener) {
                    webRequestListeners.beforeSendHeaders.push(listener);
                },
            },
            onHeadersReceived: {
                addListener(listener) {
                    webRequestListeners.headersReceived.push(listener);
                },
            },
        },
        storage: {
            managed: {
                get: async (keys) => {
                    storageCalls.managedGet.push(keys);
                    if (typeof tabOverrides.getManagedStorage === 'function') {
                        return tabOverrides.getManagedStorage(keys);
                    }

                    return {};
                },
            },
            local: {
                get: async (keys) => {
                    storageCalls.localGet.push(keys);
                    if (typeof tabOverrides.getLocalStorage === 'function') {
                        return tabOverrides.getLocalStorage(keys);
                    }

                    return {};
                },
            },
        },
    };

    if (resolvedOptions.includeProxyAuthRequired) {
        chromeStub.webRequest.onAuthRequired = {
            addListener(listener) {
                webRequestListeners.authRequired.push(listener);
            },
        };
    }

    if (resolvedOptions.includeProxyApi) {
        chromeStub.proxy = {
            onRequest: {
                addListener(listener) {
                    proxyListeners.onRequest.push(listener);
                },
            },
        };
    }

    if (resolvedOptions.includeScripting) {
        chromeStub.scripting = {
            executeScript: async (details) => {
                scriptingCalls.executeScript.push(details);
                return [{ result: { ok: true, value: 'ok' } }];
            },
        };
    }

    globalThis.chrome = chromeStub;

    return {
        cookieCalls,
        runtimeListeners,
        scriptingCalls,
        storageCalls,
        tabsCalls,
        proxyListeners,
        webRequestListeners,
        windowCalls,
        restore() {
            if (previousChrome === undefined) {
                delete globalThis.chrome;
                return;
            }

            globalThis.chrome = previousChrome;
        },
    };
}

function createConnectedRuntimeEndpoint(tabId) {
    return {
        tabId,
        connected: true,
        async send() {
        },
        async applyContext() {
        },
        async disconnect() {
        },
    };
}

function createRuntimePortStub(tabId, windowId, options = {}) {
    const messageListeners = [];
    const disconnectListeners = [];
    const postedMessages = [];
    let disconnectCallCount = 0;

    return {
        port: {
            sender: {
                tab: {
                    id: tabId,
                    windowId,
                },
            },
            postMessage(message) {
                postedMessages.push(message);
                options.postMessage?.(message);
            },
            disconnect() {
                disconnectCallCount += 1;
            },
            onMessage: {
                addListener(listener) {
                    messageListeners.push(listener);
                },
                removeListener(listener) {
                    const index = messageListeners.indexOf(listener);
                    if (index >= 0) {
                        messageListeners.splice(index, 1);
                    }
                },
            },
            onDisconnect: {
                addListener(listener) {
                    disconnectListeners.push(listener);
                },
                removeListener(listener) {
                    const index = disconnectListeners.indexOf(listener);
                    if (index >= 0) {
                        disconnectListeners.splice(index, 1);
                    }
                },
            },
        },
        postedMessages,
        get disconnectCallCount() {
            return disconnectCallCount;
        },
        simulateDisconnect() {
            for (const listener of [...disconnectListeners]) {
                listener();
            }
        },
    };
}

function installXmlHttpRequestStub(resolver) {
    const previousXmlHttpRequest = globalThis.XMLHttpRequest;
    const requests = [];

    class FakeXmlHttpRequest {
        constructor() {
            this.headers = {};
            this.method = 'GET';
            this.url = '';
            this.status = 0;
            this.responseText = '';
            this.async = true;
            this.onload = null;
            this.onerror = null;
            this.onabort = null;
        }

        open(method, url, async = true) {
            this.method = method;
            this.url = url;
            this.async = async !== false;
        }

        setRequestHeader(name, value) {
            this.headers[name] = value;
        }

        send(body) {
            const payload = typeof body === 'string' && body.length > 0
                ? JSON.parse(body)
                : undefined;

            const request = {
                method: this.method,
                url: this.url,
                headers: { ...this.headers },
                payload,
            };
            requests.push(request);

            const response = resolver(request) ?? { status: 200, body: '' };
            this.status = response.status ?? 200;
            this.responseText = typeof response.body === 'string'
                ? response.body
                : JSON.stringify(response.body ?? '');

            if (this.async !== true) {
                return;
            }

            queueMicrotask(() => {
                if (response.abort === true) {
                    this.onabort?.();
                    return;
                }

                if (response.error === true) {
                    this.onerror?.();
                    return;
                }

                this.onload?.();
            });
        }
    }

    globalThis.XMLHttpRequest = FakeXmlHttpRequest;

    return {
        requests,
        restore() {
            if (previousXmlHttpRequest === undefined) {
                delete globalThis.XMLHttpRequest;
                return;
            }

            globalThis.XMLHttpRequest = previousXmlHttpRequest;
        },
    };
}

function dispatchRuntimeMessage(listener, message, sender = { tab: { id: 7, windowId: 3, url: 'https://example.com/' } }) {
    return new Promise((resolve, reject) => {
        let settled = false;

        const sendResponse = (response) => {
            settled = true;
            resolve(response);
        };

        try {
            const keepAlive = listener(message, sender, sendResponse);
            if (keepAlive !== true) {
                queueMicrotask(() => {
                    if (!settled) {
                        resolve(undefined);
                    }
                });
            }
        } catch (error) {
            reject(error);
        }
    });
}