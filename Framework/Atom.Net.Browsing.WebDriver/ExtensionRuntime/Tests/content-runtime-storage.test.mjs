import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {
    bootstrapContentRuntime,
    buildCacheIsolationScript,
    buildCookieIsolationScript,
    buildCookieSyncScript,
    buildExecuteScriptSource,
    buildIndexedDbIsolationScript,
    buildMainWorldContextScript,
    buildStorageIsolationScript,
    resolveBridgeUtilityPort,
    resolvePreferredBridgeRuntimeConfig,
    tryLoadDiscoveryDocumentRuntimeConfig,
} from '../content.runtime.ts';

class FakeStorage {
    #items = new Map();

    getItem(key) {
        const normalizedKey = String(key);
        return this.#items.has(normalizedKey)
            ? this.#items.get(normalizedKey)
            : null;
    }

    setItem(key, value) {
        this.#items.set(String(key), String(value));
    }

    removeItem(key) {
        this.#items.delete(String(key));
    }

    key(index) {
        return Array.from(this.#items.keys())[Number(index)] ?? null;
    }

    clear() {
        this.#items.clear();
    }

    get length() {
        return this.#items.size;
    }

    snapshot() {
        return new Map(this.#items);
    }
}

test('content runtime prefers live discovery meta config when present', () => {
    const originalDocument = globalThis.document;
    const originalChrome = globalThis.chrome;
    const originalBrowser = globalThis.browser;
    const manifestVersion = '0.8.2-test-live';

    try {
        globalThis.document = {
            querySelector(selector) {
                if (selector === 'meta[name="atom-bridge-port"]') {
                    return {
                        getAttribute(name) {
                            return name === 'content' ? '43123' : null;
                        },
                    };
                }

                if (selector === 'meta[name="atom-bridge-proxy-port"]') {
                    return {
                        getAttribute(name) {
                            return name === 'content' ? '9443' : null;
                        },
                    };
                }

                if (selector === 'meta[name="atom-bridge-secret"]') {
                    return {
                        getAttribute(name) {
                            return name === 'content' ? 'stable-live-secret' : null;
                        },
                    };
                }

                return null;
            },
        };

        globalThis.browser = undefined;
        globalThis.chrome = {
            runtime: {
                getManifest() {
                    return { version: manifestVersion };
                },
            },
        };

        const config = tryLoadDiscoveryDocumentRuntimeConfig();
        assert.ok(config);
        assert.equal(config.host, '127.0.0.1');
        assert.equal(config.port, 43123);
        assert.equal(config.proxyPort, 9443);
        assert.equal(config.secret, 'stable-live-secret');
        assert.equal(config.extensionVersion, manifestVersion);
        assert.equal(config.featureFlags.enableCallbackHooks, true);
    } finally {
        globalThis.document = originalDocument;
        globalThis.chrome = originalChrome;
        globalThis.browser = originalBrowser;
    }
});

test('content runtime replaces cached bundled config with live discovery meta config when it becomes available', () => {
    const originalDocument = globalThis.document;
    const originalChrome = globalThis.chrome;
    const originalBrowser = globalThis.browser;
    const manifestVersion = '0.8.2-test-refresh';

    try {
        globalThis.document = {
            querySelector(selector) {
                if (selector === 'meta[name="atom-bridge-port"]') {
                    return {
                        getAttribute(name) {
                            return name === 'content' ? '43123' : null;
                        },
                    };
                }

                if (selector === 'meta[name="atom-bridge-proxy-port"]') {
                    return {
                        getAttribute(name) {
                            return name === 'content' ? '9443' : null;
                        },
                    };
                }

                if (selector === 'meta[name="atom-bridge-secret"]') {
                    return {
                        getAttribute(name) {
                            return name === 'content' ? 'stable-live-secret' : null;
                        },
                    };
                }

                return null;
            },
        };

        globalThis.browser = undefined;
        globalThis.chrome = {
            runtime: {
                getManifest() {
                    return { version: manifestVersion };
                },
            },
        };

        const config = resolvePreferredBridgeRuntimeConfig({
            host: '127.0.0.1',
            port: 39999,
            secret: 'stale-bundled-secret',
            sessionId: 'content_stale',
            protocolVersion: 1,
            browserFamily: 'firefox',
            extensionVersion: 'stale-bundled-version',
            featureFlags: {
                enableNavigationEvents: true,
                enableCallbackHooks: true,
                enableInterception: true,
                enableDiagnostics: true,
                enableKeepAlive: true,
            },
        });

        assert.ok(config);
        assert.equal(config.host, '127.0.0.1');
        assert.equal(config.port, 43123);
        assert.equal(config.proxyPort, 9443);
        assert.equal(config.secret, 'stable-live-secret');
        assert.equal(config.extensionVersion, manifestVersion);
    } finally {
        globalThis.document = originalDocument;
        globalThis.chrome = originalChrome;
        globalThis.browser = originalBrowser;
    }
});

test('content runtime prefers proxyPort for utility routes and falls back to main port', () => {
    assert.equal(resolveBridgeUtilityPort({ port: 43123, proxyPort: 9443 }), 9443);
    assert.equal(resolveBridgeUtilityPort({ port: 43123 }), 43123);
});

function createScriptSandbox() {
    function Navigator() {
    }

    function Document() {
    }

    function HTMLDocument() {
    }

    const document = new HTMLDocument();
    const navigator = Object.create(Navigator.prototype);
    navigator.userAgent = 'Mozilla/5.0 Original';
    navigator.appVersion = '5.0 Original';
    navigator.platform = 'OriginalPlatform';
    navigator.language = 'en-US';
    navigator.languages = ['en-US', 'en'];
    navigator.hardwareConcurrency = 4;
    navigator.deviceMemory = 8;
    navigator.maxTouchPoints = 0;

    const sandbox = {
        console,
        document,
        Document,
        HTMLDocument,
        Navigator,
        navigator,
        Intl,
        localStorage: new FakeStorage(),
        sessionStorage: new FakeStorage(),
        setTimeout,
        clearTimeout,
        indexedDB: {
            opens: [],
            deletes: [],
            open(name, version) {
                this.opens.push({ name, version });
                return { name, version };
            },
            deleteDatabase(name) {
                this.deletes.push(name);
                return name;
            },
        },
        caches: {
            opened: [],
            deleted: [],
            checked: [],
            names: [],
            async open(name) {
                this.opened.push(name);
                if (!this.names.includes(name)) {
                    this.names.push(name);
                }

                return { name };
            },
            async delete(name) {
                this.deleted.push(name);
                this.names = this.names.filter((currentName) => currentName !== name);
                return true;
            },
            async has(name) {
                this.checked.push(name);
                return this.names.includes(name);
            },
            async keys() {
                return Array.from(this.names);
            },
        },
        __atomTabContext: {
            contextId: 'ctx-a',
        },
        Window: function Window() {
        },
    };

    sandbox.globalThis = sandbox;
    sandbox.Document.prototype = {};
    sandbox.HTMLDocument.prototype = Object.create(sandbox.Document.prototype);
    Object.setPrototypeOf(document, sandbox.HTMLDocument.prototype);
    sandbox.Window.prototype = {};
    return vm.createContext(sandbox);
}

function installScript(context, script) {
    vm.runInContext(`(() => {
const globalObject = globalThis;
const readContext = () => globalObject.__atomTabContext;
${script}
})();`, context);
}

function createMainWorldCallbackSandbox({ port = '43123', proxyPort = '9443', secret = 'stable-live-secret' } = {}) {
    const globalListeners = new Map();
    const documentListeners = new Map();
    const requestNodes = [];
    const allNodes = [];
    const requests = [];

    function addListener(store, type, listener) {
        if (!store.has(type)) {
            store.set(type, []);
        }

        store.get(type).push(listener);
    }

    function removeNode(node) {
        const requestIndex = requestNodes.indexOf(node);
        if (requestIndex >= 0) {
            requestNodes.splice(requestIndex, 1);
        }

        const nodeIndex = allNodes.indexOf(node);
        if (nodeIndex >= 0) {
            allNodes.splice(nodeIndex, 1);
        }
    }

    const documentElement = {
        appendChild(node) {
            allNodes.push(node);
            if (node.dataset?.atomCallbackRequest === '1') {
                requestNodes.push(node);
            }

            return node;
        },
    };

    const document = {
        documentElement,
        head: null,
        body: null,
        querySelector(selector) {
            if (selector === 'meta[name="atom-bridge-port"]') {
                return { getAttribute: (name) => (name === 'content' ? port : null) };
            }

            if (selector === 'meta[name="atom-bridge-proxy-port"]') {
                return { getAttribute: (name) => (name === 'content' ? proxyPort : null) };
            }

            if (selector === 'meta[name="atom-bridge-secret"]') {
                return { getAttribute: (name) => (name === 'content' ? secret : null) };
            }

            return null;
        },
        querySelectorAll(selector) {
            return selector === 'script[data-atom-callback-request="1"]'
                ? Array.from(requestNodes)
                : [];
        },
        createElement() {
            const node = {
                dataset: {},
                textContent: '',
                id: '',
                type: '',
                remove() {
                    removeNode(node);
                },
            };

            return node;
        },
        getElementById(id) {
            return allNodes.find((node) => node.id === id) ?? null;
        },
        addEventListener(type, listener) {
            addListener(documentListeners, type, listener);
        },
    };

    class FakeXmlHttpRequest {
        constructor() {
            this.headers = {};
            this.status = 0;
            this.responseText = '';
        }

        open(method, url, async) {
            this.method = method;
            this.url = url;
            this.async = async;
        }

        setRequestHeader(name, value) {
            this.headers[name] = value;
        }

        send(body) {
            requests.push({
                method: this.method,
                url: this.url,
                async: this.async,
                headers: { ...this.headers },
                body,
            });
            this.status = 200;
            this.responseText = JSON.stringify({ action: 'continue' });
        }
    }

    const sandbox = {
        console,
        document,
        JSON,
        __atomTabContext: createTabContext(),
        XMLHttpRequest: FakeXmlHttpRequest,
        addEventListener(type, listener) {
            addListener(globalListeners, type, listener);
        },
    };

    sandbox.globalThis = sandbox;

    return {
        context: vm.createContext(sandbox),
        requests,
        dispatchCallback(payload) {
            const node = document.createElement('script');
            node.dataset.atomCallbackRequest = '1';
            node.textContent = JSON.stringify(payload);
            documentElement.appendChild(node);

            const event = {
                stopped: false,
                stopImmediatePropagation() {
                    this.stopped = true;
                },
            };

            for (const listener of globalListeners.get('atom-webdriver-callback-request') ?? []) {
                listener(event);
            }

            for (const listener of documentListeners.get('atom-webdriver-callback-request') ?? []) {
                listener(event);
            }
        },
        getResponseNode(requestId) {
            return document.getElementById(`atom-callback-response-${requestId}`);
        },
    };
}

function createContentRuntimeCallbackObserverHarness({ port = '43123', proxyPort = '9443', secret = 'stable-live-secret' } = {}) {
    const callbackPayloadNodes = [];
    const callbackFinalizedNodes = [];
    const responseNodes = new Map();
    const globalListeners = new Map();
    const documentListeners = new Map();
    const messageListeners = [];
    const disconnectListeners = [];
    const postedMessages = [];

    function addListener(store, type, listener) {
        if (!store.has(type)) {
            store.set(type, []);
        }

        store.get(type).push(listener);
    }

    function removeListener(store, type, listener) {
        const listeners = store.get(type);
        if (!listeners) {
            return;
        }

        const index = listeners.indexOf(listener);
        if (index >= 0) {
            listeners.splice(index, 1);
        }
    }

    function removeNode(node) {
        const callbackPayloadIndex = callbackPayloadNodes.indexOf(node);
        if (callbackPayloadIndex >= 0) {
            callbackPayloadNodes.splice(callbackPayloadIndex, 1);
        }

        const callbackFinalizedIndex = callbackFinalizedNodes.indexOf(node);
        if (callbackFinalizedIndex >= 0) {
            callbackFinalizedNodes.splice(callbackFinalizedIndex, 1);
        }

        if (typeof node.id === 'string' && node.id.length > 0) {
            responseNodes.delete(node.id);
        }
    }

    function createNode() {
        const node = {
            dataset: {},
            id: '',
            textContent: '',
            type: '',
            remove() {
                removeNode(node);
            },
        };

        return node;
    }

    function dispatchToStore(store, event) {
        for (const listener of store.get(event.type) ?? []) {
            listener(event);
        }
    }

    const documentElement = {
        appendChild(node) {
            if (typeof node.id === 'string' && node.id.length > 0) {
                responseNodes.set(node.id, node);
            }

            return node;
        },
    };

    const document = {
        readyState: 'loading',
        documentElement,
        head: null,
        body: null,
        querySelector(selector) {
            if (selector === 'meta[name="atom-bridge-port"]') {
                return { getAttribute: (name) => (name === 'content' ? port : null) };
            }

            if (selector === 'meta[name="atom-bridge-proxy-port"]') {
                return { getAttribute: (name) => (name === 'content' ? proxyPort : null) };
            }

            if (selector === 'meta[name="atom-bridge-secret"]') {
                return { getAttribute: (name) => (name === 'content' ? secret : null) };
            }

            return null;
        },
        querySelectorAll(selector) {
            if (selector === 'script[data-atom-callback-payload="1"]') {
                return Array.from(callbackPayloadNodes);
            }

            if (selector === 'script[data-atom-callback-finalized="1"]') {
                return Array.from(callbackFinalizedNodes);
            }

            return [];
        },
        createElement() {
            return createNode();
        },
        getElementById(id) {
            return responseNodes.get(id) ?? null;
        },
        addEventListener(type, listener) {
            addListener(documentListeners, type, listener);
        },
        dispatchEvent(event) {
            dispatchToStore(documentListeners, event);
            return true;
        },
    };

    const runtimePort = {
        postMessage(message) {
            postedMessages.push(message);

            if (message?.action === 'executeInMain' && typeof message.requestId === 'string') {
                queueMicrotask(() => {
                    for (const listener of messageListeners) {
                        listener({
                            action: 'mainWorldResult',
                            requestId: message.requestId,
                            status: 'ok',
                            value: 'null',
                        });
                    }
                });
            }
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
        disconnect() {
        },
    };

    const consoleStub = {
        debug() {
        },
        info() {
        },
        log() {
        },
        warn() {
        },
        error() {
        },
    };

    const originalState = {
        document: globalThis.document,
        chrome: globalThis.chrome,
        browser: globalThis.browser,
        console: globalThis.console,
        MutationObserver: globalThis.MutationObserver,
        addEventListener: globalThis.addEventListener,
        dispatchEvent: globalThis.dispatchEvent,
        hasDocument: Object.prototype.hasOwnProperty.call(globalThis, 'document'),
        hasChrome: Object.prototype.hasOwnProperty.call(globalThis, 'chrome'),
        hasBrowser: Object.prototype.hasOwnProperty.call(globalThis, 'browser'),
        hasConsole: Object.prototype.hasOwnProperty.call(globalThis, 'console'),
        hasMutationObserver: Object.prototype.hasOwnProperty.call(globalThis, 'MutationObserver'),
        hasAddEventListener: Object.prototype.hasOwnProperty.call(globalThis, 'addEventListener'),
        hasDispatchEvent: Object.prototype.hasOwnProperty.call(globalThis, 'dispatchEvent'),
        hasContentRuntimeHost: Object.prototype.hasOwnProperty.call(globalThis, '__atomContentRuntimeHost'),
        contentRuntimeHost: globalThis.__atomContentRuntimeHost,
    };

    globalThis.document = document;
    globalThis.chrome = {
        runtime: {
            connect() {
                return runtimePort;
            },
            getManifest() {
                return { version: '0.8.7-test' };
            },
            getURL(path) {
                return `chrome-extension://atom-test/${path}`;
            },
        },
    };
    globalThis.browser = undefined;
    globalThis.console = consoleStub;
    globalThis.MutationObserver = undefined;
    globalThis.addEventListener = (type, listener) => {
        addListener(globalListeners, type, listener);
    };
    globalThis.dispatchEvent = (event) => {
        dispatchToStore(globalListeners, event);
        return true;
    };

    function restoreProperty(name, hadProperty, value) {
        if (hadProperty) {
            globalThis[name] = value;
            return;
        }

        delete globalThis[name];
    }

    return {
        postedMessages,
        async bootstrap() {
            await bootstrapContentRuntime();
        },
        receive(message) {
            for (const listener of messageListeners) {
                listener(message);
            }
        },
        addCallbackPayload(payload) {
            const node = createNode();
            node.dataset.atomCallbackPayload = '1';
            node.textContent = JSON.stringify(payload);
            callbackPayloadNodes.push(node);
            return node;
        },
        addCallbackFinalized(payload) {
            const node = createNode();
            node.dataset.atomCallbackFinalized = '1';
            node.textContent = JSON.stringify(payload);
            callbackFinalizedNodes.push(node);
            return node;
        },
        dispatch(eventName) {
            const event = { type: eventName };
            document.dispatchEvent(event);
            globalThis.dispatchEvent(event);
        },
        async flush() {
            await new Promise((resolve) => setTimeout(resolve, 0));
        },
        restore() {
            restoreProperty('__atomContentRuntimeHost', originalState.hasContentRuntimeHost, originalState.contentRuntimeHost);
            restoreProperty('document', originalState.hasDocument, originalState.document);
            restoreProperty('chrome', originalState.hasChrome, originalState.chrome);
            restoreProperty('browser', originalState.hasBrowser, originalState.browser);
            restoreProperty('console', originalState.hasConsole, originalState.console);
            restoreProperty('MutationObserver', originalState.hasMutationObserver, originalState.MutationObserver);
            restoreProperty('addEventListener', originalState.hasAddEventListener, originalState.addEventListener);
            restoreProperty('dispatchEvent', originalState.hasDispatchEvent, originalState.dispatchEvent);
        },
    };
}

function createTabContext(overrides = {}) {
    return {
        sessionId: 'session-1',
        contextId: 'ctx-a',
        tabId: 'tab-1',
        connectedAt: 1,
        isReady: true,
        ...overrides,
    };
}

test('content runtime isolates localStorage and sessionStorage by contextId', () => {
    const context = createScriptSandbox();
    installScript(context, buildStorageIsolationScript());

    context.localStorage.setItem('token', 'alpha');
    context.sessionStorage.setItem('session', 'one');

    assert.equal(context.localStorage.getItem('token'), 'alpha');
    assert.equal(context.sessionStorage.getItem('session'), 'one');
    assert.equal(context.localStorage.length, 1);
    assert.equal(context.localStorage.key(0), 'token');
    assert.equal(context.sessionStorage.length, 1);
    assert.equal(context.sessionStorage.key(0), 'session');

    const localSnapshot = context.localStorage.snapshot();
    const sessionSnapshot = context.sessionStorage.snapshot();
    assert.equal(localSnapshot.get('ctx-a/token'), 'alpha');
    assert.equal(sessionSnapshot.get('ctx-a/session'), 'one');

    context.__atomTabContext.contextId = 'ctx-b';
    assert.equal(context.localStorage.getItem('token'), null);
    assert.equal(context.localStorage.length, 0);
    assert.equal(context.sessionStorage.getItem('session'), null);
    assert.equal(context.sessionStorage.length, 0);

    context.localStorage.setItem('token', 'beta');
    context.sessionStorage.setItem('session', 'two');
    assert.equal(context.localStorage.getItem('token'), 'beta');
    assert.equal(context.sessionStorage.getItem('session'), 'two');

    context.localStorage.clear();
    context.sessionStorage.clear();
    assert.equal(context.localStorage.getItem('token'), null);
    assert.equal(context.sessionStorage.getItem('session'), null);

    context.__atomTabContext.contextId = 'ctx-a';
    assert.equal(context.localStorage.getItem('token'), 'alpha');
    assert.equal(context.sessionStorage.getItem('session'), 'one');
});

test('content runtime prefixes IndexedDB and Cache API names by contextId', async () => {
    const context = createScriptSandbox();
    installScript(context, `${buildIndexedDbIsolationScript()}\n${buildCacheIsolationScript()}`);

    context.indexedDB.open('profile-db', 1);
    context.indexedDB.deleteDatabase('profile-db');
    await context.caches.open('assets');
    await context.caches.has('assets');

    assert.deepEqual(context.indexedDB.opens, [{ name: 'ctx-a/profile-db', version: 1 }]);
    assert.deepEqual(context.indexedDB.deletes, ['ctx-a/profile-db']);
    assert.deepEqual(context.caches.opened, ['ctx-a/assets']);
    assert.deepEqual(context.caches.checked, ['ctx-a/assets']);
    assert.deepEqual(await context.caches.keys(), ['assets']);

    context.__atomTabContext.contextId = 'ctx-b';
    await context.caches.open('assets');
    assert.deepEqual(await context.caches.keys(), ['assets']);
    assert.deepEqual(context.caches.opened, ['ctx-a/assets', 'ctx-b/assets']);
});

test('content runtime изолирует document.cookie и принимает background sync', () => {
    const context = createScriptSandbox();
    installScript(context, buildCookieIsolationScript());

    vm.runInContext("document.cookie = 'session=alpha; path=/'", context);
    assert.equal(vm.runInContext('document.cookie', context), 'session=alpha');

    vm.runInContext(buildCookieSyncScript('session=beta; mode=dark'), context);
    assert.equal(vm.runInContext('document.cookie', context), 'session=beta; mode=dark');

    vm.runInContext("document.cookie = 'session=gone; Max-Age=0; path=/'", context);
    assert.equal(vm.runInContext('document.cookie', context), 'mode=dark');
});

test('main world context installs document.cookie shim and accepts background sync', () => {
    const context = createScriptSandbox();

    vm.runInContext(buildMainWorldContextScript(createTabContext()), context);

    assert.equal(vm.runInContext('typeof globalThis.__atomSyncDocumentCookieHeader', context), 'function');
    assert.equal(vm.runInContext(buildCookieSyncScript('session=beta; mode=dark'), context), 'session=beta; mode=dark');
    assert.equal(vm.runInContext('document.cookie', context), 'session=beta; mode=dark');

    vm.runInContext("document.cookie = 'session=gone; Max-Age=0; path=/'", context);
    assert.equal(vm.runInContext('document.cookie', context), 'mode=dark');
});

test('content runtime возвращает значение expression-скрипта с внутренними точками с запятой', async () => {
    const result = await vm.runInNewContext(
        buildExecuteScriptSource("JSON.stringify((() => { const value = 1; return { value }; })())"),
        { JSON },
    );

    assert.equal(result, '{"value":1}');
});

test('buildExecuteScriptSource сохраняет return для expression-скрипта когда unsafe-eval недоступен', () => {
    const originalFunction = globalThis.Function;
    globalThis.Function = function BlockedFunction() {
        throw new Error('unsafe-eval blocked');
    };

    try {
        assert.equal(buildExecuteScriptSource('document.cookie'), '(async function(){return (document.cookie);})()');
        assert.equal(buildExecuteScriptSource('notify(); refresh();'), '(async function(){notify(); refresh();})()');
    } finally {
        globalThis.Function = originalFunction;
    }
});

test('content runtime подменяет navigator.userAgentData из clientHints в main world', async () => {
    const context = createScriptSandbox();

    vm.runInContext(buildMainWorldContextScript(createTabContext({
        clientHints: {
            brands: [{ brand: 'Atomium', version: '128' }],
            fullVersionList: [{ brand: 'Atomium', version: '128.0.6613.120' }],
            platform: 'Linux',
            platformVersion: '6.8.0',
            mobile: false,
            architecture: 'x86',
            model: 'Desktop',
            bitness: '64',
        },
    })), context);

    const lowEntropy = JSON.parse(vm.runInContext('JSON.stringify(navigator.userAgentData.toJSON())', context));
    const highEntropy = JSON.parse(await vm.runInContext(
        "navigator.userAgentData.getHighEntropyValues(['platformVersion','architecture','bitness','model','uaFullVersion','fullVersionList']).then((value) => JSON.stringify(value))",
        context,
    ));

    assert.equal(vm.runInContext('navigator.userAgentData.platform', context), 'Linux');
    assert.equal(vm.runInContext('navigator.userAgentData.mobile', context), false);
    assert.deepEqual(lowEntropy, {
        brands: [{ brand: 'Atomium', version: '128' }],
        mobile: false,
        platform: 'Linux',
    });
    assert.deepEqual(highEntropy, {
        brands: [{ brand: 'Atomium', version: '128' }],
        mobile: false,
        platform: 'Linux',
        platformVersion: '6.8.0',
        architecture: 'x86',
        bitness: '64',
        model: 'Desktop',
        uaFullVersion: '128.0.6613.120',
        fullVersionList: [{ brand: 'Atomium', version: '128.0.6613.120' }],
    });
});

test('content runtime обновляет navigator.userAgentData при повторном ApplyContext', () => {
    const context = createScriptSandbox();

    vm.runInContext(buildMainWorldContextScript(createTabContext({
        clientHints: {
            brands: [{ brand: 'Atomium', version: '128' }],
            platform: 'Linux',
            mobile: false,
        },
    })), context);

    assert.equal(vm.runInContext('navigator.userAgentData.platform', context), 'Linux');
    assert.equal(vm.runInContext('navigator.userAgentData.mobile', context), false);

    vm.runInContext(buildMainWorldContextScript(createTabContext({
        contextId: 'ctx-b',
        clientHints: {
            brands: [{ brand: 'Atomium Mobile', version: '129' }],
            platform: 'Android',
            mobile: true,
        },
    })), context);

    assert.equal(vm.runInContext('navigator.userAgentData.platform', context), 'Android');
    assert.equal(vm.runInContext('navigator.userAgentData.mobile', context), true);
    assert.deepEqual(
        JSON.parse(vm.runInContext('JSON.stringify(navigator.userAgentData.brands)', context)),
        [{ brand: 'Atomium Mobile', version: '129' }],
    );
});

test('content runtime публикует ready-контекст после ApplyContext даже для not-ready входного состояния', async () => {
    const harness = createContentRuntimeCallbackObserverHarness();

    try {
        await harness.bootstrap();

        const readyContext = await globalThis.__atomContentRuntimeHost.applyContext(createTabContext({
            connectedAt: 123,
            readyAt: undefined,
            isReady: false,
            url: 'https://example.test/next',
        }));
        await globalThis.__atomContentRuntimeHost.channel.emitReady(readyContext);

        let readyMessages = [];
        for (let attempt = 0; attempt < 5; attempt += 1) {
            await harness.flush();
            readyMessages = harness.postedMessages.filter((message) => message?.action === 'ready');
            if (readyMessages.length > 0) {
                break;
            }
        }

        assert.equal(readyMessages.length, 1);
        assert.equal(readyMessages[0].context.isReady, true);
        assert.equal(typeof readyMessages[0].context.readyAt, 'number');
        assert.equal(readyMessages[0].context.connectedAt, 123);
        assert.equal(readyMessages[0].context.url, 'https://example.test/next');
    } finally {
        harness.restore();
    }
});

test('main world callback bridge prefers discovery proxyPort and forwards tabId', () => {
    const sandbox = createMainWorldCallbackSandbox();

    vm.runInContext(buildMainWorldContextScript(createTabContext()), sandbox.context);
    sandbox.dispatchCallback({
        requestId: 'req-1',
        name: 'bridgeCallback',
        args: ['alpha'],
    });

    assert.equal(sandbox.requests.length, 1);
    assert.equal(sandbox.requests[0].url, 'http://127.0.0.1:9443/callback?secret=stable-live-secret');
    assert.equal(sandbox.requests[0].headers['Content-Type'], 'text/plain;charset=UTF-8');
    assert.deepEqual(JSON.parse(sandbox.requests[0].body), {
        requestId: 'req-1',
        tabId: 'tab-1',
        name: 'bridgeCallback',
        args: ['alpha'],
    });
    assert.equal(JSON.parse(sandbox.getResponseNode('req-1').textContent).action, 'continue');
});

test('content runtime relays callback finalized only from dedicated finalized payloads', async () => {
    const harness = createContentRuntimeCallbackObserverHarness();

    try {
        await harness.bootstrap();

        harness.addCallbackPayload({
            name: 'bridgeCallback',
            args: ['alpha'],
            code: 'bridgeCallback("alpha")',
        });
        harness.dispatch('atom-webdriver-callback');
        await harness.flush();

        harness.addCallbackFinalized({
            name: 'bridgeCallback',
        });
        harness.dispatch('atom-webdriver-callback-finalized');
        await harness.flush();

        assert.deepEqual(
            harness.postedMessages
                .filter((message) => message.action === 'event')
                .map((message) => ({ event: message.event, data: message.data })),
            [
                {
                    event: 'Callback',
                    data: {
                        name: 'bridgeCallback',
                        args: ['alpha'],
                        code: 'bridgeCallback("alpha")',
                    },
                },
                {
                    event: 'CallbackFinalized',
                    data: {
                        name: 'bridgeCallback',
                    },
                },
            ],
        );
    } finally {
        harness.restore();
    }
});