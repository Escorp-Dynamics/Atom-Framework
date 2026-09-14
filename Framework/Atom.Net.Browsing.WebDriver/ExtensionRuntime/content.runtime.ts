import type { BridgeMessage, JsonValue, TabContextEnvelope } from './Shared/Protocol';
import { validateTabContextEnvelope } from './Shared/Protocol/TabContextEnvelope';
import { deriveResidentChannelName } from './Shared/Protocol/ResidentChannel';
import { loadRuntimeConfig, validateRuntimeConfig, type RuntimeConfig } from './Shared/Config';
import { BrowserRuntimePortChannel, DeferredContentReadySignal } from './Content/Channel';
import { describeShadowRootMode, getShadowRootOfAnyMode } from './Content/Dom';

type ConsoleMethodName = 'debug' | 'info' | 'log' | 'warn' | 'error';

const consoleMethodNames: ConsoleMethodName[] = ['debug', 'info', 'log', 'warn', 'error'];
const elementRegistry = new Map<string, Element>();
const elementIdRegistry = new WeakMap<Element, string>();
const callbackBridgeEventName = 'atom-webdriver-callback';
const callbackRequestBridgeEventName = 'atom-webdriver-callback-request';
const callbackFinalizedBridgeEventName = 'atom-webdriver-callback-finalized';
const callbackPayloadSelector = 'script[data-atom-callback-payload="1"]';
const callbackRequestSelector = 'script[data-atom-callback-request="1"]';
const callbackFinalizedSelector = 'script[data-atom-callback-finalized="1"]';
const callbackResponseNodePrefix = 'atom-callback-response-';
let elementIdCounter = 0;

type CallbackDecisionAction = 'continue' | 'abort' | 'replace';

interface CallbackRequestPayload {
    readonly requestId: string;
    readonly name: string;
    readonly args?: JsonValue[];
    readonly code?: string;
}

interface CallbackDecisionPayload {
    readonly action: CallbackDecisionAction;
    readonly args?: JsonValue[];
    readonly code?: string;
    readonly debug?: string;
}

interface BlockingBridgePostResult<TResponse> {
    readonly response: TResponse | null;
    readonly debug?: string;
}

interface CallbackFinalizedPayload {
    readonly name: string;
}

class ContentRuntimeHost {
    private readonly channel = new BrowserRuntimePortChannel();
    private readonly readySignal = new DeferredContentReadySignal();
    private readonly detachedFrameElementIds = new Set<string>();
    private readonly claimedCallbackPayloadNodes = new WeakSet<Element>();
    private readonly claimedCallbackFinalizedNodes = new WeakSet<Element>();
    private currentContext: TabContextEnvelope | null = null;
    private bridgeRuntimeConfig: RuntimeConfig | null = null;
    private bridgeRuntimeConfigLoad: Promise<void> | null = null;
    private domContentLoadedPublished = false;
    private pageLoadedPublished = false;

    public async start(): Promise<void> {
        this.channel.subscribeContext(async (context) => {
            const readyContext = await this.applyContext(context);
            await this.channel.emitReady(readyContext);
        });

        this.channel.subscribeCommands(async (message) => {
            await this.handleCommandSafely(message);
        });

        await this.channel.connect();
        await this.ensureBridgeRuntimeConfig();

        this.registerLifecycleObservers();
        this.registerFrameDetachmentObserver();
        this.registerConsoleObserver();
        this.registerCallbackObserver();
        this.publishInitialLifecycleState();
    }

    private registerLifecycleObservers(): void {
        document.addEventListener('DOMContentLoaded', () => {
            void this.publishDomContentLoaded();
        }, { once: true });

        globalThis.addEventListener('load', () => {
            void this.publishPageLoaded();
        }, { once: true });

        globalThis.addEventListener('error', (event) => {
            void this.channel.sendEvent('ScriptError', {
                message: event.message,
                filename: event.filename,
                line: event.lineno,
                column: event.colno,
            });
        });

        globalThis.addEventListener('unhandledrejection', (event) => {
            void this.channel.sendEvent('ScriptError', {
                message: toErrorText(event.reason),
                kind: 'unhandledrejection',
            });
        });
    }

    private registerConsoleObserver(): void {
        const consoleValue = globalThis.console;
        if (consoleValue === undefined) {
            return;
        }

        for (const methodName of consoleMethodNames) {
            const original = consoleValue[methodName].bind(consoleValue);
            consoleValue[methodName] = (...args: unknown[]) => {
                void this.channel.sendEvent('ConsoleMessage', {
                    level: methodName,
                    message: args.map((arg) => toErrorText(arg)).join(' '),
                });
                original(...args);
            };
        }
    }

    private registerCallbackObserver(): void {
        const registerListener = (eventName: string, listener: () => void) => {
            globalThis.addEventListener(eventName, listener);
            document.addEventListener(eventName, listener);
        };

        registerListener(callbackBridgeEventName, () => {
            void this.flushCallbackPayloads();
        });

        registerListener(callbackRequestBridgeEventName, () => {
            this.flushCallbackRequestPayloads();
        });

        registerListener(callbackFinalizedBridgeEventName, () => {
            void this.flushCallbackFinalizedPayloads();
        });
    }

    private async flushCallbackPayloads(): Promise<void> {
        const nodes = Array.from(document.querySelectorAll(callbackPayloadSelector));
        for (const node of nodes) {
            if (!this.tryClaimCallbackNode(node, this.claimedCallbackPayloadNodes)) {
                continue;
            }

            try {
                const payload = JSON.parse(node.textContent || 'null') as {
                    name?: unknown;
                    args?: unknown;
                    code?: unknown;
                } | null;

                if (!payload || typeof payload.name !== 'string' || payload.name.length === 0) {
                    continue;
                }

                const callbackPayload: Record<string, JsonValue> = {
                    name: payload.name,
                };

                if (Array.isArray(payload.args)) {
                    callbackPayload.args = payload.args as JsonValue[];
                }

                if (typeof payload.code === 'string' && payload.code.length > 0) {
                    callbackPayload.code = payload.code;
                }

                await this.channel.sendEvent('Callback', callbackPayload);
            } catch {
            } finally {
                node.remove();
            }
        }
    }

    private flushCallbackRequestPayloads(): void {
        const nodes = Array.from(document.querySelectorAll(callbackRequestSelector));
        for (const node of nodes) {
            try {
                const payload = JSON.parse(node.textContent || 'null') as CallbackRequestPayload | null;
                if (payload === null || typeof payload.requestId !== 'string' || payload.requestId.length === 0 || typeof payload.name !== 'string' || payload.name.length === 0) {
                    continue;
                }

                const decision = this.resolveCallbackDecision(payload);
                this.writeCallbackDecisionPayload(payload.requestId, decision);
            } catch {
            } finally {
                node.remove();
            }
        }
    }

    private async flushCallbackFinalizedPayloads(): Promise<void> {
        const nodes = Array.from(document.querySelectorAll(callbackFinalizedSelector));
        for (const node of nodes) {
            if (!this.tryClaimCallbackNode(node, this.claimedCallbackFinalizedNodes)) {
                continue;
            }

            try {
                const payload = JSON.parse(node.textContent || 'null') as CallbackFinalizedPayload | null;
                if (payload === null || typeof payload.name !== 'string' || payload.name.length === 0) {
                    continue;
                }

                await this.channel.sendEvent('CallbackFinalized', {
                    name: payload.name,
                });
            } catch {
            } finally {
                node.remove();
            }
        }
    }

    private tryClaimCallbackNode(node: Element, claims: WeakSet<Element>): boolean {
        if (claims.has(node)) {
            return false;
        }

        claims.add(node);
        return true;
    }

    private resolveCallbackDecision(payload: CallbackRequestPayload): CallbackDecisionPayload {
        const tabId = this.currentContext?.tabId;
        if (tabId === undefined || tabId.length === 0) {
            return { action: 'continue', debug: 'missing-tab-id' };
        }

        this.bridgeRuntimeConfig = resolvePreferredBridgeRuntimeConfig(this.bridgeRuntimeConfig);
        if (this.bridgeRuntimeConfig === null) {
            return { action: 'continue', debug: 'missing-runtime-config' };
        }

        const result = postBlockingBridgeJson<CallbackDecisionPayload>(
            this.bridgeRuntimeConfig,
            '/callback',
            {
                requestId: payload.requestId,
                tabId,
                name: payload.name,
                args: Array.isArray(payload.args) ? payload.args : [],
                code: typeof payload.code === 'string' && payload.code.length > 0 ? payload.code : undefined,
            },
        );

        if (result.response === null || result.response === undefined) {
            return { action: 'continue', debug: result.debug };
        }

        return normalizeCallbackDecision(result.response);
    }

    private writeCallbackDecisionPayload(requestId: string, payload: CallbackDecisionPayload): void {
        const root = document.documentElement ?? document.head ?? document.body;
        if (root === null) {
            return;
        }

        const responseNodeId = `${callbackResponseNodePrefix}${requestId}`;
        document.getElementById(responseNodeId)?.remove();

        const node = document.createElement('script');
        node.id = responseNodeId;
        node.type = 'application/json';
        node.textContent = JSON.stringify(payload);
        root.appendChild(node);
    }

    private registerFrameDetachmentObserver(): void {
        const observeTarget = document.documentElement ?? document.body;
        if (observeTarget === null || typeof MutationObserver !== 'function') {
            return;
        }

        const observer = new MutationObserver((records) => {
            for (const record of records) {
                for (const removedNode of record.removedNodes) {
                    this.collectDetachedFrames(removedNode);
                }
            }
        });

        observer.observe(observeTarget, { childList: true, subtree: true });
    }

    private collectDetachedFrames(node: Node): void {
        if (!(node instanceof Element)) {
            return;
        }

        if (isFrameElement(node)) {
            this.scheduleFrameDetached(node);
        }

        for (const frameElement of node.querySelectorAll('iframe,frame')) {
            this.scheduleFrameDetached(frameElement);
        }
    }

    private scheduleFrameDetached(element: Element): void {
        const frameElementId = elementIdRegistry.get(element);
        if (frameElementId === undefined || this.detachedFrameElementIds.has(frameElementId)) {
            return;
        }

        scheduleMicrotask(() => {
            if (element.isConnected || this.detachedFrameElementIds.has(frameElementId)) {
                return;
            }

            this.detachedFrameElementIds.add(frameElementId);
            void this.channel.sendEvent('FrameDetached', { frameElementId });
        });
    }

    private publishInitialLifecycleState(): void {
        if (document.readyState === 'interactive' || document.readyState === 'complete') {
            void this.publishDomContentLoaded();
        }

        if (document.readyState === 'complete') {
            void this.publishPageLoaded();
        }
    }

    private async publishDomContentLoaded(): Promise<void> {
        if (this.domContentLoadedPublished) {
            return;
        }

        this.domContentLoadedPublished = true;
        await this.channel.sendEvent('DomContentLoaded', this.createLifecyclePayload());
    }

    private async publishPageLoaded(): Promise<void> {
        if (this.pageLoadedPublished) {
            return;
        }

        this.pageLoadedPublished = true;
        await this.channel.sendEvent('PageLoaded', this.createLifecyclePayload());
        await this.channel.sendEvent('NavigationCompleted', this.createLifecyclePayload());
    }

    private async handleCommand(message: BridgeMessage): Promise<void> {
        switch (message.command) {
            case 'ExecuteScript':
                await this.handleExecuteScript(message);
                return;
            case 'FindElement':
                await this.handleFindElement(message);
                return;
            case 'FindElements':
                await this.handleFindElements(message);
                return;
            case 'GetElementProperty':
                await this.handleGetElementProperty(message);
                return;
            case 'WaitForElement':
                await this.handleWaitForElement(message);
                return;
            case 'CheckShadowRoot':
                await this.handleCheckShadowRoot(message);
                return;
            case 'ResolveElementScreenPoint':
                await this.handleResolveElementScreenPoint(message);
                return;
            case 'DescribeElement':
                await this.handleDescribeElement(message);
                return;
            case 'FocusElement':
                await this.handleFocusElement(message);
                return;
            case 'ScrollElementIntoView':
                await this.handleScrollElementIntoView(message);
                return;
            default:
                await this.respondCommandNotImplemented(message);
                return;
        }
    }

    private async handleCommandSafely(message: BridgeMessage): Promise<void> {
        try {
            await this.handleCommand(message);
        } catch (error) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'Error',
                error: toErrorText(error),
            });
        }
    }

    private async handleExecuteScript(message: BridgeMessage): Promise<void> {
        const payload = readExecuteScriptPayload(message.payload);
        if (payload.elementId !== undefined) {
            const element = elementRegistry.get(payload.elementId);
            if (element === undefined) {
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Error',
                    error: 'Target element not found.',
                });
                return;
            }

            const markerId = `ael${++elementIdCounter}`;
            element.setAttribute('data-atom-el', markerId);

            try {
                const result = await executeScriptWithFallback(
                    this.channel,
                    buildElementExecuteScriptSource(markerId, payload.script),
                    payload.preferPageContextOnNull === true,
                    payload.forcePageContextExecution === true,
                );
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Ok',
                    payload: result,
                });
            } finally {
                element.removeAttribute('data-atom-el');
            }

            return;
        }

        if (payload.shadowHostElementId !== undefined) {
            const host = elementRegistry.get(payload.shadowHostElementId);
            if (host === undefined) {
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Error',
                    error: 'Shadow host element not found.',
                });
                return;
            }

            const shadowRoot = getShadowRootOfAnyMode(host);
            if (shadowRoot === null) {
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Error',
                    error: 'Shadow root not found.',
                });
                return;
            }

            const markerId = `asr${++elementIdCounter}`;
            host.setAttribute('data-atom-sr', markerId);

            try {
                // ★ Закрытый корень виден ТОЛЬКО из мира расширения: `host.shadowRoot` в основном
                // мире страницы отдаёт null по спецификации, и исполняемый там скрипт корень не
                // найдёт. Поэтому для closed-режима исполняем здесь, в изолированном мире, где
                // корень уже получен через chrome.dom.openOrClosedShadowRoot. Открытый корень идёт
                // прежним путём — через основной мир, чтобы скрипт видел глобалы страницы.
                const result = host.shadowRoot === null
                    ? await executeScriptInClosedShadowRoot(shadowRoot, payload.script)
                    : await executeScriptWithFallback(
                        this.channel,
                        buildShadowRootExecuteScriptSource(markerId, payload.script),
                        payload.preferPageContextOnNull === true,
                        payload.forcePageContextExecution === true,
                    );
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Ok',
                    payload: result,
                });
            } finally {
                host.removeAttribute('data-atom-sr');
            }
            return;
        }

        if (payload.frameHostElementId !== undefined) {
            const host = elementRegistry.get(payload.frameHostElementId);
            if (host === undefined) {
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Error',
                    error: 'Frame host element not found.',
                });
                return;
            }

            if (!isFrameHostElement(host) || host.contentWindow === null) {
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Ok',
                    payload: null,
                });
                return;
            }

            if (isFrameOpaqueForMainWorldEval(host)) {
                // Непрозрачный для main-world eval фрейм (кросс-доменный data:/blob:/иной origin): выполнить
                // код внутрь него нельзя. Отвечаем СРАЗУ строковым sentinel-ом 'null' — тем же, что даёт
                // обычный путь для скрипта, разрешившегося в null (GetTitle/GetContent/Evaluate→"null",
                // GetUrl→null) — БЕЗ main-world round-trip. Это критично: round-trip в opaque-фрейм не только
                // не вернёт значение, но и зависает в main-world на том же потоке, что и content-таймеры,
                // поэтому никакой content-side timeout его не спасёт — надо отказать закрыто ДО round-trip.
                // Проверка синхронная по origin атрибута src (Xray-независимая): из isolated world
                // contentDocument.URL кросс-доменного фрейма вводит в заблуждение. find/wait/child-frames для
                // такого фрейма уже fail-closed через resolveSearchRoot.
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Ok',
                    payload: 'null',
                });
                return;
            }

            const markerId = `afh${++elementIdCounter}`;
            host.setAttribute('data-atom-frame', markerId);

            try {
                const result = await executeScriptWithFallback(
                    this.channel,
                    buildFrameExecuteScriptSource(markerId, payload.script),
                    payload.preferPageContextOnNull === true,
                    payload.forcePageContextExecution === true,
                );
                await this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Ok',
                    payload: result,
                });
            } finally {
                host.removeAttribute('data-atom-frame');
            }

            return;
        }

        const result = await executeScriptWithFallback(
            this.channel,
            buildExecuteScriptSource(payload.script),
            payload.preferPageContextOnNull === true,
            payload.forcePageContextExecution === true,
        );
        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: result,
        });
    }

    private async handleFindElement(message: BridgeMessage): Promise<void> {
        const payload = readElementSearchPayload(message.payload);
        const root = resolveSearchRoot(payload);
        if (root === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'Error',
                error: 'Root element not found.',
            });
            return;
        }

        if (root === null) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
            });
            return;
        }

        const element = findSingle(payload.strategy, payload.value, root);
        if (element === null) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
            });
            return;
        }

        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: registerElement(element),
        });
    }

    private async handleFindElements(message: BridgeMessage): Promise<void> {
        const payload = readElementSearchPayload(message.payload);
        const root = resolveSearchRoot(payload);
        if (root === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'Error',
                error: 'Root element not found.',
            });
            return;
        }

        if (root === null) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'Ok',
                payload: [],
            });
            return;
        }

        const matches = payload.allowShadowRootDiscovery === true
            ? findMultipleWithOpenShadowRootDiscovery(payload.strategy, payload.value, root)
            : findMultiple(payload.strategy, payload.value, root);
        const ids = matches.map((element) => registerElement(element));
        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: ids,
        });
    }

    private async handleGetElementProperty(message: BridgeMessage): Promise<void> {
        const payload = readElementPropertyPayload(message.payload);
        const element = elementRegistry.get(payload.elementId);
        if (element === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
                error: 'Элемент не найден в реестре.',
            });
            return;
        }

        let propertyValue: unknown = (element as unknown as Record<string, unknown>)[payload.propertyName];
        if (propertyValue === undefined) {
            propertyValue = element.getAttribute(payload.propertyName);
        }

        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: propertyValue != null ? String(propertyValue) : null,
        });
    }

    private async handleWaitForElement(message: BridgeMessage): Promise<void> {
        const payload = readWaitForElementPayload(message.payload);
        const root = resolveSearchRoot(payload);
        if (root === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'Error',
                error: 'Root element not found.',
            });
            return;
        }

        if (root === null) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
            });
            return;
        }

        // Кандидат, удовлетворяющий виду ожидания. Для Stable требуется два подряд одинаковых
        // положения: одиночный замер не отличает остановившийся элемент от едущего.
        let previousBox: ElementBox | null = null;
        let previousSampleAt = 0;

        const resolveCandidate = (): Element | null => {
            const element = findSingle(payload.strategy, payload.value, root);
            if (element === null) {
                previousBox = null;
                return null;
            }

            if (!payload.requireVisible) {
                return element;
            }

            if (!isElementVisible(element)) {
                previousBox = null;
                return null;
            }

            if (!payload.requireStable) {
                return element;
            }

            const now = typeof performance?.now === 'function' ? performance.now() : 0;
            if (previousBox !== null && now - previousSampleAt < WAIT_FOR_ELEMENT_STABLE_SAMPLE_INTERVAL_MS) {
                return null;
            }

            const box = readElementBox(element);
            const settled = isSameBox(previousBox, box);
            previousBox = box;
            previousSampleAt = now;
            return settled ? element : null;
        };

        const immediate = resolveCandidate();
        if (immediate !== null) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'Ok',
                payload: registerElement(immediate),
            });
            return;
        }

        await new Promise<void>((resolve) => {
            const observeTarget = root === document ? document.documentElement : root;
            if (observeTarget === null) {
                void this.channel.sendResponse({
                    id: message.id,
                    type: 'Response',
                    status: 'Error',
                    error: 'Root element not found.',
                }).finally(resolve);
                return;
            }

            let finished = false;

            const finish = (element: Element | null) => {
                if (finished) {
                    return;
                }

                finished = true;
                observer.disconnect();
                globalThis.clearInterval(pollId);
                globalThis.clearTimeout(timerId);

                void this.channel.sendResponse(element === null
                    ? {
                        id: message.id,
                        type: 'Response',
                        status: 'Timeout',
                        payload: null,
                        error: 'Элемент не появился в течение таймаута.',
                    }
                    : {
                        id: message.id,
                        type: 'Response',
                        status: 'Ok',
                        payload: registerElement(element),
                    }).finally(resolve);
            };

            const attempt = () => {
                const element = resolveCandidate();
                if (element !== null) {
                    finish(element);
                }
            };

            const observer = new MutationObserver(attempt);
            observer.observe(observeTarget, { childList: true, subtree: true, attributes: true });

            // Наблюдатель мутаций не видит ни перекомпоновку, ни анимацию, ни изменение стиля из
            // таблицы стилей — а именно от них зависят видимость и стабильность. Поэтому к нему
            // нужен опрос; для Attached он безвреден, для остальных видов обязателен.
            const pollId = globalThis.setInterval(attempt, WAIT_FOR_ELEMENT_POLL_INTERVAL_MS);
            const timerId = globalThis.setTimeout(() => finish(null), payload.timeoutMs);
        });
    }

    private async handleCheckShadowRoot(message: BridgeMessage): Promise<void> {
        const payload = readElementIdentifierPayload(message.payload, 'Payload проверки shadow root имеет неверную форму');
        const element = elementRegistry.get(payload.elementId);
        if (element === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
                error: 'Element not found.',
            });
            return;
        }

        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: describeShadowRootMode(element),
        });
    }

    private async handleResolveElementScreenPoint(message: BridgeMessage): Promise<void> {
        const payload = readResolveScreenPointPayload(message.payload);
        const element = elementRegistry.get(payload.elementId);
        if (element === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
                error: 'Элемент не найден в реестре.',
            });
            return;
        }

        if (payload.scrollIntoView) {
            element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
            await waitForLayoutMeasurementAsync();
        }

        const rect = element.getBoundingClientRect();
        const viewportX = rect.width > 0 ? rect.left + rect.width / 2 : rect.left;
        const viewportY = rect.height > 0 ? rect.top + rect.height / 2 : rect.top;

        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: {
                viewportX,
                viewportY,
            },
        });
    }

    private async handleDescribeElement(message: BridgeMessage): Promise<void> {
        const payload = readElementIdentifierPayload(message.payload, 'Payload описания элемента имеет неверную форму');
        const element = elementRegistry.get(payload.elementId);
        if (element === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
                error: 'Элемент не найден в реестре.',
            });
            return;
        }

        const rect = typeof element.getBoundingClientRect === 'function'
            ? element.getBoundingClientRect()
            : { left: 0, top: 0, width: 0, height: 0 };

        const computedStyle = typeof globalThis.getComputedStyle === 'function'
            ? globalThis.getComputedStyle(element)
            : null;

        const serializedComputedStyle: Record<string, JsonValue> = {};
        if (computedStyle !== null) {
            for (const propertyName of computedStyle) {
                serializedComputedStyle[propertyName] = computedStyle.getPropertyValue(propertyName) ?? '';
            }
        }

        const isVisible = !!(
            rect.width > 0
            && rect.height > 0
            && computedStyle !== null
            && computedStyle.display !== 'none'
            && computedStyle.visibility !== 'hidden'
            && computedStyle.visibility !== 'collapse'
            && computedStyle.opacity !== '0'
        );

        const options = element instanceof HTMLSelectElement
            ? Array.from(element.options, (option) => ({
                value: option.value ?? '',
                text: option.text ?? '',
            }))
            : [];

        const associatedControlId = element instanceof HTMLLabelElement && element.control instanceof HTMLElement
            ? element.control.id || ''
            : '';

        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: {
                tagName: element.tagName,
                checked: 'checked' in element ? Boolean((element as HTMLInputElement).checked) : false,
                selectedIndex: 'selectedIndex' in element ? Number((element as HTMLSelectElement).selectedIndex) : -1,
                isActive: document.activeElement === element,
                isConnected: element.isConnected,
                isVisible,
                associatedControlId,
                boundingBox: {
                    left: Number(rect.left ?? 0),
                    top: Number(rect.top ?? 0),
                    width: Number(rect.width ?? 0),
                    height: Number(rect.height ?? 0),
                },
                computedStyle: serializedComputedStyle,
                options,
            },
        });
    }

    private async handleFocusElement(message: BridgeMessage): Promise<void> {
        const payload = readFocusElementPayload(message.payload);
        const element = elementRegistry.get(payload.elementId);
        if (element === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
                error: 'Элемент не найден в реестре.',
            });
            return;
        }

        if (payload.scrollIntoView) {
            element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
            await waitForLayoutMeasurementAsync();
        }

        if (element instanceof HTMLElement) {
            element.focus({ preventScroll: true });
        }

        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: {
                isActive: document.activeElement === element,
            },
        });
    }

    private async handleScrollElementIntoView(message: BridgeMessage): Promise<void> {
        const payload = readElementIdentifierPayload(message.payload, 'Payload прокрутки элемента имеет неверную форму');
        const element = elementRegistry.get(payload.elementId);
        if (element === undefined) {
            await this.channel.sendResponse({
                id: message.id,
                type: 'Response',
                status: 'NotFound',
                payload: null,
                error: 'Элемент не найден в реестре.',
            });
            return;
        }

        element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
        await waitForLayoutMeasurementAsync();
        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: null,
        });
    }

    private async handleApplyContext(message: BridgeMessage): Promise<void> {
        const context = validateTabContextEnvelope(message.payload);
        await this.applyContext(context);

        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Ok',
            payload: this.createLifecyclePayload(),
        });
    }

    private createLifecyclePayload(): JsonValue {
        const payload: Record<string, JsonValue> = {
            href: globalThis.location.href,
            readyState: document.readyState,
            title: document.title,
        };

        if (this.currentContext !== null) {
            payload.contextId = this.currentContext.contextId;
            payload.tabId = this.currentContext.tabId;
            payload.windowId = this.currentContext.windowId ?? '';
        }

        return payload;
    }

    private async applyContext(context: TabContextEnvelope): Promise<TabContextEnvelope> {
        const readyContext = this.createReadyContext(context);
        this.currentContext = readyContext;
        this.persistContextInContentWorld(readyContext);
        await this.ensureBridgeRuntimeConfig();
        await this.applyContextInMainWorld(readyContext);
        await this.readySignal.emitReady(readyContext);
        return readyContext;
    }

    private createReadyContext(context: TabContextEnvelope): TabContextEnvelope {
        return {
            ...context,
            isReady: true,
            readyAt: Date.now(),
        };
    }

    private async ensureBridgeRuntimeConfig(): Promise<void> {
        this.bridgeRuntimeConfig = resolvePreferredBridgeRuntimeConfig(this.bridgeRuntimeConfig);
        if (this.bridgeRuntimeConfig !== null) {
            return;
        }

        if (this.bridgeRuntimeConfigLoad !== null) {
            await this.bridgeRuntimeConfigLoad;
            return;
        }

        this.bridgeRuntimeConfigLoad = (async () => {
            this.bridgeRuntimeConfig = resolvePreferredBridgeRuntimeConfig(this.bridgeRuntimeConfig);
            if (this.bridgeRuntimeConfig !== null) {
                return;
            }

            try {
                this.bridgeRuntimeConfig = resolvePreferredBridgeRuntimeConfig(await loadBundledContentRuntimeConfig());
            } catch {
                this.bridgeRuntimeConfig = resolvePreferredBridgeRuntimeConfig(null);
            }
        })();

        try {
            await this.bridgeRuntimeConfigLoad;
        } finally {
            this.bridgeRuntimeConfigLoad = null;
        }
    }

    private persistContextInContentWorld(context: TabContextEnvelope): void {
        const state = globalThis as typeof globalThis & {
            __atomContentRuntimeContext?: TabContextEnvelope;
        };

        state.__atomContentRuntimeContext = context;
    }

    private async applyContextInMainWorld(context: TabContextEnvelope): Promise<void> {
        const script = buildMainWorldContextScript(context);

        // Подчинённые фреймы обслуживает ФОН (см. applyIdentityToAllFrames): контент-скрипт
        // объявлен с `all_frames: false` и до кросс-доменного фрейма не дотягивается, а документ
        // проверки Cloudflare вдобавок запрещает выполнять строку как код (Trusted Types) —
        // туда проходит только внедрение готовой функции средствами браузера.
        try {
            await this.channel.executeInMain(script);
        } catch {
            await executeScriptInPageContext(script);
        }
    }

    private async respondCommandNotImplemented(message: BridgeMessage): Promise<void> {
        await this.channel.sendResponse({
            id: message.id,
            type: 'Response',
            status: 'Error',
            error: `Команда ${message.command ?? 'unknown'} пока не реализована новым content runtime`,
        });
    }
}

export async function bootstrapContentRuntime(): Promise<void> {
    const state = globalThis as typeof globalThis & {
        __atomContentRuntimeHost?: ContentRuntimeHost;
    };

    if (state.__atomContentRuntimeHost !== undefined) {
        return;
    }

    const host = new ContentRuntimeHost();
    state.__atomContentRuntimeHost = host;
    await host.start();
}

function canBootstrapContentRuntime(): boolean {
    const state = globalThis as typeof globalThis & {
        chrome?: { runtime?: unknown };
        browser?: { runtime?: unknown };
    };

    return state.chrome?.runtime !== undefined || state.browser?.runtime !== undefined;
}

if (canBootstrapContentRuntime()) {
    void bootstrapContentRuntime().catch((error) => {
        console.error('[контентный вход] Фатальный сбой bootstrap content runtime', error);
    });
}

function toErrorText(value: unknown): string {
    if (value instanceof Error && value.message.trim().length > 0) {
        return value.message;
    }

    if (typeof value === 'string') {
        return value;
    }

    try {
        return JSON.stringify(value);
    } catch {
        return String(value);
    }
}

function parseOptionalPositivePort(value: string | null | undefined): number | undefined {
    if (typeof value !== 'string' || value.trim().length === 0) {
        return undefined;
    }

    const port = Number.parseInt(value, 10);
    return Number.isInteger(port) && port > 0
        ? port
        : undefined;
}

export interface DiscoveryDocumentEndpointOverride {
    port: number;
    proxyPort?: number;
}

/**
 * Discovery-страница моста публикует фактические порты текущего запуска. Это нужно, когда
 * расширение стартовало с закешированным config.json от прежнего запуска и его порт устарел.
 *
 * Секрет здесь намеренно не читается: он поставляется только внутри пакета расширения
 * (config.json / managed storage). Публикация секрета в DOM сделала бы его доступным любому
 * скрипту на странице моста и не даёт ничего сверх того, что уже есть в пакете.
 */
export function tryReadDiscoveryDocumentEndpointOverride(): DiscoveryDocumentEndpointOverride | null {
    const documentObject = globalThis.document;
    if (typeof documentObject?.querySelector !== 'function') {
        return null;
    }

    const port = parseOptionalPositivePort(
        documentObject.querySelector('meta[name="atom-bridge-port"]')?.getAttribute('content')?.trim());
    if (port === undefined) {
        return null;
    }

    const proxyPort = parseOptionalPositivePort(
        documentObject.querySelector('meta[name="atom-bridge-proxy-port"]')?.getAttribute('content')?.trim());

    return proxyPort === undefined ? { port } : { port, proxyPort };
}

export function resolvePreferredBridgeRuntimeConfig(currentConfig: RuntimeConfig | null): RuntimeConfig | null {
    if (currentConfig === null) {
        return null;
    }

    const override = tryReadDiscoveryDocumentEndpointOverride();
    if (override === null) {
        return currentConfig;
    }

    if (currentConfig.port === override.port
        && (override.proxyPort === undefined || currentConfig.proxyPort === override.proxyPort)) {
        return currentConfig;
    }

    return validateRuntimeConfig({
        ...currentConfig,
        port: override.port,
        proxyPort: override.proxyPort ?? currentConfig.proxyPort,
    });
}

async function loadBundledContentRuntimeConfig(): Promise<RuntimeConfig> {
    const runtime = getContentRuntimeApi();
    const configUrl = runtime.getURL('config.json');

    return await loadRuntimeConfig({
        loadText: async () => {
            try {
                const response = await fetch(configUrl);
                if (!response.ok) {
                    throw new Error('Файл конфигурации runtime недоступен');
                }

                return await response.text();
            } catch {
                return await loadBundledContentRuntimeConfigViaXmlHttpRequest(configUrl);
            }
        },
    });
}

async function loadBundledContentRuntimeConfigViaXmlHttpRequest(configUrl: string): Promise<string> {
    if (typeof XMLHttpRequest !== 'function') {
        throw new Error('Файл конфигурации runtime недоступен');
    }

    return await new Promise<string>((resolve, reject) => {
        const request = new XMLHttpRequest();
        request.open('GET', configUrl, true);

        request.onload = () => {
            if (request.status >= 200 && request.status < 300) {
                resolve(request.responseText);
                return;
            }

            reject(new Error('Файл конфигурации runtime недоступен'));
        };

        request.onerror = () => reject(new Error('Файл конфигурации runtime недоступен'));
        request.onabort = () => reject(new Error('Файл конфигурации runtime недоступен'));

        try {
            request.send();
        } catch {
            reject(new Error('Файл конфигурации runtime недоступен'));
        }
    });
}

function getContentRuntimeApi(): { getURL(path: string): string; getManifest?(): { version?: string } } {
    const runtimeHost = (globalThis as typeof globalThis & {
        browser?: { runtime?: { getURL(path: string): string; getManifest?(): { version?: string } } };
        chrome?: { runtime?: { getURL(path: string): string; getManifest?(): { version?: string } } };
    }).browser ?? (globalThis as typeof globalThis & {
        chrome?: { runtime?: { getURL(path: string): string; getManifest?(): { version?: string } } };
    }).chrome;

    const runtime = runtimeHost?.runtime;
    if (runtime === undefined) {
        throw new Error('Средства выполнения браузера недоступны');
    }

    return runtime;
}

function postBlockingBridgeJson<TResponse>(
    config: RuntimeConfig | null,
    path: string,
    payload: Record<string, unknown>,
): BlockingBridgePostResult<TResponse> {
    if (config === null || typeof XMLHttpRequest !== 'function') {
        return {
            response: null,
            debug: config === null ? 'missing-runtime-config' : 'xmlhttprequest-unavailable',
        };
    }

    const url = createBridgeUtilityUrl(config, path);
    const utilityPort = resolveBridgeUtilityPort(config);

    try {
        const request = new XMLHttpRequest();
        request.open('POST', url, false);
        request.setRequestHeader('Content-Type', 'text/plain;charset=UTF-8');
        request.send(JSON.stringify(payload));

        if (request.status < 200 || request.status >= 300) {
            return {
                response: null,
                debug: `http-status-${request.status}@${config.host}:${utilityPort}`,
            };
        }

        if (typeof request.responseText !== 'string' || request.responseText.trim().length === 0) {
            return {
                response: null,
                debug: `empty-response@${config.host}:${utilityPort}`,
            };
        }

        return {
            response: JSON.parse(request.responseText) as TResponse,
        };
    } catch (error) {
        return {
            response: null,
            debug: `request-error@${config.host}:${utilityPort}:${toErrorText(error)}`,
        };
    }
}

export function resolveBridgeUtilityPort(config: Pick<RuntimeConfig, 'port' | 'proxyPort'>): number {
    return typeof config.proxyPort === 'number' && Number.isInteger(config.proxyPort) && config.proxyPort > 0
        ? config.proxyPort
        : config.port;
}

function createBridgeUtilityUrl(config: RuntimeConfig, path: string): string {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    return `http://${config.host}:${resolveBridgeUtilityPort(config)}${normalizedPath}?secret=${encodeURIComponent(config.secret)}`;
}

function normalizeCallbackDecision(value: CallbackDecisionPayload): CallbackDecisionPayload {
    switch (value.action) {
        case 'abort':
            return { action: 'abort' };
        case 'replace':
            return {
                action: 'replace',
                code: typeof value.code === 'string' && value.code.length > 0 ? value.code : '',
                args: Array.isArray(value.args) ? value.args : undefined,
            };
        default:
            return {
                action: 'continue',
                args: Array.isArray(value.args) ? value.args : undefined,
            };
    }
}

export function buildStorageIsolationScript(): string {
    return `
    let storageIsolationInstalled = false;

    if (!storageIsolationInstalled) {
        const patchedStoragePrototypes = new WeakSet();
        const installStorageIsolation = (storage) => {
            if (!storage) {
                return;
            }

            const proto = Object.getPrototypeOf(storage);
            // ★ Сторож живёт в WeakSet, а НЕ свойством прототипа. Имени '__atomStorageIsolationPatched'
            // в браузере не существует, и оно находилось обычным getOwnPropertyNames(Storage.prototype) —
            // визитка автоматики, заметнее всего, что она прячет.
            if (!proto || patchedStoragePrototypes.has(proto)) {
                return;
            }

            const originalGetItem = typeof proto.getItem === 'function' ? proto.getItem : null;
            const originalSetItem = typeof proto.setItem === 'function' ? proto.setItem : null;
            const originalRemoveItem = typeof proto.removeItem === 'function' ? proto.removeItem : null;
            const originalKey = typeof proto.key === 'function' ? proto.key : null;
            const originalLength = Object.getOwnPropertyDescriptor(proto, 'length')?.get;
            if (!originalGetItem || !originalSetItem || !originalRemoveItem || !originalKey || typeof originalLength !== 'function') {
                return;
            }

            const readStoragePrefix = () => {
                const contextId = readContext().contextId;
                return typeof contextId === 'string' && contextId.length > 0
                    ? contextId + '/'
                    : '';
            };

            const collectVisibleKeys = (target) => {
                const prefix = readStoragePrefix();
                const keys = [];
                const length = Number(originalLength.call(target));
                for (let index = 0; index < length; index += 1) {
                    const key = originalKey.call(target, index);
                    if (typeof key === 'string' && key.startsWith(prefix)) {
                        keys.push(key);
                    }
                }

                return keys;
            };

            patchedStoragePrototypes.add(proto);

            Object.defineProperty(proto, 'getItem', {
                configurable: true,
                value(key) {
                    return originalGetItem.call(this, readStoragePrefix() + String(key));
                },
            });

            Object.defineProperty(proto, 'setItem', {
                configurable: true,
                value(key, value) {
                    return originalSetItem.call(this, readStoragePrefix() + String(key), String(value));
                },
            });

            Object.defineProperty(proto, 'removeItem', {
                configurable: true,
                value(key) {
                    return originalRemoveItem.call(this, readStoragePrefix() + String(key));
                },
            });

            Object.defineProperty(proto, 'key', {
                configurable: true,
                value(index) {
                    const visibleKeys = collectVisibleKeys(this);
                    const key = visibleKeys[Number(index)];
                    return typeof key === 'string'
                        ? key.slice(readStoragePrefix().length)
                        : null;
                },
            });

            Object.defineProperty(proto, 'length', {
                configurable: true,
                get() {
                    return collectVisibleKeys(this).length;
                },
            });

            Object.defineProperty(proto, 'clear', {
                configurable: true,
                value() {
                    for (const key of collectVisibleKeys(this)) {
                        originalRemoveItem.call(this, key);
                    }
                },
            });
        };

        try {
            installStorageIsolation(globalObject.localStorage);
        } catch {
        }

        try {
            installStorageIsolation(globalObject.sessionStorage);
        } catch {
        }

        storageIsolationInstalled = true;
    }
`;
}

export function buildCookieIsolationScript(): string {
    return `
    let cookieIsolationInstalled = false;
    let cookieIsolationState = null;
    const createCookieMap = () => Object.create(null);
    const parseCookieHeader = (header) => {
        const cookies = createCookieMap();
        if (typeof header !== 'string' || header.trim().length === 0) {
            return cookies;
        }

        for (const part of header.split(';')) {
            const item = part.trim();
            if (item.length === 0) {
                continue;
            }

            const separatorIndex = item.indexOf('=');
            if (separatorIndex <= 0) {
                continue;
            }

            const name = item.slice(0, separatorIndex).trim();
            if (name.length === 0) {
                continue;
            }

            cookies[name] = item.slice(separatorIndex + 1).trim();
        }

        return cookies;
    };
    const serializeCookies = (cookies) => Object.entries(cookies)
        .map(([name, value]) => name + '=' + value)
        .join('; ');
    const readCookieState = () => {
        const existingState = cookieIsolationState;
        if (existingState && typeof existingState === 'object') {
            return existingState;
        }

        const state = {
            cookies: createCookieMap(),
            header: '',
        };

        cookieIsolationState = state;
        return state;
    };
    const syncCookieHeader = (header) => {
        const state = readCookieState();
        state.header = typeof header === 'string' ? header : '';
        state.cookies = parseCookieHeader(state.header);
        return serializeCookies(state.cookies);
    };
    const applyCookieMutation = (rawValue) => {
        if (typeof rawValue !== 'string') {
            return;
        }

        const parts = rawValue.split(';');
        const nameValue = parts[0]?.trim() ?? '';
        const separatorIndex = nameValue.indexOf('=');
        if (separatorIndex <= 0) {
            return;
        }

        const name = nameValue.slice(0, separatorIndex).trim();
        const value = nameValue.slice(separatorIndex + 1).trim();
        let shouldDelete = false;

        for (let index = 1; index < parts.length; index += 1) {
            const attribute = parts[index]?.trim() ?? '';
            const lowerAttribute = attribute.toLowerCase();
            if (lowerAttribute.startsWith('max-age=')) {
                const maxAge = Number.parseInt(attribute.slice('max-age='.length), 10);
                if (!Number.isNaN(maxAge) && maxAge <= 0) {
                    shouldDelete = true;
                }
            }

            if (lowerAttribute.startsWith('expires=')) {
                const expiresAt = Date.parse(attribute.slice('expires='.length));
                if (!Number.isNaN(expiresAt) && expiresAt <= Date.now()) {
                    shouldDelete = true;
                }
            }
        }

        const state = readCookieState();
        if (shouldDelete) {
            delete state.cookies[name];
        } else {
            state.cookies[name] = value;
        }

        state.header = serializeCookies(state.cookies);
    };
    const installCookieShim = (target) => {
        if (!target) {
            return;
        }

        try {
            Object.defineProperty(target, 'cookie', {
                configurable: true,
                get() {
                    return serializeCookies(readCookieState().cookies);
                },
                set(value) {
                    applyCookieMutation(String(value));
                },
            });
        } catch {
        }
    };

    syncCookieHeaderImpl = syncCookieHeader;

    if (!cookieIsolationInstalled) {
        installCookieShim(globalObject.Document?.prototype);
        installCookieShim(globalObject.HTMLDocument?.prototype);
        installCookieShim(globalObject.document);
        cookieIsolationInstalled = true;
    }
    `;
}

export function buildCookieSyncScript(cookieHeader: string, sessionId: string): string {
    return `(() => {
const handoff = { cookieHeader: ${JSON.stringify(cookieHeader)}, handled: false, result: null };
try {
    document.dispatchEvent(new CustomEvent(${JSON.stringify(deriveResidentChannelName(sessionId))}, { detail: handoff }));
} catch {}

return typeof handoff.result === 'string' ? handoff.result : '';
})();`;
}

export function buildIndexedDbIsolationScript(): string {
    return `
    let indexedDbIsolationInstalled = false;

    if (!indexedDbIsolationInstalled && globalObject.indexedDB) {
        const indexedDb = globalObject.indexedDB;
        const originalOpen = typeof indexedDb.open === 'function'
            ? indexedDb.open.bind(indexedDb)
            : null;
        const originalDeleteDatabase = typeof indexedDb.deleteDatabase === 'function'
            ? indexedDb.deleteDatabase.bind(indexedDb)
            : null;

        const readIndexedDbPrefix = () => {
            const contextId = readContext().contextId;
            return typeof contextId === 'string' && contextId.length > 0
                ? contextId + '/'
                : '';
        };

        if (originalOpen) {
            Object.defineProperty(indexedDb, 'open', {
                configurable: true,
                value(name, version) {
                    return originalOpen(readIndexedDbPrefix() + String(name), version);
                },
            });
        }

        if (originalDeleteDatabase) {
            Object.defineProperty(indexedDb, 'deleteDatabase', {
                configurable: true,
                value(name) {
                    return originalDeleteDatabase(readIndexedDbPrefix() + String(name));
                },
            });
        }

        indexedDbIsolationInstalled = true;
    }
`;
}

export function buildCacheIsolationScript(): string {
    return `
    let cacheIsolationInstalled = false;

    if (!cacheIsolationInstalled && globalObject.caches) {
        const cacheStorage = globalObject.caches;
        const originalOpen = typeof cacheStorage.open === 'function'
            ? cacheStorage.open.bind(cacheStorage)
            : null;
        const originalDelete = typeof cacheStorage.delete === 'function'
            ? cacheStorage.delete.bind(cacheStorage)
            : null;
        const originalHas = typeof cacheStorage.has === 'function'
            ? cacheStorage.has.bind(cacheStorage)
            : null;
        const originalKeys = typeof cacheStorage.keys === 'function'
            ? cacheStorage.keys.bind(cacheStorage)
            : null;

        const readCachePrefix = () => {
            const contextId = readContext().contextId;
            return typeof contextId === 'string' && contextId.length > 0
                ? contextId + '/'
                : '';
        };

        if (originalOpen) {
            Object.defineProperty(cacheStorage, 'open', {
                configurable: true,
                value(name) {
                    return originalOpen(readCachePrefix() + String(name));
                },
            });
        }

        if (originalDelete) {
            Object.defineProperty(cacheStorage, 'delete', {
                configurable: true,
                value(name) {
                    return originalDelete(readCachePrefix() + String(name));
                },
            });
        }

        if (originalHas) {
            Object.defineProperty(cacheStorage, 'has', {
                configurable: true,
                value(name) {
                    return originalHas(readCachePrefix() + String(name));
                },
            });
        }

        if (originalKeys) {
            Object.defineProperty(cacheStorage, 'keys', {
                configurable: true,
                async value() {
                    const prefix = readCachePrefix();
                    const names = await originalKeys();
                    return Array.isArray(names)
                        ? names
                            .filter((name) => typeof name === 'string' && name.startsWith(prefix))
                            .map((name) => name.slice(prefix.length))
                        : names;
                },
            });
        }

        cacheIsolationInstalled = true;
    }
`;
}

/**
 * Строит скрипт, исполняемый в основном мире страницы.
 *
 * Всё, что попадает в возвращаемую строку, видно любому скрипту сайта — включая комментарии,
 * поэтому пояснения держим здесь, а не внутри шаблона.
 *
 * Решения по callback доставляет ИСКЛЮЧИТЕЛЬНО изолированный мир контент-скрипта
 * (registerCallbackObserver -> flushCallbackRequestPayloads -> resolveCallbackDecision). Прежде
 * тот же путь дублировался в мире страницы, и ради него в window клался объект с секретом и
 * портом моста. Секрет — единственная авторизация utility-эндпоинтов (/callback, /intercept и
 * прочих), поэтому любой скрипт сайта получал полный контроль над сеансом автоматики и мог
 * подделывать либо подавлять её решения. Дубликат удалён: изолированный мир выполняет тот же
 * запрос сам, а странице не остаётся ни учётных данных, ни повода узнать об автоматике.
 */
export interface MainWorldContextScriptOptions {
    /**
     * Собрать скрипт ТОЛЬКО с подменой личности, без изоляции хранилищ (storage/cookie/indexedDB/
     * cache). Такой вариант предназначен для ПОДЧИНЁННЫХ фреймов: подменять личность там
     * обязательно (иначе кросс-доменный фрейм выдаёт настоящую ОС и противоречит и верхнему
     * документу, и заголовкам запросов), а изоляция хранилищ верхнего фрейма туда переноситься
     * не должна — у подчинённого фрейма своё происхождение и свой канал синхронизации кук.
     */
    readonly identityOnly?: boolean;
}

export function buildMainWorldContextScript(
    context: TabContextEnvelope,
    options?: MainWorldContextScriptOptions,
): string {
    const identityOnly = options?.identityOnly === true;
    const serializedContext = JSON.stringify(context);

    return `(() => {
const context = ${serializedContext};
const globalObject = globalThis;
const channelName = ${JSON.stringify(deriveResidentChannelName(context.sessionId))};
const callbackRequestEventName = 'atom-webdriver-callback-request';
const callbackRequestSelector = 'script[data-atom-callback-request="1"]';
const callbackResponseNodePrefix = 'atom-callback-response-';

const handoff = { context, handled: false, result: null };
try {
    globalObject.document?.dispatchEvent(new CustomEvent(channelName, { detail: handoff }));
} catch {}

if (handoff.handled) {
    return;
}

let currentContext = context;
let syncCookieHeaderImpl = null;

try {
    globalObject.document?.addEventListener(channelName, (event) => {
        const detail = event?.detail;
        if (!detail || typeof detail !== 'object') {
            return;
        }

        if (detail.context) {
            currentContext = detail.context;
        }

        if (typeof detail.cookieHeader === 'string' && typeof syncCookieHeaderImpl === 'function') {
            detail.result = syncCookieHeaderImpl(detail.cookieHeader);
        }

        if (detail.wantContext === true) {
            detail.result = currentContext;
        }

        detail.handled = true;
    }, true);
} catch {}

const readContext = () => currentContext;

{
    const state = {
        originalDateTimeFormat: Intl.DateTimeFormat,
        originalUserAgent: globalObject.navigator?.userAgent ?? null,
        originalUserAgentData: globalObject.navigator?.userAgentData ?? null,
        originalAppVersion: globalObject.navigator?.appVersion ?? null,
        originalPlatform: globalObject.navigator?.platform ?? null,
        originalLanguage: globalObject.navigator?.language ?? null,
        originalLanguages: Array.isArray(globalObject.navigator?.languages) ? Array.from(globalObject.navigator.languages) : [],
        originalHardwareConcurrency: globalObject.navigator?.hardwareConcurrency ?? null,
        originalDeviceMemory: globalObject.navigator?.deviceMemory ?? null,
        originalDoNotTrack: globalObject.navigator?.doNotTrack ?? null,
        originalGlobalPrivacyControl: typeof globalObject.navigator?.globalPrivacyControl === 'boolean'
            ? globalObject.navigator.globalPrivacyControl
            : null,
        originalMaxTouchPoints: globalObject.navigator?.maxTouchPoints ?? null,
        originalDevicePixelRatio: globalObject.devicePixelRatio ?? null,
        originalInnerWidth: globalObject.innerWidth ?? null,
        originalInnerHeight: globalObject.innerHeight ?? null,
        originalOuterWidth: globalObject.outerWidth ?? null,
        originalOuterHeight: globalObject.outerHeight ?? null,
    };

    // ★ Обёртка обязана выглядеть родной функцией. Замер показал, что она не выглядела:
    // 'WebGLRenderingContext.prototype.getParameter.toString()' печатал НАШ исходник, у геттера
    // 'platform' имя было пустым вместо 'get platform', а у подменённых методов присутствовал
    // собственный 'prototype', которого у нативных функций не бывает. Любой из этих признаков
    // читается страницей одной строкой и не требует ни таблиц, ни статистики.
    const nativeOrigins = new WeakMap();

    // Подставная функция получает имя и арность оригинала и запоминает сам оригинал: его исходник
    // и отдаётся при чтении 'toString'. Собственного 'prototype' у неё нет по построению — обёртки
    // создаются сокращённой записью метода, у которой прототипа не бывает, ровно как у нативных.
    const markNative = (patched, original, name, length) => {
        ensureToStringMasked();

        try {
            Object.defineProperty(patched, 'name', { configurable: true, value: name });

            if (typeof length === 'number') {
                Object.defineProperty(patched, 'length', { configurable: true, value: length });
            }
        } catch {
        }

        if (typeof original === 'function') {
            try {
                nativeOrigins.set(patched, original);
            } catch {
            }
        }

        return patched;
    };

    // Единственный путь прочитать исходник функции — 'Function.prototype.toString'. Подменяем его
    // ОДИН раз: нашим обёрткам он отдаёт исходник оригинала, всему остальному — обычный результат.
    // Сам он тоже зарегистрирован, поэтому чтение собственного текста его не выдаёт.
    //
    // ★ И ставим его ЛЕНИВО — только когда появилась первая обёртка. Штатный режим без подмены
    // профиля (именно так работает солвер) не должен платить ни одной подменённой функцией:
    // подменять нечего, а лишний перехват — лишняя поверхность.
    let toStringMasked = false;
    const ensureToStringMasked = () => {
        if (toStringMasked) {
            return;
        }

        toStringMasked = true;

        const originalFunctionToString = Function.prototype.toString;
        const toStringHolder = {
            toString() {
                const origin = nativeOrigins.get(this);
                return originalFunctionToString.call(origin === undefined ? this : origin);
            },
        };

        try {
            Object.defineProperty(toStringHolder.toString, 'name', { configurable: true, value: 'toString' });
            Object.defineProperty(toStringHolder.toString, 'length', { configurable: true, value: 0 });
            nativeOrigins.set(toStringHolder.toString, originalFunctionToString);
        } catch {
        }

        try {
            Object.defineProperty(Function.prototype, 'toString', {
                configurable: true,
                writable: true,
                value: toStringHolder.toString,
            });
        } catch {
        }
    };

    // Метод без собственного 'prototype': сокращённая запись в литерале объекта даёт ровно такую
    // функцию, а обычное 'function ...' — нет.
    const asNativeMethod = (name, original, implementation) => {
        const holder = {
            [name](...args) {
                return implementation.apply(this, args);
            },
        };

        return markNative(holder[name], original, name, typeof original === 'function' ? original.length : implementation.length);
    };

    // Геттер аксессора: имя 'get <свойство>' проставляется движком само, исходник берётся у
    // родного геттера того же свойства — тогда и текст, и имя совпадают с настоящими.
    const asNativeGetter = (property, original, getter) => {
        const holder = {
            get [property]() {
                // Чужой получатель обязан ронять геттер так же, как родной атрибут WebIDL:
                // 'Screen.prototype.width.get.call({})' у настоящего браузера бросает TypeError.
                if (typeof original === 'function') original.call(this);

                return getter.call(this);
            },
        };

        return markNative(Object.getOwnPropertyDescriptor(holder, property).get, original, 'get ' + property, 0);
    };

    const defineGetter = (target, property, getter) => {
        if (!target) {
            return;
        }

        try {
            const existing = Object.getOwnPropertyDescriptor(target, property);

            Object.defineProperty(target, property, {
                configurable: true,
                // Атрибуты WebIDL перечислимы; неперечислимое свойство на месте перечислимого
                // отличается от настоящего одним вызовом getOwnPropertyDescriptor.
                enumerable: existing ? existing.enumerable : true,
                get: asNativeGetter(property, existing?.get, getter),
            });
        } catch {
        }
    };

    // Уже ли живое окружение отдаёт ровно то, что мы собирались подставить.
    //
    // Часть профиля браузер применяет САМ: строку агента задаёт '--user-agent', часовой пояс и
    // локаль — переменные окружения процесса. Ставить обёртку поверх уже верного значения — чистый
    // проигрыш: поведение не меняется, а подменённое свойство появляется там, где его можно найти.
    const alreadyMatches = (target, property, getter) => {
        try {
            const desired = getter();

            // Профиль ничего не заявляет — подменять нечего, и обёртка была бы чистым проигрышем:
            // поведение прежнее, а подменённое свойство находится обычным описателем.
            if (desired === undefined || desired === null) {
                return true;
            }

            const current = target[property];

            if (Array.isArray(desired)) {
                return Array.isArray(current)
                    && current.length === desired.length
                    && desired.every((value, index) => value === current[index]);
            }

            return current === desired;
        } catch {
            return false;
        }
    };

    // Атрибуты навигатора в настоящем браузере объявлены НА ПРОТОТИПЕ, а не на самом объекте:
    // 'Object.getOwnPropertyNames(navigator)' у Chrome пуст. Замер показывал там пятнадцать
    // собственных свойств — след, видимый без единой проверки значения. Поэтому подменяем ровно
    // там, где свойство объявлено, и НИКОГДА не добавляем несуществующее: лишний атрибут выдаёт
    // и подмену, и чужой браузер (так на Chrome появлялся firefox-овый 'mozGetUserMedia').
    const defineNavigatorGetter = (property, getter) => {
        const navigatorObject = globalObject.navigator;
        if (navigatorObject && alreadyMatches(navigatorObject, property, getter)) {
            return;
        }

        const prototype = globalObject.Navigator?.prototype ?? globalObject.WorkerNavigator?.prototype;

        if (prototype && Object.getOwnPropertyDescriptor(prototype, property) !== undefined) {
            defineGetter(prototype, property, getter);
            return;
        }

        if (navigatorObject && Object.getOwnPropertyDescriptor(navigatorObject, property) !== undefined) {
            defineGetter(navigatorObject, property, getter);
        }
    };

    const defineWindowGetter = (property, getter) => {
        if (alreadyMatches(globalObject, property, getter)) {
            return;
        }

        // Глобальный объект — исключение из правила о прототипе: по WebIDL у интерфейса с [Global]
        // атрибуты объявляются на самом объекте, и в настоящем браузере они там собственные.
        if (Object.getOwnPropertyDescriptor(globalObject, property) !== undefined) {
            defineGetter(globalObject, property, getter);
            return;
        }

        defineGetter(globalObject.Window?.prototype, property, getter);
    };

    const cloneClientHintBrands = (brands) => Array.isArray(brands)
        ? brands
            .filter((brand) => brand && typeof brand.brand === 'string' && typeof brand.version === 'string')
            .map((brand) => ({
                brand: brand.brand,
                version: brand.version,
            }))
        : [];

    const createUserAgentDataOverride = () => {
        const clientHints = readContext().clientHints;
        if (!clientHints) {
            return state.originalUserAgentData ?? undefined;
        }

        const brands = cloneClientHintBrands(clientHints.brands);
        const fullVersionList = cloneClientHintBrands(clientHints.fullVersionList ?? clientHints.brands);
        const platform = clientHints.platform ?? '';
        const mobile = clientHints.mobile ?? false;
        const platformVersion = clientHints.platformVersion ?? '';
        const architecture = clientHints.architecture ?? '';
        const model = clientHints.model ?? '';
        const bitness = clientHints.bitness ?? '';
        const uaFullVersion = fullVersionList[0]?.version ?? brands[0]?.version ?? '';

        const toLowEntropyValues = () => ({
            brands: cloneClientHintBrands(brands),
            mobile,
            platform,
        });

        return {
            get brands() {
                return cloneClientHintBrands(brands);
            },
            get mobile() {
                return mobile;
            },
            get platform() {
                return platform;
            },
            toJSON() {
                return toLowEntropyValues();
            },
            async getHighEntropyValues(hints) {
                const requestedHints = Array.isArray(hints)
                    ? hints
                    : [];

                const values = toLowEntropyValues();

                if (requestedHints.includes('platformVersion')) {
                    values.platformVersion = platformVersion;
                }

                if (requestedHints.includes('architecture')) {
                    values.architecture = architecture;
                }

                if (requestedHints.includes('model')) {
                    values.model = model;
                }

                if (requestedHints.includes('bitness')) {
                    values.bitness = bitness;
                }

                if (requestedHints.includes('fullVersionList')) {
                    values.fullVersionList = cloneClientHintBrands(fullVersionList);
                }

                if (requestedHints.includes('uaFullVersion')) {
                    values.uaFullVersion = uaFullVersion;
                }

                return values;
            },
        };
    };

    /**
     * Эмулирует область просмотра заявленного устройства внутри вкладки.
     *
     * Размер окна для этого не годится: вкладки одного окна несут разные профили, а окно одно на
     * всех. Поэтому вьюпорт воссоздаётся здесь, и закрыты все пути, которыми страница его читает:
     * innerWidth/innerHeight, documentElement.clientWidth/clientHeight, visualViewport и
     * медиазапросы о ширине. Замер показал, что каждый из них подменяем и что раскладку корня
     * можно сузить до заявленной ширины, иначе width:100% выдаёт настоящие 500 против 412.
     *
     * Метрики окна равны области просмотра: у настоящего телефона отдельного окна нет вовсе.
     */
    const installViewportOverride = () => {
        const resolveViewport = () => {
            const context = readContext();
            const declared = context.viewport ?? context.screen;

            const width = declared?.width;
            const height = declared?.height;

            if (typeof width !== 'number' || width <= 0 || typeof height !== 'number' || height <= 0) {
                return null;
            }

            // Область просмотра не больше доступной области экрана: профиль рабочего стола заявляет
            // вьюпорт 1920x1080 при доступных 1920x1040, а окно вместе с рамкой браузера за экран не
            // выходит. Экран вкладки важнее живого: последний несёт профиль БРАУЗЕРА и для вкладки
            // со своим профилем дал бы чужую доступную область.
            const screen = context.screen ?? globalObject.screen;

            const limit = (value, available) => typeof available === 'number' && available > 0
                ? Math.min(value, available)
                : value;

            return {
                width: limit(width, screen?.availWidth),
                height: limit(height, screen?.availHeight),
            };
        };

        if (resolveViewport() === null) {
            return;
        }

        const elementPrototype = globalObject.Element?.prototype;

        // Экран вкладки: ранний скрипт знает только профиль браузера, и вкладка со своим профилем
        // отдавала бы его экран — то же расхождение, что прежде исправили для видеокарты.
        const screenPrototype = globalObject.Screen?.prototype;

        if (screenPrototype) {
            for (const property of ['width', 'height', 'availWidth', 'availHeight', 'colorDepth', 'pixelDepth']) {
                const original = Object.getOwnPropertyDescriptor(screenPrototype, property)?.get;

                if (original) {
                    defineGetter(screenPrototype, property, function () {
                        return readContext().screen?.[property] ?? original.call(this);
                    });
                }
            }
        }

        // ★ Размеры подменяются ТОЛЬКО в главном документе. У встроенного фрейма своя область
        // просмотра — размер самого фрейма, а не устройства; подмена там ломает его раскладку и
        // выдаёт себя расхождением с размером элемента iframe у родителя. Экран же общий на все
        // фреймы, поэтому его подмена выше остаётся безусловной.
        const isTopLevelDocument = (() => {
            try {
                return globalObject.top === globalObject.self;
            } catch {
                return false;
            }
        })();

        if (!isTopLevelDocument) {
            return;
        }

        // Запасное значение берётся из снимка, а НЕ из самого свойства: читая globalObject.innerWidth
        // внутри его же геттера, подмена вызывала бы саму себя.
        defineWindowGetter('innerWidth', () => resolveViewport()?.width ?? state.originalInnerWidth);

        // Высота области просмотра — окно минус рамка браузера: она совпадает с тем, что заявляет
        // ранний скрипт, иначе два пути подмены разойдутся между собой.
        defineWindowGetter('innerHeight', () => {
            const viewport = resolveViewport();

            return viewport === null
                ? state.originalInnerHeight
                : Math.max(1, viewport.height - (readContext().isMobile === true ? 0 : 87));
        });

        // ★ У НАСТОЛЬНОГО окна между outer и inner всегда стоит рамка браузера: вкладки, адресная
        // строка и полоса прокрутки. Замер показывал out=in=1280x720 — окно ровно по содержимому,
        // чего не бывает ни у одного настоящего браузера, и это читается одним вычитанием.
        // У телефона рамки нет — там окно и область просмотра совпадают честно.
        // Рамка браузера съедает область просмотра ВНУТРИ окна: само окно за экран не выходит.
        const chromeHeight = readContext().isMobile === true ? 0 : 87;

        defineWindowGetter('outerWidth', () => resolveViewport()?.width ?? state.originalOuterWidth);
        defineWindowGetter('outerHeight', () => resolveViewport()?.height ?? state.originalOuterHeight);

        // Размеры элементов объявлены на Element.prototype и читаются ВСЕМИ элементами сразу,
        // поэтому подменяем ровно корневой: у прочих размер обязан остаться настоящим.
        for (const [property, axis] of [['clientWidth', 'width'], ['clientHeight', 'height']]) {
            const original = Object.getOwnPropertyDescriptor(elementPrototype, property)?.get;

            if (!original) {
                continue;
            }

            // Корень сверяется с ЖИВЫМ документом элемента: захваченная ссылка устаревает при
            // смене документа, а у элемента из встроенного фрейма корень вообще другой.
            defineGetter(elementPrototype, property, function () {
                if (this !== this?.ownerDocument?.documentElement || this?.ownerDocument !== globalObject.document) {
                    return original.call(this);
                }

                const viewport = resolveViewport();

                if (viewport === null) {
                    return original.call(this);
                }

                // Раскладка равна области просмотра, а та по высоте — окно минус рамка браузера.
                return axis === 'height'
                    ? Math.max(1, viewport.height - (readContext().isMobile === true ? 0 : 87))
                    : viewport.width;
            });
        }

        // Прокручиваемый размер корня: замер показывал 500 при заявленных 412 — то есть настоящую
        // ширину окна, прочитанную в обход всех подмен выше.
        for (const [property, axis] of [['scrollWidth', 'width'], ['scrollHeight', 'height']]) {
            const original = Object.getOwnPropertyDescriptor(elementPrototype, property)?.get;

            if (!original) {
                continue;
            }

            defineGetter(elementPrototype, property, function () {
                if (this !== this?.ownerDocument?.documentElement || this?.ownerDocument !== globalObject.document) {
                    return original.call(this);
                }

                const declared = resolveViewport()?.[axis];
                const real = original.call(this);

                // Длинная страница прокручивается по-настоящему: занижать её высоту нельзя, иначе
                // прокрутка противоречит содержимому. Заявленный размер — только нижняя граница.
                return typeof declared === 'number' ? Math.max(declared, real) : real;
            });
        }

        const visualViewportPrototype = globalObject.VisualViewport?.prototype;

        if (visualViewportPrototype) {
            for (const [property, axis] of [['width', 'width'], ['height', 'height']]) {
                const original = Object.getOwnPropertyDescriptor(visualViewportPrototype, property)?.get;

                if (original) {
                    defineGetter(visualViewportPrototype, property, function () {
                        return resolveViewport()?.[axis] ?? original.call(this);
                    });
                }
            }
        }

        installWindowPositionOverride();
        installScreenOrientationOverride();
        installViewportMediaOverride(resolveViewport);
        applyViewportLayout(resolveViewport);
    };

    /**
     * Ставит окно в начало экрана.
     *
     * Замер видел screenX и screenLeft равными 20 — смещение окна на рабочем столе. У телефона,
     * который мы заявляем, окно занимает экран целиком и смещения не имеет вовсе.
     */
    const installWindowPositionOverride = () => {
        for (const property of ['screenX', 'screenLeft', 'screenY', 'screenTop']) {
            if (Object.getOwnPropertyDescriptor(globalObject, property) !== undefined) {
                defineGetter(globalObject, property, () => 0);
            }
        }

        const screenObject = globalObject.screen;

        for (const property of ['availLeft', 'availTop']) {
            if (screenObject && property in screenObject) {
                defineGetter(globalObject.Screen?.prototype ?? screenObject, property, () => 0);
            }
        }
    };

    /**
     * Согласует ориентацию с заявленным экраном.
     *
     * Замер на телефонном профиле показывал 'landscape-primary': ориентация считается по НАСТОЯЩЕМУ
     * окну, и вертикальный экран 412x915 сочетался с горизонтальной ориентацией — сочетание,
     * невозможное ни на одном устройстве.
     */
    const installScreenOrientationOverride = () => {
        const prototype = globalObject.ScreenOrientation?.prototype;

        if (!prototype) {
            return;
        }

        const resolveOrientation = () => {
            const screen = readContext().screen;

            if (typeof screen?.width !== 'number' || typeof screen?.height !== 'number') {
                return null;
            }

            return screen.height >= screen.width ? 'portrait-primary' : 'landscape-primary';
        };

        const originalType = Object.getOwnPropertyDescriptor(prototype, 'type')?.get;

        if (originalType) {
            defineGetter(prototype, 'type', function () {
                return resolveOrientation() ?? originalType.call(this);
            });
        }

        const originalAngle = Object.getOwnPropertyDescriptor(prototype, 'angle')?.get;

        if (originalAngle) {
            // Основная ориентация устройства — всегда угол 0, независимо от того, книжная она
            // или альбомная: угол отсчитывается от собственной основной ориентации экрана.
            defineGetter(prototype, 'angle', function () {
                return resolveOrientation() === null ? originalAngle.call(this) : 0;
            });
        }
    };

    /**
     * Согласует медиазапросы о размере с заявленной областью просмотра.
     *
     * Без этого запрос (max-width: 500px) отвечает по настоящему окну, и адаптивная вёрстка
     * расходится с числами, которые страница читает из innerWidth — расхождение виднее самих
     * чисел. Разбирается текст запроса, а matchMedia и объект результата остаются родными.
     */
    const installViewportMediaOverride = (resolveViewport) => {
        const prototype = globalObject.MediaQueryList?.prototype;
        const originalMatches = Object.getOwnPropertyDescriptor(prototype, 'matches')?.get;

        if (!originalMatches) {
            return;
        }

        // 'device-width' у телефона равен ширине области просмотра: отдельного окна там нет.
        const resolveFeature = (part) => {
            const separator = part.indexOf(':');

            if (separator < 0) {
                return undefined;
            }

            const feature = part.slice(part.indexOf('(') + 1, separator).trim();
            const rawValue = part.slice(separator + 1).replace(')', '').trim();
            const match = /^(-?\d+(?:\.\d+)?)px$/.exec(rawValue);
            const viewport = resolveViewport();

            if (!match || viewport === null) {
                return undefined;
            }

            const requested = Number.parseFloat(match[1]);

            switch (feature) {
                case 'width':
                case 'device-width':
                    return viewport.width === requested;
                case 'min-width':
                case 'min-device-width':
                    return viewport.width >= requested;
                case 'max-width':
                case 'max-device-width':
                    return viewport.width <= requested;
                case 'height':
                case 'device-height':
                    return viewport.height === requested;
                case 'min-height':
                case 'min-device-height':
                    return viewport.height >= requested;
                case 'max-height':
                case 'max-device-height':
                    return viewport.height <= requested;
                default:
                    return undefined;
            }
        };

        defineGetter(prototype, 'matches', function () {
            const media = typeof this.media === 'string' ? this.media.trim().toLowerCase() : '';

            if (media.length === 0 || media.indexOf(',') >= 0 || media.indexOf('not ') === 0) {
                return originalMatches.call(this);
            }

            let overridden = false;
            let result = true;

            for (const part of media.split(' and ')) {
                const override = resolveFeature(part);

                if (override === undefined) {
                    continue;
                }

                overridden = true;
                result = result && override;
            }

            return overridden ? result : originalMatches.call(this);
        });
    };

    /**
     * Сужает раскладку страницы до заявленной ширины.
     *
     * ★ Ширина задаётся МАСШТАБОМ ВКЛАДКИ (chrome.tabs.setZoom), а здесь остаётся лишь страховка на
     * случай, когда масштаб не встал: браузер отверг множитель или вкладка ещё не получила его.
     * Правка делается СТИЛЕВЫМ ЛИСТОМ, а не инлайном на <html>: замер показал, что инлайн виден
     * странице напрямую — 'documentElement.getAttribute("style")' печатал
     * 'width: 412px !important; overflow-x: hidden !important', то есть подмена объявляла о себе
     * сама. Лист вставляется в конец head и снимается с documentElement сразу после применения,
     * поэтому в DOM не остаётся ни атрибута, ни узла с нашими правилами.
     */
    const applyViewportLayout = (resolveViewport) => {
        const document = globalObject.document;

        const apply = () => {
            const viewport = resolveViewport();
            const root = document?.documentElement;

            if (viewport === null || !root) {
                return;
            }

            // Раскладка уже сужена масштабом вкладки — вмешиваться незачем.
            if (Math.abs(root.clientWidth - viewport.width) < 2) {
                return;
            }

            try {
                const width = viewport.width + 'px';
                const sheet = new globalObject.CSSStyleSheet();
                sheet.replaceSync(':root{width:' + width + ' !important;max-width:' + width + ' !important;overflow-x:hidden !important}');

                const own = [...document.adoptedStyleSheets, sheet];
                document.adoptedStyleSheets = own;

                // Свой лист прячется из перечисления: иначе он виден странице лишним элементом
                // 'document.adoptedStyleSheets'. Подмена ставится на ПРОТОТИПЕ — там свойство и
                // объявлено; на самом документе она дала бы собственное свойство, которого у
                // настоящего документа нет, и выдала бы себя одним getOwnPropertyNames.
                const visible = own.slice(0, own.length - 1);
                const documentPrototype = globalObject.Document?.prototype;
                const originalAdopted = Object.getOwnPropertyDescriptor(documentPrototype, 'adoptedStyleSheets')?.get;

                if (originalAdopted) {
                    defineGetter(documentPrototype, 'adoptedStyleSheets', function () {
                        return this === document ? visible : originalAdopted.call(this);
                    });
                }
            } catch {
                // Конструируемые листы недоступны — правка не ставится вовсе: инлайн на <html>
                // выдал бы автоматику вернее, чем помогла бы сама правка.
            }
        };

        apply();

        if (document?.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', apply, { once: true });
        }
    };

    /**
     * Подменяет вендора и renderer, которые WebGL сообщает странице.
     *
     * Значения читаются двумя разными путями: обычными VENDOR/RENDERER и немаскированными через
     * расширение WEBGL_debug_renderer_info. Подменять нужно ОБА — расхождение между ними само по себе
     * является признаком подделки. Перехват ставится на прототипах обоих контекстов (WebGL и WebGL2),
     * поэтому работает независимо от того, какой из них запросит страница.
     */
    const installWebGlOverride = () => {
        // Константы расширения WEBGL_debug_renderer_info: они фиксированы спецификацией, а само
        // расширение в контексте может отсутствовать — тогда полагаться на его объект нельзя.
        const UNMASKED_VENDOR = 0x9245;
        const UNMASKED_RENDERER = 0x9246;
        const VENDOR = 0x1f00;
        const RENDERER = 0x1f01;
        const VERSION = 0x1f02;
        const SHADING_LANGUAGE_VERSION = 0x8b8c;

        // Числовые пределы: пара «имя видеокарты — её пределы» известна и проверяется таблицей,
        // поэтому подменять одно без другого хуже, чем не подменять вовсе. Заявляя Apple M1 Pro,
        // мы обязаны отдавать и его размеры, а не размеры настоящего графического стека машины.
        const MAX_TEXTURE_SIZE = 0x0d33;
        const MAX_RENDERBUFFER_SIZE = 0x84e8;
        const MAX_VIEWPORT_DIMS = 0x0d3a;
        const MAX_VARYING_VECTORS = 0x8dfc;
        const MAX_VERTEX_UNIFORM_VECTORS = 0x8dfb;
        const MAX_FRAGMENT_UNIFORM_VECTORS = 0x8dfd;

        const resolveParameterOverride = (parameter) => {
            const limits = readContext().webGlParameters;
            if (!limits) {
                return undefined;
            }

            switch (parameter) {
                case MAX_TEXTURE_SIZE:
                    return limits.maxTextureSize;
                case MAX_RENDERBUFFER_SIZE:
                    return limits.maxRenderbufferSize;
                case MAX_VARYING_VECTORS:
                    return limits.maxVaryingVectors;
                case MAX_VERTEX_UNIFORM_VECTORS:
                    return limits.maxVertexUniformVectors;
                case MAX_FRAGMENT_UNIFORM_VECTORS:
                    return limits.maxFragmentUniformVectors;
                case MAX_VIEWPORT_DIMS:
                    // Настоящий WebGL отдаёт здесь типизированный массив; обычный выдал бы себя
                    // и типом, и поведением при чтении по индексу.
                    return Array.isArray(limits.maxViewportDims) && limits.maxViewportDims.length === 2
                        ? new Int32Array(limits.maxViewportDims)
                        : undefined;
                default:
                    return undefined;
            }
        };

        const resolveOverride = (parameter) => {
            const numeric = resolveParameterOverride(parameter);
            if (numeric !== undefined) {
                return numeric;
            }

            const webGl = readContext().webGl;
            if (!webGl) {
                return undefined;
            }

            switch (parameter) {
                case UNMASKED_VENDOR:
                    return webGl.unmaskedVendor ?? webGl.vendor;
                case UNMASKED_RENDERER:
                    return webGl.unmaskedRenderer ?? webGl.renderer;
                // Маскированные слоты — константы движка, одинаковые на любой машине: имя видеокарты
                // здесь выдавало подмену одной сверкой с 'WebKit'.
                case VENDOR:
                    return 'WebKit';
                case RENDERER:
                    return 'WebKit WebGL';
                case VERSION:
                    return webGl.version;
                case SHADING_LANGUAGE_VERSION:
                    return webGl.shadingLanguageVersion;
                default:
                    return undefined;
            }
        };

        for (const contextName of ['WebGLRenderingContext', 'WebGL2RenderingContext']) {
            const contextConstructor = globalObject[contextName];
            const prototype = contextConstructor?.prototype;
            if (!prototype || typeof prototype.getParameter !== 'function') {
                continue;
            }

            const originalGetParameter = prototype.getParameter;
            prototype.getParameter = asNativeMethod('getParameter', originalGetParameter, function (parameter) {
                const override = resolveOverride(parameter);
                if (override !== undefined) {
                    return override;
                }

                return originalGetParameter.call(this, parameter);
            });
        }
    };

    const installGeolocationOverride = () => {
        const navigatorObject = globalObject.navigator;
        if (!navigatorObject) {
            return;
        }

        const originalGeolocation = navigatorObject.geolocation ?? null;
        const originalGetCurrentPosition = typeof originalGeolocation?.getCurrentPosition === 'function'
            ? originalGeolocation.getCurrentPosition.bind(originalGeolocation)
            : null;
        const originalWatchPosition = typeof originalGeolocation?.watchPosition === 'function'
            ? originalGeolocation.watchPosition.bind(originalGeolocation)
            : null;
        const originalClearWatch = typeof originalGeolocation?.clearWatch === 'function'
            ? originalGeolocation.clearWatch.bind(originalGeolocation)
            : null;
        let nextSyntheticWatchId = 0;

        const buildSyntheticPosition = () => {
            const geolocation = readContext().geolocation;
            if (!geolocation) {
                return null;
            }

            const accuracy = typeof geolocation.accuracy === 'number' && Number.isFinite(geolocation.accuracy)
                ? geolocation.accuracy
                : 10;

            return {
                coords: {
                    latitude: geolocation.latitude,
                    longitude: geolocation.longitude,
                    accuracy,
                    altitude: null,
                    altitudeAccuracy: null,
                    heading: null,
                    speed: null,
                },
                timestamp: Date.now(),
            };
        };

        const notifyUnavailable = (errorCallback) => {
            if (typeof errorCallback === 'function') {
                globalObject.setTimeout(() => errorCallback({
                    code: 1,
                    message: 'Geolocation override is unavailable.',
                }), 1);
            }
        };

        const fakeGeolocation = {
            getCurrentPosition(successCallback, errorCallback, options) {
                const position = buildSyntheticPosition();
                if (position) {
                    if (typeof successCallback === 'function') {
                        globalObject.setTimeout(() => successCallback(position), 1);
                    }

                    return;
                }

                if (originalGetCurrentPosition) {
                    return originalGetCurrentPosition(successCallback, errorCallback, options);
                }

                notifyUnavailable(errorCallback);
            },
            watchPosition(successCallback, errorCallback, options) {
                const position = buildSyntheticPosition();
                if (position) {
                    const watchId = ++nextSyntheticWatchId;
                    if (typeof successCallback === 'function') {
                        globalObject.setTimeout(() => successCallback(position), 1);
                    }

                    return watchId;
                }

                if (originalWatchPosition) {
                    return originalWatchPosition(successCallback, errorCallback, options);
                }

                notifyUnavailable(errorCallback);
                return 0;
            },
            clearWatch(watchId) {
                if (originalClearWatch) {
                    return originalClearWatch(watchId);
                }
            },
        };

        defineNavigatorGetter('geolocation', () => readContext().geolocation ? fakeGeolocation : originalGeolocation);
    };

${identityOnly ? '' : buildStorageIsolationScript()}

${identityOnly ? '' : buildCookieIsolationScript()}

${identityOnly ? '' : buildIndexedDbIsolationScript()}

${identityOnly ? '' : buildCacheIsolationScript()}

    if (typeof syncCookieHeaderImpl === 'function') {
        syncCookieHeaderImpl('');
    }

    defineNavigatorGetter('userAgent', () => readContext().userAgent ?? state.originalUserAgent);
    defineNavigatorGetter('appVersion', () => {
        const userAgent = readContext().userAgent ?? state.originalUserAgent;
        if (typeof userAgent === 'string' && userAgent.startsWith('Mozilla/')) {
            return userAgent.slice('Mozilla/'.length);
        }

        return state.originalAppVersion;
    });
    defineNavigatorGetter('userAgentData', () => createUserAgentDataOverride());
    defineNavigatorGetter('platform', () => readContext().platform ?? state.originalPlatform);
    defineNavigatorGetter('language', () => readContext().locale ?? readContext().languages?.[0] ?? state.originalLanguage);
    defineNavigatorGetter('languages', () => Array.from(readContext().languages ?? state.originalLanguages));
    defineNavigatorGetter('hardwareConcurrency', () => readContext().hardwareConcurrency ?? state.originalHardwareConcurrency);
    defineNavigatorGetter('deviceMemory', () => readContext().deviceMemory ?? state.originalDeviceMemory);
    defineNavigatorGetter('doNotTrack', () => readContext().doNotTrack !== undefined ? (readContext().doNotTrack ? '1' : '0') : state.originalDoNotTrack);
    defineNavigatorGetter('globalPrivacyControl', () => readContext().globalPrivacyControl ?? state.originalGlobalPrivacyControl);
    defineNavigatorGetter('maxTouchPoints', () => readContext().maxTouchPoints ?? state.originalMaxTouchPoints);

    // ★ Подмена ставится БЕЗУСЛОВНО, минуя сверку «уже совпадает». Масштаб вкладки, которым
    // сужается область просмотра, ДЕЛИТ devicePixelRatio: замер при заявленном dpr=1 показывал
    // 0.4755 — значение, невозможное ни у одного устройства. Сверка же видела совпадение ДО
    // масштабирования и подмену пропускала, оставляя странице расхождение.
    defineGetter(globalObject, 'devicePixelRatio', () => readContext().deviceScaleFactor ?? state.originalDevicePixelRatio);

    // ★ Область просмотра эмулируется ЗДЕСЬ, на уровне вкладки, а не размером окна. Размером окна
    // это нерешаемо в принципе: вкладки одного окна несут разные профили, а Chromium вдобавок не
    // сужает окно ниже примерно 500 точек — мобильный профиль в 412 недостижим физически.
    installViewportOverride();

    // ★ Подмена WebGL ставится ЗДЕСЬ, а не только ранним скриптом личности. Ранний скрипт несёт
    // профиль БРАУЗЕРА, запечённый при его запуске, и для вкладки со своим профилем отдавал чужую
    // видеокарту: замер показывал 'NVIDIA GeForce RTX 3070' во вкладке, заявляющей Apple M1 Pro.
    // Этот же перехват читает контекст ВКЛАДКИ и потому верен и при своём профиле на вкладку, и
    // при смене профиля на лету. Раньше функция была объявлена, но не вызывалась ни разу.
    installWebGlOverride();

    // Мобильный браузер не имеет плагинов вовсе — ни Chrome на Android, ни Safari на iOS. Для
    // вкладки со своим мобильным профилем это должно решаться здесь: ранний скрипт знает только
    // профиль браузера и на настольном профиле оставлял пять записей PDF-просмотрщика.
    if (readContext().isMobile === true) {
        const emptyList = (name) => {
            const listConstructor = globalObject[name];
            const list = listConstructor?.prototype ? Object.create(listConstructor.prototype) : [];

            try {
                Object.defineProperty(list, 'length', { configurable: true, get: () => 0 });
                list.item = asNativeMethod('item', listConstructor?.prototype?.item, () => null);
                list.namedItem = asNativeMethod('namedItem', listConstructor?.prototype?.namedItem, () => null);
                list.refresh = asNativeMethod('refresh', listConstructor?.prototype?.refresh, () => undefined);
            } catch {
            }

            return list;
        };

        const emptyPlugins = emptyList('PluginArray');
        const emptyMimeTypes = emptyList('MimeTypeArray');

        defineNavigatorGetter('plugins', () => emptyPlugins);
        defineNavigatorGetter('mimeTypes', () => emptyMimeTypes);
    }

    installGeolocationOverride();

    const mediaDevicesObject = globalObject.navigator?.mediaDevices;
    if (mediaDevicesObject) {
        const originalEnumerateDevices = typeof mediaDevicesObject.enumerateDevices === 'function'
            ? mediaDevicesObject.enumerateDevices.bind(mediaDevicesObject)
            : null;
        const originalGetUserMedia = typeof mediaDevicesObject.getUserMedia === 'function'
            ? mediaDevicesObject.getUserMedia.bind(mediaDevicesObject)
            : null;
        const originalLegacyGetUserMedia = typeof globalObject.navigator?.getUserMedia === 'function'
            ? globalObject.navigator.getUserMedia.bind(globalObject.navigator)
            : typeof globalObject.navigator?.webkitGetUserMedia === 'function'
                ? globalObject.navigator.webkitGetUserMedia.bind(globalObject.navigator)
                : typeof globalObject.navigator?.mozGetUserMedia === 'function'
                    ? globalObject.navigator.mozGetUserMedia.bind(globalObject.navigator)
                    : null;
        const readVirtualMediaConfig = () => readContext().virtualMediaDevices;
        const readRequestedDeviceId = (request) => {
            if (request === undefined || request === null) {
                return undefined;
            }

            if (typeof request === 'string' && request.length > 0) {
                return request;
            }

            if (Array.isArray(request)) {
                for (const item of request) {
                    const nestedDeviceId = readRequestedDeviceId(item);
                    if (typeof nestedDeviceId === 'string' && nestedDeviceId.length > 0) {
                        return nestedDeviceId;
                    }
                }

                return undefined;
            }

            if (typeof request !== 'object') {
                return undefined;
            }

            if (request.exact !== undefined && request.exact !== null) {
                return readRequestedDeviceId(request.exact);
            }

            if (request.ideal !== undefined && request.ideal !== null) {
                return readRequestedDeviceId(request.ideal);
            }

            if (request.deviceId !== undefined && request.deviceId !== null) {
                return readRequestedDeviceId(request.deviceId);
            }

            if (request.sourceId !== undefined && request.sourceId !== null) {
                return readRequestedDeviceId(request.sourceId);
            }

            if (request.mandatory && typeof request.mandatory === 'object') {
                const mandatoryDeviceId = readRequestedDeviceId(request.mandatory.deviceId)
                    ?? readRequestedDeviceId(request.mandatory.sourceId);
                if (typeof mandatoryDeviceId === 'string' && mandatoryDeviceId.length > 0) {
                    return mandatoryDeviceId;
                }
            }

            if (Array.isArray(request.optional)) {
                for (const item of request.optional) {
                    const optionalDeviceId = readRequestedDeviceId(item);
                    if (typeof optionalDeviceId === 'string' && optionalDeviceId.length > 0) {
                        return optionalDeviceId;
                    }
                }
            }

            return undefined;
        };
        const normalizeLabel = (value) => typeof value === 'string'
            ? value.trim().toLowerCase()
            : '';
        const targetsVirtualDevice = (request, visibleDeviceId, enabled) => {
            if (!enabled || request === false || request === undefined || request === null) {
                return false;
            }

            if (request === true) {
                return true;
            }

            if (typeof request !== 'object') {
                return true;
            }

            const requestedDeviceId = readRequestedDeviceId(request);
            if (requestedDeviceId === undefined || requestedDeviceId === 'default' || typeof visibleDeviceId !== 'string' || visibleDeviceId.length === 0) {
                return true;
            }

            return requestedDeviceId === visibleDeviceId;
        };
        const rewriteRequestedKind = (request, actualDeviceId) => {
            if (request === true || request === undefined || request === null) {
                return { deviceId: { exact: actualDeviceId } };
            }

            if (typeof request !== 'object') {
                return request;
            }

            const rewritten = Object.assign({}, request);
            rewritten.deviceId = { exact: actualDeviceId };
            return rewritten;
        };
        const resolveRequestedNumber = (value, fallbackValue) => {
            const number = Number(value);
            return Number.isFinite(number) && number > 0
                ? number
                : fallbackValue;
        };
        const resolveVideoSetting = (request, key, fallbackValue) => {
            if (request === undefined || request === null || request === true || request === false || typeof request !== 'object') {
                return fallbackValue;
            }

            const descriptor = request[key];
            if (descriptor === undefined || descriptor === null) {
                return fallbackValue;
            }

            if (typeof descriptor === 'number' || typeof descriptor === 'string') {
                return resolveRequestedNumber(descriptor, fallbackValue);
            }

            if (Array.isArray(descriptor)) {
                for (const item of descriptor) {
                    const nestedValue = resolveVideoSetting({ value: item }, 'value', fallbackValue);
                    if (nestedValue !== fallbackValue) {
                        return nestedValue;
                    }
                }

                return fallbackValue;
            }

            if (typeof descriptor === 'object') {
                if (descriptor.exact !== undefined && descriptor.exact !== null) {
                    return resolveRequestedNumber(Array.isArray(descriptor.exact) ? descriptor.exact[0] : descriptor.exact, fallbackValue);
                }

                if (descriptor.ideal !== undefined && descriptor.ideal !== null) {
                    return resolveRequestedNumber(Array.isArray(descriptor.ideal) ? descriptor.ideal[0] : descriptor.ideal, fallbackValue);
                }

                if (descriptor.min !== undefined && descriptor.min !== null) {
                    return resolveRequestedNumber(descriptor.min, fallbackValue);
                }

                if (descriptor.max !== undefined && descriptor.max !== null) {
                    return resolveRequestedNumber(descriptor.max, fallbackValue);
                }
            }

            return fallbackValue;
        };
        const mergeStreams = (primary, secondary) => {
            if (!primary) {
                return secondary;
            }

            if (!secondary) {
                return primary;
            }

            const combined = new MediaStream();
            primary.getTracks().forEach((track) => combined.addTrack(track));
            secondary.getTracks().forEach((track) => combined.addTrack(track));
            return combined;
        };
        const stopStream = (stream) => {
            if (!stream || typeof stream.getTracks !== 'function') {
                return;
            }

            stream.getTracks().forEach((track) => {
                try {
                    track.stop();
                } catch {
                }
            });
        };
        const createSyntheticVideoStream = (request) => {
            const documentObject = globalObject.document;
            if (!documentObject || typeof documentObject.createElement !== 'function') {
                throw new DOMException('Synthetic video stream is unavailable.', 'NotSupportedError');
            }

            const width = Math.max(2, Math.round(resolveVideoSetting(request, 'width', 640)));
            const height = Math.max(2, Math.round(resolveVideoSetting(request, 'height', 480)));
            const frameRate = Math.min(60, Math.max(1, resolveVideoSetting(request, 'frameRate', 30)));
            const canvas = documentObject.createElement('canvas');
            canvas.width = width;
            canvas.height = height;

            if (typeof canvas.captureStream !== 'function') {
                throw new DOMException('Synthetic video stream is unavailable.', 'NotSupportedError');
            }

            const canvasContext = canvas.getContext('2d');
            let animationHandle = 0;
            const startedAt = typeof globalObject.performance?.now === 'function'
                ? globalObject.performance.now()
                : Date.now();
            const drawFrame = () => {
                if (!canvasContext) {
                    return;
                }

                const now = typeof globalObject.performance?.now === 'function'
                    ? globalObject.performance.now()
                    : Date.now();
                const elapsed = (now - startedAt) / 1000;
                const label = readVirtualMediaConfig()?.videoInputLabel ?? 'Virtual Camera';
                canvasContext.fillStyle = '#0f172a';
                canvasContext.fillRect(0, 0, width, height);
                canvasContext.fillStyle = '#38bdf8';
                canvasContext.fillRect(0, 0, width, Math.floor(height * 0.18));
                canvasContext.fillStyle = '#e2e8f0';
                canvasContext.fillRect(Math.floor(width * 0.08), Math.floor(height * 0.24), Math.floor(width * 0.84), Math.floor(height * 0.52));
                canvasContext.fillStyle = '#0f172a';
                canvasContext.font = Math.max(18, Math.floor(height * 0.08)).toString() + 'px sans-serif';
                canvasContext.fillText(label, Math.max(12, Math.floor(width * 0.06)), Math.max(28, Math.floor(height * 0.14)));
                canvasContext.fillStyle = '#2563eb';
                const pulseWidth = Math.max(24, Math.floor(width * 0.18));
                const travel = Math.max(1, width - pulseWidth - Math.floor(width * 0.12));
                const offset = (Math.sin(elapsed * 2.1) + 1) * 0.5 * travel;
                canvasContext.fillRect(Math.floor(width * 0.06 + offset), Math.floor(height * 0.68), pulseWidth, Math.max(12, Math.floor(height * 0.08)));
                animationHandle = typeof globalObject.requestAnimationFrame === 'function'
                    ? globalObject.requestAnimationFrame(drawFrame)
                    : globalObject.setTimeout(drawFrame, Math.max(16, Math.floor(1000 / frameRate)));
            };

            drawFrame();

            const stream = canvas.captureStream(frameRate);
            const videoTrack = typeof stream.getVideoTracks === 'function'
                ? stream.getVideoTracks()[0] ?? null
                : null;
            if (videoTrack && typeof videoTrack.stop === 'function') {
                const originalStop = videoTrack.stop.bind(videoTrack);
                videoTrack.stop = () => {
                    if (animationHandle) {
                        if (typeof globalObject.cancelAnimationFrame === 'function') {
                            globalObject.cancelAnimationFrame(animationHandle);
                        } else {
                            globalObject.clearTimeout(animationHandle);
                        }
                    }

                    return originalStop();
                };
            }

            return stream;
        };
        const buildVirtualDeviceEntry = (kind, deviceId, label, groupId) => Object.freeze({
            kind,
            deviceId,
            label,
            groupId,
            toJSON() {
                return {
                    kind,
                    deviceId,
                    label,
                    groupId,
                };
            },
        });
        const buildVirtualDevices = () => {
            const config = readVirtualMediaConfig();
            if (!config) {
                return null;
            }

            const devices = [];
            const groupId = typeof config.groupId === 'string' && config.groupId.length > 0
                ? config.groupId
                : 'atom-virtual-media';

            if (config.audioInputEnabled !== false && typeof config.audioInputLabel === 'string' && config.audioInputLabel.length > 0) {
                devices.push(buildVirtualDeviceEntry(
                    'audioinput',
                    typeof config.audioInputBrowserDeviceId === 'string' && config.audioInputBrowserDeviceId.length > 0
                        ? config.audioInputBrowserDeviceId
                        : 'audioinput-' + groupId,
                    config.audioInputLabel,
                    groupId,
                ));
            }

            if (config.videoInputEnabled !== false && typeof config.videoInputLabel === 'string' && config.videoInputLabel.length > 0) {
                devices.push(buildVirtualDeviceEntry(
                    'videoinput',
                    typeof config.videoInputBrowserDeviceId === 'string' && config.videoInputBrowserDeviceId.length > 0
                        ? config.videoInputBrowserDeviceId
                        : 'videoinput-' + groupId,
                    config.videoInputLabel,
                    groupId,
                ));
            }

            if (config.audioOutputEnabled === true && typeof config.audioOutputLabel === 'string' && config.audioOutputLabel.length > 0) {
                devices.push(buildVirtualDeviceEntry(
                    'audiooutput',
                    'audiooutput-' + groupId,
                    config.audioOutputLabel,
                    groupId,
                ));
            }

            return devices;
        };
        const findNativeDeviceByLabel = (devices, kind, label) => {
            const expectedLabel = normalizeLabel(label);
            if (!Array.isArray(devices) || expectedLabel.length === 0) {
                return null;
            }

            for (const device of devices) {
                if (device
                    && device.kind === kind
                    && typeof device.deviceId === 'string'
                    && device.deviceId.length > 0
                    && normalizeLabel(device.label) === expectedLabel) {
                    return device.deviceId;
                }
            }

            return null;
        };
        const mediaOperationTimeoutMs = 1500;
        const enumerateNativeDevices = async () => {
            if (!originalEnumerateDevices) {
                return [];
            }

            let timeoutHandle = null;
            const enumeratePromise = originalEnumerateDevices()
                .then((devices) => Array.isArray(devices) ? devices : [])
                .catch(() => []);

            try {
                return await Promise.race([
                    enumeratePromise,
                    new Promise((resolve) => {
                        timeoutHandle = globalObject.setTimeout(() => resolve([]), mediaOperationTimeoutMs);
                    }),
                ]);
            } finally {
                if (timeoutHandle !== null) {
                    globalObject.clearTimeout(timeoutHandle);
                }
            }
        };
        const resolveActualDeviceId = async (kind, label, fallbackVisibleId, enabled) => {
            const cachedDeviceId = state.resolvedMediaDeviceIds?.[kind];
            if (typeof cachedDeviceId === 'string' && cachedDeviceId.length > 0) {
                return cachedDeviceId;
            }

            const resolvedMediaDeviceIds = state.resolvedMediaDeviceIds ?? (state.resolvedMediaDeviceIds = {
                audioinput: null,
                videoinput: null,
            });
            const configuredDeviceId = typeof fallbackVisibleId === 'string' && fallbackVisibleId.length > 0
                ? fallbackVisibleId
                : null;
            let devices = await enumerateNativeDevices();
            let actualDeviceId = findNativeDeviceByLabel(devices, kind, label) ?? configuredDeviceId;

            // Видео всегда имеет синтетический canvas-fallback (createSyntheticVideoStream), поэтому
            // НИКОГДА не блокируем на нативном camera-warmup: в headless/automation Firefox
            // (secure-контекст 127.0.0.1, без авто-разрешения камеры и без fake-stream pref) нативный
            // getUserMedia({video}) может висеть бесконечно, из-за чего переопределённый getUserMedia
            // истекает по таймауту, так и не дойдя до синтетики. Для videoinput с пустым device id
            // (синтетика) сразу оставляем actualDeviceId=null → useSyntheticVideo. Реальная OS-камера
            // имеет непустой DeviceIdentifier → configuredDeviceId уже задан выше, и этот блок не нужен.
            //
            // Для аудио синтетики нет, warmup нужен (после выдачи разрешения enumerateDevices раскроет
            // label), но time-box'им его, чтобы нативный вызов не мог застопорить нас дольше
            // mediaOperationTimeoutMs.
            if (!actualDeviceId && enabled && originalGetUserMedia && kind === 'audioinput') {
                let warmupTimeout = null;
                const warmupPromise = originalGetUserMedia({ audio: true }).then(
                    (stream) => stream,
                    () => null,
                );
                let warmupStream = null;
                try {
                    warmupStream = await Promise.race([
                        warmupPromise,
                        new Promise((resolve) => {
                            warmupTimeout = globalObject.setTimeout(() => resolve(null), mediaOperationTimeoutMs);
                        }),
                    ]);
                } finally {
                    if (warmupTimeout !== null) {
                        globalObject.clearTimeout(warmupTimeout);
                    }
                }

                if (warmupStream !== null) {
                    stopStream(warmupStream);
                } else {
                    // Таймаут выиграл гонку: если нативный вызов разрешится позже — всё равно стопаем
                    // поток, чтобы не оставить микрофон захваченным.
                    void warmupPromise.then((late) => {
                        if (late !== null) {
                            stopStream(late);
                        }
                    });
                }

                devices = await enumerateNativeDevices();
                actualDeviceId = findNativeDeviceByLabel(devices, kind, label) ?? configuredDeviceId;
            }

            resolvedMediaDeviceIds[kind] = actualDeviceId ?? null;
            return actualDeviceId ?? null;
        };

        if (originalEnumerateDevices) {
            mediaDevicesObject.enumerateDevices = async () => {
                const virtualDevices = buildVirtualDevices();
                return virtualDevices ?? await enumerateNativeDevices();
            };
        }

        const invokeOriginalGetUserMedia = originalGetUserMedia
            ? (constraints) => originalGetUserMedia(constraints)
            : originalLegacyGetUserMedia
                ? (constraints) => new Promise((resolve, reject) => {
                    originalLegacyGetUserMedia.call(globalObject.navigator, constraints, resolve, reject);
                })
                : null;

        if (invokeOriginalGetUserMedia) {
            mediaDevicesObject.getUserMedia = async (constraints) => {
                const config = readVirtualMediaConfig();
                if (!config) {
                    return await invokeOriginalGetUserMedia(constraints);
                }

                const request = constraints && typeof constraints === 'object' ? constraints : {};
                const wantsAudio = targetsVirtualDevice(request.audio, config.audioInputBrowserDeviceId, config.audioInputEnabled !== false);
                const wantsVideo = targetsVirtualDevice(request.video, config.videoInputBrowserDeviceId, config.videoInputEnabled !== false);

                if (!wantsAudio && !wantsVideo) {
                    return await invokeOriginalGetUserMedia(constraints);
                }

                const actualAudioDeviceId = wantsAudio
                    ? await resolveActualDeviceId('audioinput', config.audioInputLabel, config.audioInputBrowserDeviceId, config.audioInputEnabled !== false)
                    : null;
                const actualVideoDeviceId = wantsVideo
                    ? await resolveActualDeviceId('videoinput', config.videoInputLabel, config.videoInputBrowserDeviceId, config.videoInputEnabled !== false)
                    : null;
                const useSyntheticVideo = wantsVideo && !actualVideoDeviceId;

                if (wantsAudio && !actualAudioDeviceId) {
                    throw new DOMException('Virtual audio input is not available.', 'NotFoundError');
                }

                const rewritten = Object.assign({}, request);
                let usesNativeConstraints = false;

                if (wantsAudio && actualAudioDeviceId) {
                    rewritten.audio = rewriteRequestedKind(request.audio, actualAudioDeviceId);
                    usesNativeConstraints = true;
                }

                if (wantsVideo && actualVideoDeviceId) {
                    rewritten.video = rewriteRequestedKind(request.video, actualVideoDeviceId);
                    usesNativeConstraints = true;
                } else if (useSyntheticVideo) {
                    rewritten.video = false;
                }

                if (!usesNativeConstraints) {
                    if (useSyntheticVideo) {
                        return createSyntheticVideoStream(request.video);
                    }

                    return await invokeOriginalGetUserMedia(constraints);
                }

                const nativeStream = await invokeOriginalGetUserMedia(rewritten);
                if (!useSyntheticVideo) {
                    return nativeStream;
                }

                return mergeStreams(nativeStream, createSyntheticVideoStream(request.video));
            };
        }

        if (originalLegacyGetUserMedia) {
            const bridgeLegacyGetUserMedia = function(constraints, success, error) {
                mediaDevicesObject.getUserMedia(constraints)
                    .then((stream) => {
                        if (typeof success === 'function') {
                            success(stream);
                        }
                    })
                    .catch((reason) => {
                        if (typeof error === 'function') {
                            error(reason);
                        }
                    });
            };

            // Только СУЩЕСТВУЮЩИЕ псевдонимы. Прежний код добавлял все три подряд, и на Chrome
            // появлялся firefox-овый 'mozGetUserMedia': свойство, которого у этого браузера нет,
            // выдаёт подмену вернее любого расхождения в значениях.
            for (const alias of ['getUserMedia', 'webkitGetUserMedia', 'mozGetUserMedia']) {
                const prototype = globalObject.Navigator?.prototype;
                const target = prototype && alias in prototype
                    ? prototype
                    : (alias in globalObject.navigator ? globalObject.navigator : null);

                if (!target) {
                    continue;
                }

                try {
                    const original = target[alias];
                    Object.defineProperty(target, alias, {
                        configurable: true,
                        writable: true,
                        enumerable: Object.getOwnPropertyDescriptor(target, alias)?.enumerable ?? true,
                        value: asNativeMethod(alias, original, function (...args) {
                            return bridgeLegacyGetUserMedia.apply(this, args);
                        }),
                    });
                } catch {
                }
            }
        }
    }

    // ★ Пояс процессу задаётся переменной окружения TZ при запуске браузера, и в штатном случае
    // 'Intl' отдаёт заявленный пояс САМ — нативно и согласованно во всех путях, включая 'Date',
    // форматирование и воркеры. Тогда обёртка не даёт ничего, а стоит подменённого
    // 'resolvedOptions' и подменённого конструктора 'Intl.DateTimeFormat' — двух заметных
    // поверхностей сразу.
    //
    // Поэтому ставим её ТОЛЬКО при реальном расхождении: пояс сменили на лету уже после запуска.
    const declaredTimezone = readContext().timezone;
    let nativeTimezone = null;

    try {
        nativeTimezone = new state.originalDateTimeFormat().resolvedOptions().timeZone;
    } catch {
    }

    if (typeof declaredTimezone === 'string' && declaredTimezone.length > 0 && declaredTimezone !== nativeTimezone) {
        const originalResolvedOptions = state.originalDateTimeFormat.prototype.resolvedOptions;
        try {
            Object.defineProperty(state.originalDateTimeFormat.prototype, 'resolvedOptions', {
                configurable: true,
                writable: true,
                value: asNativeMethod('resolvedOptions', originalResolvedOptions, function (...args) {
                    const result = originalResolvedOptions.apply(this, args);
                    const timezone = readContext().timezone;
                    if (!timezone || typeof result !== 'object' || result === null) {
                        return result;
                    }

                    return {
                        ...result,
                        timeZone: timezone,
                    };
                }),
            });
        } catch {
        }

        const createDateTimeFormatOptions = (options) => {
            const current = readContext();
            const nextOptions = Object.assign({}, options ?? {});
            if (current.timezone && nextOptions.timeZone === undefined) {
                nextOptions.timeZone = current.timezone;
            }

            return nextOptions;
        };

        const AtomDateTimeFormat = function(locales, options) {
            const nextOptions = createDateTimeFormatOptions(options);
            if (new.target) {
                return Reflect.construct(state.originalDateTimeFormat, [locales, nextOptions], new.target);
            }

            return new state.originalDateTimeFormat(locales, nextOptions);
        };

        try {
            Object.setPrototypeOf(AtomDateTimeFormat, state.originalDateTimeFormat);
        } catch {
        }

        try {
            AtomDateTimeFormat.prototype = state.originalDateTimeFormat.prototype;
        } catch {
        }

        try {
            Object.defineProperty(AtomDateTimeFormat, 'supportedLocalesOf', {
                configurable: true,
                writable: true,
                value: asNativeMethod(
                    'supportedLocalesOf',
                    state.originalDateTimeFormat.supportedLocalesOf,
                    (...args) => state.originalDateTimeFormat.supportedLocalesOf(...args)),
            });
        } catch {
        }

        try {
            Object.defineProperty(Intl, 'DateTimeFormat', {
                configurable: true,
                writable: true,
                value: markNative(AtomDateTimeFormat, state.originalDateTimeFormat, 'DateTimeFormat', state.originalDateTimeFormat.length),
            });
        } catch {
        }
    }

}

try {
    assignContext(context);
} catch {
}

return true;
})()`;
}

function readExecuteScriptPayload(value: JsonValue | undefined): {
    script?: string;
    shadowHostElementId?: string;
    frameHostElementId?: string;
    elementId?: string;
    preferPageContextOnNull?: boolean;
    forcePageContextExecution?: boolean;
} {
    if (!isJsonRecord(value)) {
        return {};
    }

    const payload: {
        script?: string;
        shadowHostElementId?: string;
        frameHostElementId?: string;
        elementId?: string;
        preferPageContextOnNull?: boolean;
        forcePageContextExecution?: boolean;
    } = {};

    if (typeof value.script === 'string') {
        payload.script = value.script;
    }

    if (typeof value.shadowHostElementId === 'string' && value.shadowHostElementId.trim().length > 0) {
        payload.shadowHostElementId = value.shadowHostElementId;
    }

    if (typeof value.frameHostElementId === 'string' && value.frameHostElementId.trim().length > 0) {
        payload.frameHostElementId = value.frameHostElementId;
    }

    if (typeof value.elementId === 'string' && value.elementId.trim().length > 0) {
        payload.elementId = value.elementId;
    }

    if (value.preferPageContextOnNull === true) {
        payload.preferPageContextOnNull = true;
    }

    if (value.forcePageContextExecution === true) {
        payload.forcePageContextExecution = true;
    }

    return payload;
}

export function buildExecuteScriptSource(script: string | undefined): string {
    return `(async function(){${normalizeScriptBody(script)}})()`;
}

function buildElementExecuteScriptSource(markerId: string, script: string | undefined): string {
    const scriptBody = normalizeScriptBody(script);
    const serializedMarkerId = JSON.stringify(markerId);

    return `(() => {
const markerId = ${serializedMarkerId};
// Исходник исполняется в МИРЕ СТРАНИЦЫ: импорты модуля сюда не доезжают, а 'chrome.dom' доступен
// только изолированному миру. Поэтому чтение корня объявлено здесь и полагается на то, что видно
// странице; вызов импортированной функции давал ReferenceError и обрывал весь поиск.
const getShadowRootOfAnyMode = (host) => {
    const open = host.shadowRoot;
    if (open) {
        return open;
    }

    // Firefox отдаёт закрытый корень свойством самого узла.
    const viaGecko = host.openOrClosedShadowRoot;
    return viaGecko ?? null;
};
const findElementInRoot = (root) => {
    if (!root || typeof root.querySelector !== 'function' || typeof root.querySelectorAll !== 'function') {
        return null;
    }

    const directMatch = root.querySelector('[data-atom-el="' + markerId + '"]');
    if (directMatch) {
        return directMatch;
    }

    for (const candidate of root.querySelectorAll('*')) {
        if (!(candidate instanceof Element)) {
            continue;
        }

        const candidateShadow = getShadowRootOfAnyMode(candidate);
        if (candidateShadow) {
            const shadowMatch = findElementInRoot(candidateShadow);
            if (shadowMatch) {
                return shadowMatch;
            }
        }

        if ((candidate instanceof HTMLIFrameElement || (typeof HTMLFrameElement !== 'undefined' && candidate instanceof HTMLFrameElement))
            && candidate.contentWindow) {
            const frameMatch = findElementInWindow(candidate.contentWindow);
            if (frameMatch) {
                return frameMatch;
            }
        }
    }

    return null;
};

const findElementInWindow = (win) => {
    try {
        return findElementInRoot(win.document);
    } catch {
        return null;
    }
};

const target = findElementInWindow(window);
if (!target) {
    return null;
}

target.removeAttribute('data-atom-el');
return (() => {
    const element = target;
    const self = target;
    return (async () => {${scriptBody}})();
})();
})()`;
}

function buildShadowRootExecuteScriptSource(markerId: string, script: string | undefined): string {
    const scriptBody = normalizeScriptBody(script);
    const serializedMarkerId = JSON.stringify(markerId);

    return `(() => {
const markerId = ${serializedMarkerId};
const findHost = (root) => {
    if (!root || typeof root.querySelector !== 'function' || typeof root.querySelectorAll !== 'function') {
        return null;
    }

    const directMatch = root.querySelector('[data-atom-sr="' + markerId + '"]');
    if (directMatch) {
        return directMatch;
    }

    for (const candidate of root.querySelectorAll('*')) {
        if (!(candidate instanceof Element) || candidate.shadowRoot === null) {
            continue;
        }

        const nestedMatch = findHost(candidate.shadowRoot);
        if (nestedMatch) {
            return nestedMatch;
        }
    }

    return null;
};

const findHostInWindow = (win) => {
    try {
        const nestedHost = findHost(win.document);
        if (nestedHost) {
            return nestedHost;
        }

        for (const frameElement of win.document.querySelectorAll('iframe,frame')) {
            try {
                const childWindow = frameElement.contentWindow;
                if (!childWindow) {
                    continue;
                }

                const childHost = findHostInWindow(childWindow);
                if (childHost) {
                    return childHost;
                }
            } catch {
            }
        }
    } catch {
    }

    return null;
};

const host = findHostInWindow(window);
if (!host) {
    return null;
}

host.removeAttribute('data-atom-sr');
const shadowRoot = host.shadowRoot;
if (!shadowRoot) {
    return null;
}

return (async () => {${scriptBody}})();
})()`;
}

function buildFrameExecuteScriptSource(markerId: string, script: string | undefined): string {
    const serializedMarkerId = JSON.stringify(markerId);
    const serializedScript = JSON.stringify(buildExecuteScriptSource(script));

    return `(() => {
const markerId = ${serializedMarkerId};
const source = ${serializedScript};
const findHost = (root) => {
    if (!root || typeof root.querySelector !== 'function' || typeof root.querySelectorAll !== 'function') {
        return null;
    }

    const directMatch = root.querySelector('[data-atom-frame="' + markerId + '"]');
    if (directMatch) {
        return directMatch;
    }

    for (const candidate of root.querySelectorAll('*')) {
        if (!(candidate instanceof Element) || candidate.shadowRoot === null) {
            continue;
        }

        const nestedMatch = findHost(candidate.shadowRoot);
        if (nestedMatch) {
            return nestedMatch;
        }
    }

    return null;
};

const findHostInWindow = (win) => {
    try {
        const nestedHost = findHost(win.document);
        if (nestedHost) {
            return nestedHost;
        }

        for (const frameElement of win.document.querySelectorAll('iframe,frame')) {
            try {
                const childWindow = frameElement.contentWindow;
                if (!childWindow) {
                    continue;
                }

                const childHost = findHostInWindow(childWindow);
                if (childHost) {
                    return childHost;
                }
            } catch {
            }
        }
    } catch {
    }

    return null;
};

const host = findHostInWindow(window);
if (!(host instanceof HTMLIFrameElement) && !(typeof HTMLFrameElement !== 'undefined' && host instanceof HTMLFrameElement)) {
    return null;
}

host.removeAttribute('data-atom-frame');
try {
    const frameWindow = host.contentWindow;
    if (!frameWindow || typeof frameWindow.eval !== 'function') {
        return null;
    }

    return Promise.resolve(frameWindow.eval(source));
} catch {
    return null;
}
})()`;
}

function normalizeScriptBody(script: string | undefined): string {
    const source = String(script ?? '').trim();
    if (source.length === 0) {
        return 'return undefined;';
    }

    if (/^return\b/.test(source)) {
        return source;
    }

    if (shouldWrapScriptAsExpression(source)) {
        return `return (${stripTrailingStatementTerminator(source)});`;
    }

    return source;
}

function shouldWrapScriptAsExpression(source: string): boolean {
    const normalizedSource = stripTrailingStatementTerminator(source);

    if (normalizedSource.startsWith('(') || normalizedSource.startsWith('[') || normalizedSource.startsWith('{')) {
        return true;
    }

    if (/^(?:const|let|var|if|for|while|switch|try|catch|finally|throw|class|function|import|export|do|break|continue|debugger)\b/.test(normalizedSource)) {
        return false;
    }

    return canCompileAsAsyncExpression(normalizedSource) || isLikelyExpressionWithoutUnsafeEval(normalizedSource);
}

function stripTrailingStatementTerminator(source: string): string {
    return source.endsWith(';') ? source.slice(0, -1).trimEnd() : source;
}

function canCompileAsAsyncExpression(source: string): boolean {
    try {
        new Function(`return (async function(){ return (${source}); });`);
        return true;
    } catch {
        return false;
    }
}

function isLikelyExpressionWithoutUnsafeEval(source: string): boolean {
    if (source.length == 0) {
        return false;
    }

    return !hasTopLevelStatementTerminator(source);
}

function hasTopLevelStatementTerminator(source: string): boolean {
    let parenthesisDepth = 0;
    let bracketDepth = 0;
    let braceDepth = 0;
    let activeQuote: string | null = null;
    let escaping = false;
    let lineComment = false;
    let blockComment = false;

    for (let index = 0; index < source.length; index += 1) {
        const current = source[index];
        const next = index + 1 < source.length ? source[index + 1] : '';

        if (lineComment) {
            if (current === '\n' || current === '\r') {
                lineComment = false;
            }

            continue;
        }

        if (blockComment) {
            if (current === '*' && next === '/') {
                blockComment = false;
                index += 1;
            }

            continue;
        }

        if (activeQuote !== null) {
            if (escaping) {
                escaping = false;
                continue;
            }

            if (current === '\\') {
                escaping = true;
                continue;
            }

            if (current === activeQuote) {
                activeQuote = null;
            }

            continue;
        }

        if (current === '/' && next === '/') {
            lineComment = true;
            index += 1;
            continue;
        }

        if (current === '/' && next === '*') {
            blockComment = true;
            index += 1;
            continue;
        }

        if (current === '\'' || current === '"' || current === '`') {
            activeQuote = current;
            continue;
        }

        switch (current) {
            case '(':
                parenthesisDepth += 1;
                break;
            case ')':
                parenthesisDepth = Math.max(0, parenthesisDepth - 1);
                break;
            case '[':
                bracketDepth += 1;
                break;
            case ']':
                bracketDepth = Math.max(0, bracketDepth - 1);
                break;
            case '{':
                braceDepth += 1;
                break;
            case '}':
                braceDepth = Math.max(0, braceDepth - 1);
                break;
            case ';':
                if (parenthesisDepth === 0 && bracketDepth === 0 && braceDepth === 0) {
                    return true;
                }

                break;
        }
    }

    return false;
}

async function executeScriptWithFallback(channel: BrowserRuntimePortChannel, script: string, preferPageContextOnNull = false, forcePageContextExecution = false): Promise<string> {
    try {
        return await channel.executeInMain(script, preferPageContextOnNull, forcePageContextExecution);
    } catch (error) {
        if (!shouldFallbackToIsolatedWorld(error)) {
            throw error;
        }

        return await executeScriptInContentWorld(script);
    }
}

function shouldFallbackToIsolatedWorld(error: unknown): boolean {
    if (!(error instanceof Error)) {
        return false;
    }

    return error.message.includes('scripting.executeScript');
}

async function executeScriptInPageContext(script: string): Promise<string> {
    const responseId = `atom-main-world-response-${++elementIdCounter}`;
    const eventName = `atom-main-world-result-${responseId}`;

    return await new Promise<string>((resolve, reject) => {
        let settled = false;
        const cleanup = () => {
            globalThis.removeEventListener(eventName, handleResult);
            globalThis.clearTimeout(timeoutId);
            document.getElementById(responseId)?.remove();
            injection.remove();
        };
        const finish = (callback: () => void) => {
            if (settled) {
                return;
            }

            settled = true;
            cleanup();
            callback();
        };
        const handleResult = () => {
            const payloadText = document.getElementById(responseId)?.textContent;
            if (!payloadText) {
                finish(() => reject(new Error('Основной мир не вернул результат выполнения скрипта.')));
                return;
            }

            try {
                const payload = JSON.parse(payloadText) as {
                    status?: string;
                    value?: string;
                    error?: string;
                };
                if (payload.status === 'ok') {
                    finish(() => resolve(typeof payload.value === 'string' ? payload.value : 'null'));
                    return;
                }

                finish(() => reject(new Error(typeof payload.error === 'string' ? payload.error : 'Не удалось выполнить код в основном мире.')));
            } catch (error) {
                finish(() => reject(error));
            }
        };
        const timeoutId = globalThis.setTimeout(() => {
            finish(() => reject(new Error('Истекло ожидание результата выполнения скрипта в основном мире.')));
        }, 5000);
        const injection = document.createElement('script');
        injection.addEventListener('error', () => {
            finish(() => reject(new Error('Не удалось выполнить встроенный скрипт в основном мире.')));
        }, { once: true });
        injection.textContent = buildInjectedMainWorldExecuteScript(responseId, eventName, script);

        globalThis.addEventListener(eventName, handleResult, { once: true });

        const root = document.documentElement ?? document.head ?? document.body;
        if (root === null) {
            finish(() => reject(new Error('Не удалось найти корневой узел документа для выполнения скрипта в основном мире.')));
            return;
        }

        root.appendChild(injection);
    });
}

function buildInjectedMainWorldExecuteScript(responseId: string, eventName: string, script: string): string {
    const serializedResponseId = JSON.stringify(responseId);
    const serializedEventName = JSON.stringify(eventName);
    const serializedScript = JSON.stringify(script);

    return `(() => {
const responseId = ${serializedResponseId};
const eventName = ${serializedEventName};
const source = ${serializedScript};
const publish = (payload) => {
    const root = document.documentElement ?? document.head ?? document.body;
    if (!root) {
        return;
    }

    let node = document.getElementById(responseId);
    if (!node) {
        node = document.createElement('script');
        node.id = responseId;
        node.type = 'application/json';
        root.appendChild(node);
    }

    node.textContent = JSON.stringify(payload);
    globalThis.dispatchEvent(new Event(eventName));
};

Promise.resolve()
    .then(() => (0, eval)(source))
    .then((result) => {
        publish({
            status: 'ok',
            value: result !== null && result !== undefined ? String(result) : 'null',
        });
    })
    .catch((error) => {
        publish({
            status: 'err',
            error: error instanceof Error ? error.message : String(error),
        });
    });
})();`;
}


async function executeScriptInContentWorld(script: string): Promise<string> {
    let result = (0, eval)(script);
    if (result !== null && typeof result === 'object' && 'then' in result && typeof result.then === 'function') {
        result = await result;
    }

    return result !== null && result !== undefined ? String(result) : 'null';
}

/**
 * Исполняет скрипт над ЗАКРЫТЫМ shadow root прямо в изолированном мире расширения.
 *
 * Такой корень недостижим из основного мира страницы, поэтому обычный путь через маркер
 * `data-atom-sr` и `host.shadowRoot` там вернул бы null. Здесь корень уже получен через
 * `chrome.dom.openOrClosedShadowRoot` и передаётся скрипту переменной `shadowRoot` — тот же
 * контракт, что у открытого пути (см. `buildShadowRootExecuteScriptSource`).
 */
async function executeScriptInClosedShadowRoot(shadowRoot: ShadowRoot, script: string | undefined): Promise<string> {
    const body = normalizeScriptBody(script);
    const runner = new Function('shadowRoot', `return (async () => {${body}})();`) as (root: ShadowRoot) => Promise<unknown>;

    const result = await runner(shadowRoot);
    return result !== null && result !== undefined ? String(result) : 'null';
}

function isJsonRecord(value: JsonValue | undefined): value is Record<string, JsonValue> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readElementSearchPayload(value: JsonValue | undefined): {
    strategy: string;
    value: string;
    parentElementId?: string;
    shadowHostElementId?: string;
    frameHostElementId?: string;
    allowShadowRootDiscovery?: boolean;
} {
    if (!isJsonRecord(value)) {
        throw new Error('Payload поиска элемента имеет неверную форму');
    }

    const strategy = typeof value.strategy === 'string' ? value.strategy : '';
    const selectorValue = typeof value.value === 'string' ? value.value : '';
    if (strategy.trim().length === 0 || selectorValue.trim().length === 0) {
        throw new Error('Payload поиска элемента не содержит strategy/value');
    }

    return {
        strategy,
        value: selectorValue,
        parentElementId: typeof value.parentElementId === 'string' ? value.parentElementId : undefined,
        shadowHostElementId: typeof value.shadowHostElementId === 'string' ? value.shadowHostElementId : undefined,
        frameHostElementId: typeof value.frameHostElementId === 'string' ? value.frameHostElementId : undefined,
        allowShadowRootDiscovery: value.allowShadowRootDiscovery === true,
    };
}

function readElementPropertyPayload(value: JsonValue | undefined): {
    elementId: string;
    propertyName: string;
} {
    if (!isJsonRecord(value)) {
        throw new Error('Payload свойства элемента имеет неверную форму');
    }

    const elementId = typeof value.elementId === 'string' ? value.elementId : '';
    const propertyName = typeof value.propertyName === 'string' ? value.propertyName : '';
    if (elementId.trim().length === 0 || propertyName.trim().length === 0) {
        throw new Error('Payload свойства элемента не содержит elementId/propertyName');
    }

    return { elementId, propertyName };
}

function readElementIdentifierPayload(value: JsonValue | undefined, errorMessage: string): {
    elementId: string;
} {
    if (!isJsonRecord(value)) {
        throw new Error(errorMessage);
    }

    const elementId = typeof value.elementId === 'string' ? value.elementId : '';
    if (elementId.trim().length === 0) {
        throw new Error('Payload не содержит elementId');
    }

    return { elementId };
}

function readResolveScreenPointPayload(value: JsonValue | undefined): {
    elementId: string;
    scrollIntoView: boolean;
} {
    const payload = readElementIdentifierPayload(value, 'Payload screen point имеет неверную форму');

    if (!isJsonRecord(value)) {
        throw new Error('Payload screen point имеет неверную форму');
    }

    return {
        ...payload,
        scrollIntoView: value.scrollIntoView === true,
    };
}

function readFocusElementPayload(value: JsonValue | undefined): {
    elementId: string;
    scrollIntoView: boolean;
} {
    const payload = readElementIdentifierPayload(value, 'Payload фокуса элемента имеет неверную форму');

    if (!isJsonRecord(value)) {
        throw new Error('Payload фокуса элемента имеет неверную форму');
    }

    return {
        ...payload,
        scrollIntoView: value.scrollIntoView === true,
    };
}

function readWaitForElementPayload(value: JsonValue | undefined): {
    strategy: string;
    value: string;
    timeoutMs: number;
    requireVisible: boolean;
    requireStable: boolean;
    parentElementId?: string;
    shadowHostElementId?: string;
    frameHostElementId?: string;
} {
    const payload = readElementSearchPayload(value);

    if (!isJsonRecord(value)) {
        throw new Error('Payload ожидания элемента имеет неверную форму');
    }

    const timeoutMs = typeof value.timeoutMs === 'number' && Number.isFinite(value.timeoutMs)
        ? value.timeoutMs
        : 10_000;

    // Драйвер всегда присылает вид ожидания. Прежде поле молча игнорировалось, и Visible со
    // Stable вели себя как Attached: ожидание завершалось на появлении узла в DOM, ещё до того
    // как элемент становился видимым и переставал двигаться. Перечисление на стороне C# помечено
    // [Flags], поэтому Visible | Stable приезжает строкой «Visible, Stable» — разбираем список.
    const requestedKinds = typeof value.kind === 'string'
        ? value.kind.split(',').map((part) => part.trim())
        : [];
    const requireStable = requestedKinds.includes('Stable');

    // Стабильность невидимого элемента лишена смысла, поэтому Stable подразумевает Visible.
    const requireVisible = requireStable || requestedKinds.includes('Visible');

    return {
        ...payload,
        timeoutMs,
        requireVisible,
        requireStable,
    };
}

const WAIT_FOR_ELEMENT_POLL_INTERVAL_MS = 50;

// Минимальный разнос между замерами положения для вида Stable. Наблюдатель мутаций срабатывает на
// каждое изменение style, поэтому без этого порога два замера могли прийтись на один кадр — они,
// разумеется, совпадали, и едущий элемент объявлялся стабильным.
const WAIT_FOR_ELEMENT_STABLE_SAMPLE_INTERVAL_MS = 120;

interface ElementBox {
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
}

function readElementBox(element: Element): ElementBox | null {
    if (typeof element.getBoundingClientRect !== 'function') {
        return null;
    }

    const rect = element.getBoundingClientRect();
    return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
}

function isElementVisible(element: Element): boolean {
    const box = readElementBox(element);
    if (box === null || box.width <= 0 || box.height <= 0) {
        return false;
    }

    const view = element.ownerDocument?.defaultView;
    if (!view || typeof view.getComputedStyle !== 'function') {
        return true;
    }

    const style = view.getComputedStyle(element);
    if (style.visibility === 'hidden' || style.visibility === 'collapse' || style.display === 'none') {
        return false;
    }

    return Number.parseFloat(style.opacity || '1') > 0;
}

function isSameBox(left: ElementBox | null, right: ElementBox | null): boolean {
    if (left === null || right === null) {
        return false;
    }

    // Полпикселя: браузер отдаёт дробные координаты, и дрожание субпиксельного округления
    // не должно бесконечно откладывать признание элемента стабильным.
    return Math.abs(left.x - right.x) < 0.5
        && Math.abs(left.y - right.y) < 0.5
        && Math.abs(left.width - right.width) < 0.5
        && Math.abs(left.height - right.height) < 0.5;
}

function resolveSearchRoot(payload: {
    parentElementId?: string;
    shadowHostElementId?: string;
    frameHostElementId?: string;
}): Document | ShadowRoot | Element | null | undefined {
    if (payload.shadowHostElementId !== undefined) {
        const host = elementRegistry.get(payload.shadowHostElementId);
        if (host === undefined) {
            return undefined;
        }

        return getShadowRootOfAnyMode(host) ?? undefined;
    }

    if (payload.frameHostElementId !== undefined) {
        const host = elementRegistry.get(payload.frameHostElementId);
        if (!isFrameHostElement(host)) {
            return undefined;
        }

        try {
            return host.contentDocument ?? null;
        } catch {
            return null;
        }
    }

    if (payload.parentElementId !== undefined) {
        return elementRegistry.get(payload.parentElementId);
    }

    return document;
}

function findSingle(strategy: string, value: string, root: Document | ShadowRoot | Element): Element | null {
    const evaluationDocument = root instanceof Document ? root : (root.ownerDocument ?? document);

    switch (strategy) {
        case 'Css':
            return root.querySelector(value);
        case 'XPath': {
            const result = evaluationDocument.evaluate(value, root, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
            return result instanceof Element ? result : null;
        }
        case 'Id':
            return 'getElementById' in root && typeof root.getElementById === 'function'
                ? root.getElementById(value)
                : root.querySelector(`#${CSS.escape(value)}`);
        case 'Text':
            return findByText(value, root);
        case 'Name':
            return root.querySelector(`[name="${CSS.escape(value)}"]`);
        case 'TagName':
            return root.querySelector(value);
        default:
            return null;
    }
}

function findMultiple(strategy: string, value: string, root: Document | ShadowRoot | Element): Element[] {
    const evaluationDocument = root instanceof Document ? root : (root.ownerDocument ?? document);

    switch (strategy) {
        case 'Css':
            return [...root.querySelectorAll(value)];
        case 'XPath': {
            const snapshot = evaluationDocument.evaluate(value, root, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
            const elements: Element[] = [];
            for (let index = 0; index < snapshot.snapshotLength; index += 1) {
                const node = snapshot.snapshotItem(index);
                if (node instanceof Element) {
                    elements.push(node);
                }
            }

            return elements;
        }
        case 'Id': {
            const element = 'getElementById' in root && typeof root.getElementById === 'function'
                ? root.getElementById(value)
                : root.querySelector(`#${CSS.escape(value)}`);
            return element === null ? [] : [element];
        }
        case 'Text':
            return findAllByText(value, root);
        case 'Name':
            return [...root.querySelectorAll(`[name="${CSS.escape(value)}"]`)];
        case 'TagName':
            return [...root.querySelectorAll(value)];
        default:
            return [];
    }
}

function findMultipleWithOpenShadowRootDiscovery(strategy: string, value: string, root: Document | ShadowRoot | Element): Element[] {
    if (strategy !== 'Css') {
        return findMultiple(strategy, value, root);
    }

    const results: Element[] = [];
    const seen = new Set<Element>();

    const appendMatches = (currentRoot: Document | ShadowRoot | Element): void => {
        for (const match of currentRoot.querySelectorAll(value)) {
            if (seen.has(match)) {
                continue;
            }

            seen.add(match);
            // Отсоединённые от документа элементы не должны попадать в результаты live-поиска: иначе
            // discovery фреймов вернёт host отвалившегося iframe (например, при remove→reattach в shadow
            // root узел ещё держится ссылкой из elementRegistry и всплывает через обход теневых корней),
            // и C#-кэш дочерних фреймов не сможет его выпилить (см. Frame.EnsureChildFramesDiscoveredAsync).
            if (!match.isConnected) {
                continue;
            }

            results.push(match);
        }
    };

    const visit = (currentRoot: Document | ShadowRoot | Element): void => {
        appendMatches(currentRoot);

        if (currentRoot instanceof Element) {
            const ownShadow = getShadowRootOfAnyMode(currentRoot);
            if (ownShadow !== null) {
                visit(ownShadow);
            }
        }

        for (const candidate of currentRoot.querySelectorAll('*')) {
            if (!(candidate instanceof Element)) {
                continue;
            }

            const candidateShadow = getShadowRootOfAnyMode(candidate);
            if (candidateShadow === null) {
                continue;
            }

            visit(candidateShadow);
        }
    };

    visit(root);
    return results;
}

function isFrameHostElement(element: Element | undefined): element is HTMLIFrameElement | HTMLFrameElement {
    return element instanceof HTMLIFrameElement
        || (typeof HTMLFrameElement !== 'undefined' && element instanceof HTMLFrameElement);
}

// Непрозрачность, блокирующая main-world eval, наблюдаема ТОЛЬКО по origin: Xray позволяет isolated
// content-миру прочитать contentDocument кросс-доменного фрейма (поэтому проверка contentDocument здесь
// бесполезна). data:/blob:/чужой origin дают отдельный или opaque ('null') origin — для них fail-closed
// БЕЗ main-world round-trip. Синхронно, чтобы не зависеть от потока, на котором стоит page main world.
function isFrameOpaqueForMainWorldEval(host: HTMLIFrameElement | HTMLFrameElement): boolean {
    // Определяем по атрибуту src ЭЛЕМЕНТА-хоста (он в родительском документе и доступен content-скрипту
    // напрямую), а НЕ по host.contentDocument.URL: для кросс-доменного фрейма Xray-обёртка отдаёт из
    // isolated world вводящий в заблуждение 'about:blank', из-за чего data:-фрейм ошибочно считался
    // same-origin.
    let frameSrc = '';
    try {
        frameSrc = host.src ?? '';
    } catch {
        // ignore — оценим ниже
    }

    // Пустой src или about:* (about:blank[#...], about:srcdoc, srcdoc-фреймы) — same-origin, наследует
    // origin родителя: исполнять внутрь можно.
    if (frameSrc === '' || frameSrc.startsWith('about:')) {
        return false;
    }

    try {
        const frameOrigin = new URL(frameSrc, document.baseURI).origin;
        // data:/blob: дают opaque ('null') origin; чужой http(s) origin — cross-origin. И то, и другое
        // непрозрачно для main-world eval → fail closed без round-trip.
        return frameOrigin === 'null' || frameOrigin !== document.location.origin;
    } catch {
        return true;
    }
}

function findByText(text: string, root: Document | ShadowRoot | Element): Element | null {
    const walkRoot = root === document ? document.body : root;
    if (walkRoot === null) {
        return null;
    }

    const walker = document.createTreeWalker(walkRoot, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
        if (walker.currentNode.textContent?.trim() === text) {
            return walker.currentNode.parentElement;
        }
    }

    return null;
}

function findAllByText(text: string, root: Document | ShadowRoot | Element): Element[] {
    const walkRoot = root === document ? document.body : root;
    if (walkRoot === null) {
        return [];
    }

    const walker = document.createTreeWalker(walkRoot, NodeFilter.SHOW_TEXT);
    const elements: Element[] = [];
    while (walker.nextNode()) {
        if (walker.currentNode.textContent?.trim() === text && walker.currentNode.parentElement !== null) {
            elements.push(walker.currentNode.parentElement);
        }
    }

    return elements;
}

// Порог, после которого при регистрации выполняется амортизированная уборка отсоединённых
// от DOM элементов. Держит elementRegistry (сильные ссылки) от бесконечного роста на
// динамических/SPA-страницах, которые не проходят полную навигацию.
const maxRegisteredElements = 5000;

function registerElement(element: Element): string {
    // O(1) обратный поиск через WeakMap вместо линейного сканирования всего реестра.
    const existingId = elementIdRegistry.get(element);
    if (existingId !== undefined && elementRegistry.get(existingId) === element) {
        return existingId;
    }

    if (elementRegistry.size >= maxRegisteredElements) {
        pruneDisconnectedElements();
    }

    const id = `el_${++elementIdCounter}`;
    elementRegistry.set(id, element);
    elementIdRegistry.set(element, id);
    return id;
}

// Освобождает записи для элементов, удалённых из DOM: сильная ссылка в elementRegistry иначе
// не давала бы собрать отсоединённые поддеревья. Записи в elementIdRegistry (WeakMap) уходят
// сами при сборке элемента. Подключённые элементы сохраняются — драйвер ещё может их адресовать.
function pruneDisconnectedElements(): void {
    for (const [id, element] of elementRegistry) {
        if (!element.isConnected) {
            elementRegistry.delete(id);
        }
    }
}

function isFrameElement(element: Element): boolean {
    const tagName = element.tagName.toUpperCase();
    return tagName === 'IFRAME' || tagName === 'FRAME';
}

function scheduleMicrotask(callback: () => void): void {
    if (typeof globalThis.queueMicrotask === 'function') {
        globalThis.queueMicrotask(callback);
        return;
    }

    globalThis.setTimeout(callback, 0);
}

async function waitForLayoutMeasurementAsync(frameCount: number = 2): Promise<void> {
    for (let index = 0; index < frameCount; index++) {
        await new Promise<void>((resolve) => {
            if (typeof globalThis.requestAnimationFrame === 'function') {
                globalThis.requestAnimationFrame(() => resolve());
                return;
            }

            globalThis.setTimeout(resolve, 16);
        });
    }
}