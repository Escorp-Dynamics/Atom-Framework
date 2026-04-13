import { type RuntimeConfig } from '../Shared/Config';
import {
    bridgeEventNames,
    validateTabContextEnvelope,
    type BridgeMessage,
    type JsonValue,
    type MainWorldResultEnvelope,
    type TabContextEnvelope,
} from '../Shared/Protocol';
import {
    BridgeSessionCoordinator,
    BrowserWebSocketTransportClient,
    buildVirtualCookieHeader,
    buildCookieUrl,
    captureTabPngDataUrl,
    closeTrackedTab,
    closeTrackedWindowTabs,
    cloneHeaders,
    ConsoleSessionHealthReporter,
    createCookieCommandContext,
    createDefaultTabContext as createDefaultTabContextEnvelope,
    createDirectResponse,
    createTab,
    createWindow,
    createDirectVirtualCookie,
    createLifecycleEventMessage,
    createInternalMessageId,
    createSessionId,
    DefaultHandshakeClient,
    DefaultRouteFailurePolicy,
    deleteVisibleVirtualCookies,
    emitBackgroundDebugEvent,
    ensureVirtualCookieStore,
    ensureDiscoveryTab,
    executeScriptInFrames,
    evaluateMainWorldScript,
    findFirstWindowTab,
    getAllWindows,
    getBrowserHost,
    getCookies,
    handleClientHintsRequestInterception,
    handleDeleteCookiesCommand,
    handleCookieRequestInterception,
    handleCookieResponseInterception,
    handleGetCookiesCommand,
    handleGetTitleCommand,
    handleGetUrlCommand,
    handleGetWindowBoundsCommand,
    handleActivateWindowCommand,
    handleOpenWindowCommand,
    isHeaderNamed,
    readMessageTabId,
    readOptionalPayloadBoolean,
    readOptionalPayloadInteger,
    readOptionalPayloadString,
    readPayloadString,
    readPayloadValueString,
    readWindowId,
    readWindowPosition,
    requireInteger,
    handleSetCookieCommand,
    getRuntimeApi,
    getWebRequestTabId,
    getTab,
    getWindow,
    getVisibleVirtualCookies,
    InMemoryRequestCorrelationStore,
    InMemoryTabRegistry,
    IntervalKeepAliveController,
    queryTabs,
    reloadTab,
    removeCookie,
    removeTab,
    removeWindow,
    requireTabUrl,
    RuntimePortTabEndpoint,
    resolveWindowTab,
    setCookie,
    TabPortCommandRouter,
    loadBootstrapRuntimeConfigWithSource,
    moveVirtualCookieStore,
    PassiveEventRouter,
    registerSetTabContext,
    toErrorMessage,
    toJsonVirtualCookie,
    upsertVirtualCookie,
    updateTab,
    updateWindow,
    addWebRequestListener,
    pruneExpiredVirtualCookies,
    releaseTabContext,
    type BrowserCookie,
    type BrowserHost,
    type BrowserTab,
    type BrowserWindowInfo,
    type HeaderLike,
    type VirtualCookie,
    type BootstrapRuntimeConfigResult,
    type BootstrapRuntimeConfigSource,
    type MutableHeaderLike,
    type WebRequestDetails,
    type RuntimePortLike,
} from './index';

type JsonRecord = Record<string, unknown>;
type HeaderMap = Record<string, string>;
type InterceptionRoute = { tabId: string; windowId?: string };
type ProxyRequestDetails = { readonly tabId?: number; readonly url?: string };
type ProxyAuthRequiredDetails = ProxyRequestDetails & { readonly isProxy?: boolean };
type ProxyRoutingResult =
    | { type: 'direct' }
    | { type: 'http'; host: string; port: number }
    | { type: 'socks'; host: string; port: number; proxyDNS: true }
    | { type: 'socks4'; host: string; port: number };
type PendingNavigationState = {
    readonly command: 'Navigate' | 'Reload';
    readonly expectedUrl?: string;
    readonly previousUrl?: string;
    readonly previousEndpoint: unknown | null;
    readonly startedAt: number;
    readonly retryCount: number;
    readonly lastRetryAt?: number;
    readonly runtimeConfirmationAttemptedAt?: number;
};
type PendingNavigationResolution = {
    readonly pendingNavigation?: PendingNavigationState;
    readonly browserTab: BrowserTab | null;
};
type RuntimeUrlInspection = {
    readonly confirmed: boolean;
    readonly status: string;
    readonly href?: string;
    readonly readyState?: string;
    readonly error?: string;
};
type PendingNavigationReapplyInspection = {
    readonly canReapply: boolean;
    readonly runtimeCheckStatus?: string;
    readonly runtimeHref?: string;
    readonly runtimeReadyState?: string;
    readonly runtimeCheckError?: string;
};
type BlockingInterceptionDecision = {
    action?: string;
    url?: string;
    headers?: HeaderMap;
    responseHeaders?: HeaderMap;
    statusCode?: number;
    reasonPhrase?: string;
    bodyBase64?: string;
};
type BackgroundRuntimeGlobalState = typeof globalThis & {
    __atomBackgroundRuntimeHost?: BackgroundRuntimeHost;
};

const bridgeEventNameSet = new Set(bridgeEventNames);
const maxLateNavigateRetryCount = 4;

export class BackgroundRuntimeHost {
    private readonly browserHost = getBrowserHost();
    private readonly runtime = getRuntimeApi(this.browserHost);
    private sessionIdValue = createSessionId();
    private readonly transport = new BrowserWebSocketTransportClient();
    private readonly tabs = new InMemoryTabRegistry();
    private readonly tabContexts = new Map<string, TabContextEnvelope>();
    private readonly virtualCookies = new Map<string, VirtualCookie[]>();
    private readonly pendingNavigations = new Map<string, PendingNavigationState>();
    private readonly pendingNavigationGateDiagnostics = new Map<string, string>();
    private readonly interceptEnabledTabs = new Set<string>();
    private readonly interceptPatterns = new Map<string, readonly RegExp[]>();
    private readonly pendingResponseHeaderOverrides = new Map<string, HeaderMap>();
    private health = new ConsoleSessionHealthReporter(this.sessionIdValue);
    private coordinator = this.createCoordinator(this.sessionIdValue);
    private started = false;
    private config: RuntimeConfig | null = null;
    private cookieIsolationListenersInitialized = false;
    private proxyRoutingListenersInitialized = false;

    private readonly onConnect = (port: RuntimePortLike) => {
        void this.handlePortConnected(port);
    };

    private readonly onBeforeSendHeaders = (details: WebRequestDetails) => {
        const tabId = getWebRequestTabId(details.tabId);
        const context = tabId === null ? undefined : this.getTabContext(tabId);
        const cookieMutation = handleCookieRequestInterception(
            details,
            (tabId) => this.getTabContext(tabId),
            (contextId) => ensureVirtualCookieStore(this.virtualCookies, contextId),
        );

        const mutation = handleClientHintsRequestInterception(
            details,
            (tabId) => this.getTabContext(tabId),
            cookieMutation?.requestHeaders,
        ) ?? cookieMutation;

        const requestDebugDetails = createMainFrameDeviceRequestDebugDetails(
            details,
            context,
            mutation?.requestHeaders ?? details.requestHeaders,
        );
        if (requestDebugDetails !== undefined) {
            emitBackgroundDebugEvent(this.config, 'device-context-main-frame-request', requestDebugDetails);
        }

        return this.handleBlockingInterceptedRequest(details, mutation);
    };

    private readonly onHeadersReceived = (details: WebRequestDetails) => {
        const tabId = getWebRequestTabId(details.tabId);
        const context = tabId === null ? undefined : this.getTabContext(tabId);
        const mutation = handleCookieResponseInterception(
            details,
            (tabId) => this.getTabContext(tabId),
            (contextId) => ensureVirtualCookieStore(this.virtualCookies, contextId),
        );

        if (tabId !== null && context !== undefined && mutation !== undefined) {
            void this.syncDocumentCookieSurface(tabId, context, details.url);
        }

        const responseDebugDetails = createMainFrameDeviceResponseDebugDetails(
            details,
            context,
            mutation?.responseHeaders ?? details.responseHeaders,
        );
        if (responseDebugDetails !== undefined) {
            emitBackgroundDebugEvent(this.config, 'device-context-main-frame-response', responseDebugDetails);
        }

        return this.handleBlockingInterceptedResponse(details, mutation);
    };

    private readonly onProxyRequest = (details: ProxyRequestDetails): ProxyRoutingResult | undefined => {
        const context = this.resolveProxyContext(details.tabId);
        if (context === undefined) {
            return undefined;
        }

        const navigationProxyRoute = this.resolveNavigationProxyRoutingResult(context);
        if (navigationProxyRoute !== undefined) {
            return navigationProxyRoute;
        }

        if (context.proxy === null) {
            return { type: 'direct' };
        }

        return context.proxy === undefined
            ? undefined
            : resolveProxyRoutingResult(context.proxy);
    };

    private readonly onProxyAuthRequired = (details: ProxyAuthRequiredDetails) => {
        if (details.isProxy === false) {
            return {};
        }

        const context = this.resolveProxyContext(details.tabId);
        const navigationProxyRouteToken = this.resolveNavigationProxyRouteToken(context);
        if (navigationProxyRouteToken !== undefined) {
            return {
                authCredentials: {
                    username: navigationProxyRouteToken,
                    password: '',
                },
            };
        }

        if (context?.proxy === undefined || context.proxy === null) {
            return {};
        }

        const authCredentials = resolveProxyAuthCredentials(context.proxy);
        return authCredentials === null ? {} : { authCredentials };
    };

    public constructor() {
    }

    public get state() {
        return this.coordinator.state;
    }

    public get sessionId(): string {
        return this.sessionIdValue;
    }

    public async start(): Promise<void> {
        if (this.started) {
            return;
        }

        this.started = true;
        // Content scripts connect immediately on startup, so the port listener must exist before async config loading.
        this.runtime.onConnect?.addListener(this.onConnect);

        let loadedConfig: BootstrapRuntimeConfigResult;
        try {
            loadedConfig = await loadBootstrapRuntimeConfigWithSource(this.runtime, this.browserHost);
            this.config = loadedConfig.config;
        } catch (error) {
            this.runtime.onConnect?.removeListener?.(this.onConnect);
            console.error('[фоновый вход] Не удалось загрузить конфигурацию запуска', error);
            this.started = false;
            return;
        }

        this.sessionIdValue = this.config.sessionId;

        emitBackgroundDebugEvent(this.config, 'config-loaded', {
            source: loadedConfig.source,
            sessionId: this.config.sessionId,
            host: this.config.host,
            port: this.config.port,
            extensionVersion: this.config.extensionVersion,
        });

        try {
            this.ensureCookieIsolationListeners();
        } catch (error) {
            emitBackgroundDebugEvent(this.config, 'cookie-isolation-init-failed', {
                error: toErrorMessage(error),
            });

            console.error('[фоновый вход] Не удалось инициализировать cookie isolation listeners', error);
        }

        try {
            this.ensureProxyRoutingListeners();
        } catch (error) {
            emitBackgroundDebugEvent(this.config, 'proxy-routing-init-failed', {
                error: toErrorMessage(error),
            });

            console.error('[фоновый вход] Не удалось инициализировать proxy routing listeners', error);
        }

        this.health = new ConsoleSessionHealthReporter(this.sessionIdValue);
        this.coordinator = this.createCoordinator(this.sessionIdValue);

        try {
            emitBackgroundDebugEvent(this.config, 'session-starting', {
                discoveryUrl: createDiscoveryUrl(this.config),
            });

            const startResult = await this.coordinator.start(this.config);
            await ensureDiscoveryTab(
                this.runtime,
                this.browserHost,
                this.config,
                (tabId) => this.tabs.get(tabId) !== null,
                createDiscoveryUrl(this.config),
            );
            await this.publishRegisteredTabsToBridge();

            emitBackgroundDebugEvent(this.config, 'session-started', {
                state: this.state,
                transportConnected: this.transport.connected,
                connectedTabCount: this.tabs.count(),
                pendingRequestCount: this.snapshot().pendingRequestCount,
                startResult,
            });

            console.info('[фоновый вход] Сеанс мостового слоя запущен', startResult);
        } catch (error) {
            emitBackgroundDebugEvent(this.config, 'session-start-failed', {
                state: this.state,
                error: toErrorMessage(error),
            });

            console.error('[фоновый вход] Не удалось запустить сеанс мостового слоя', error);
        }
    }

    public async stop(reason: string): Promise<void> {
        this.runtime.onConnect?.removeListener?.(this.onConnect);

        for (const runtime of this.tabs.list()) {
            await runtime.endpoint.disconnect(reason);
        }

        await this.coordinator.stop(reason);
        this.tabContexts.clear();
        this.virtualCookies.clear();
        this.started = false;
    }

    public snapshot() {
        return this.health.createSnapshot(this.sessionId);
    }

    private createCoordinator(sessionId: string): BridgeSessionCoordinator {
        return new BridgeSessionCoordinator({
            transport: this.transport,
            handshake: new DefaultHandshakeClient(),
            tabs: this.tabs,
            commandRouter: new TabPortCommandRouter({
                tabs: this.tabs,
                transport: this.transport,
                failures: new DefaultRouteFailurePolicy(),
                routeDirect: (message) => this.tryHandleDirectCommand(message),
            }),
            eventRouter: new PassiveEventRouter(),
            health: this.health,
            correlation: new InMemoryRequestCorrelationStore(),
            keepAlive: new IntervalKeepAliveController(),
        }, sessionId);
    }

    private async handlePortConnected(port: RuntimePortLike): Promise<void> {
        let endpoint: RuntimePortTabEndpoint;
        try {
            endpoint = new RuntimePortTabEndpoint(port, {
                forwardToBridge: (message) => this.transport.send(message),
                executeInMainWorld: (requestId, script, preferPageContextOnNull, forcePageContextExecution) => this.executeInMainWorld(endpoint.tabId, requestId, script, preferPageContextOnNull, forcePageContextExecution),
                markReady: (tabId, context) => {
                    const resolvedContext = resolveReadyTabContext(this.getTabContext(tabId), context);
                    this.tabContexts.set(tabId, resolvedContext);
                    this.tabs.markReady(tabId, resolvedContext);
                    this.health.reportTabCount(this.tabs.count());
                },
                onDisconnected: (tabId) => {
                    const currentRuntime = this.tabs.get(tabId);
                    if (currentRuntime?.endpoint !== endpoint) {
                        emitBackgroundDebugEvent(this.config, 'runtime-port-disconnect-ignored', {
                            tabId,
                            windowId: endpoint.windowId,
                            reason: 'superseded-by-new-connection',
                            hasCurrentRuntime: currentRuntime !== null,
                        });

                        return;
                    }

                    this.tabs.unregister(tabId);
                    this.health.reportTabCount(this.tabs.count());

                    emitBackgroundDebugEvent(this.config, 'runtime-port-disconnected', {
                        tabId,
                        windowId: endpoint.windowId,
                        connectedTabCount: this.tabs.count(),
                    });

                    void this.sendLifecycleEvent('TabDisconnected', tabId);
                },
            });
        } catch (error) {
            emitBackgroundDebugEvent(this.config, 'runtime-port-prepare-failed', {
                error: toErrorMessage(error),
            });

            console.error('[фоновый вход] Не удалось подготовить порт вкладки', error);
            return;
        }

        const previous = this.tabs.unregister(endpoint.tabId);
        if (previous !== null) {
            emitBackgroundDebugEvent(this.config, 'runtime-port-replacement-start', {
                tabId: endpoint.tabId,
                windowId: endpoint.windowId,
                previousWindowId: previous.endpoint.windowId,
                previousConnected: previous.endpoint.connected,
                hadPreviousContext: previous.context !== undefined,
            });

            await previous.endpoint.disconnect('Порт вкладки заменён новым подключением');
        }

        let restoredContext = previous?.context
            ?? this.tabContexts.get(endpoint.tabId)
            ?? this.createDefaultTabContext(endpoint.tabId, endpoint.windowId);
        restoredContext = await this.refreshContextFromBrowserTabAsync(endpoint.tabId, restoredContext, endpoint.windowId);

        this.updateTrackedTabContext(endpoint.tabId, restoredContext);
        ensureVirtualCookieStore(this.virtualCookies, restoredContext.contextId);
        this.tabs.register(endpoint, restoredContext);
        this.health.reportTabCount(this.tabs.count());

        emitBackgroundDebugEvent(this.config, 'runtime-port-connected', {
            tabId: endpoint.tabId,
            windowId: endpoint.windowId,
            replaced: previous !== null,
            connectedTabCount: this.tabs.count(),
            contextId: restoredContext.contextId,
            url: restoredContext.url,
        });

        await endpoint.applyContext(restoredContext).catch((error) => {
            emitBackgroundDebugEvent(this.config, 'runtime-port-apply-context-failed', {
                tabId: endpoint.tabId,
                windowId: endpoint.windowId,
                replaced: previous !== null,
                contextId: restoredContext.contextId,
                url: restoredContext.url,
                error: toErrorMessage(error),
            });

            console.error('[фоновый вход] Не удалось восстановить контекст вкладки', error);
        });

        await this.sendLifecycleEvent('TabConnected', endpoint.tabId, endpoint.windowId, toJsonContext(restoredContext));

        void this.syncDocumentCookieSurface(endpoint.tabId, restoredContext, restoredContext.url);
    }

    private async publishRegisteredTabsToBridge(): Promise<void> {
        for (const runtime of this.tabs.list()) {
            await this.sendLifecycleEvent(
                'TabConnected',
                runtime.endpoint.tabId,
                runtime.endpoint.windowId,
                toJsonContext(runtime.context),
            );
        }
    }

    private async tryHandleDirectCommand(message: BridgeMessage): Promise<boolean> {
        const directTabReadCommands = this.createDirectTabReadCommandContext();
        const directWindowReadCommands = this.createDirectWindowReadCommandContext();
        const directWindowWriteCommands = this.createDirectWindowWriteCommandContext();
        const directCookieCommands = this.createDirectCookieCommandContext();
        const command = message.command as BridgeMessage['command'] | 'Reload';

        try {
            switch (command) {
                case 'DebugPortStatus':
                    {
                        const tabId = requireTabId(message);
                        const trackedTabId = tabId.toString();
                        const runtime = this.tabs.get(trackedTabId);
                        const browserTab = await this.tryGetBrowserTabAsync(trackedTabId);
                        const context = await this.resolveDebugPortStatusContextAsync(
                            trackedTabId,
                            runtime,
                            this.getTabContext(trackedTabId),
                            browserTab,
                        );
                        const { pendingNavigation, browserTab: resolvedBrowserTab } = await this.resolvePendingNavigationStateAsync(
                            trackedTabId,
                            runtime,
                            context,
                            browserTab,
                        );
                        if (pendingNavigation === undefined) {
                            this.clearPendingNavigationGateDebugState(trackedTabId);
                        }
                        const isPendingNavigation = pendingNavigation !== undefined;
                        const pendingNavigationReapplyInspection = isPendingNavigation && runtime !== null
                            ? await this.inspectPendingNavigationReapplyAsync(trackedTabId, pendingNavigation)
                            : undefined;
                        const canReapplyPendingNavigationContext = pendingNavigationReapplyInspection?.canReapply === true;
                        if (isPendingNavigation
                            && runtime !== null
                            && context !== undefined
                            && hasExtendedTabContext(context)
                            && !canReapplyPendingNavigationContext) {
                            this.emitPendingNavigationGateDebugEvent(
                                'pending-navigation-hidden-port',
                                trackedTabId,
                                this.createPendingNavigationGateDebugDetails(
                                    'debug-port-status',
                                    trackedTabId,
                                    pendingNavigation,
                                    context,
                                    resolvedBrowserTab,
                                    false,
                                    pendingNavigationReapplyInspection,
                                ),
                            );
                        } else {
                            this.pendingNavigationGateDiagnostics.delete(`pending-navigation-hidden-port:${trackedTabId}`);
                        }
                        const hasVisiblePort = runtime !== null && (!isPendingNavigation || canReapplyPendingNavigationContext);
                        const browserTabUrl = readNonEmptyString(resolvedBrowserTab?.url);
                        const browserTabPendingUrl = readNonEmptyString(resolvedBrowserTab?.pendingUrl);
                        const browserTabStatus = readNonEmptyString(resolvedBrowserTab?.status);

                        const payload: JsonRecord = {
                            tabId,
                            hasPort: hasVisiblePort,
                            queueLength: 0,
                            hasSocket: hasVisiblePort && this.transport.connected,
                            isReady: !isPendingNavigation && context?.isReady === true,
                            interceptEnabled: !isPendingNavigation && runtime !== null
                                && this.cookieIsolationListenersInitialized
                                && (this.config?.featureFlags.enableInterception ?? false),
                            hasTabContext: context !== undefined,
                            hasBrowserTab: resolvedBrowserTab !== null,
                        };

                        if (context?.contextId !== undefined) {
                            payload.contextId = context.contextId;
                        }

                        if (context?.userAgent !== undefined) {
                            payload.contextUserAgent = context.userAgent;
                        }

                        if (browserTabUrl !== undefined) {
                            payload.browserTabUrl = browserTabUrl;
                        }

                        if (browserTabPendingUrl !== undefined) {
                            payload.browserTabPendingUrl = browserTabPendingUrl;
                        }

                        if (browserTabStatus !== undefined) {
                            payload.browserTabStatus = browserTabStatus;
                        }

                        if (pendingNavigationReapplyInspection?.runtimeCheckStatus !== undefined) {
                            payload.runtimeCheckStatus = pendingNavigationReapplyInspection.runtimeCheckStatus;
                        }

                        if (pendingNavigationReapplyInspection?.runtimeHref !== undefined) {
                            payload.runtimeHref = pendingNavigationReapplyInspection.runtimeHref;
                        }

                        if (pendingNavigationReapplyInspection?.runtimeReadyState !== undefined) {
                            payload.runtimeReadyState = pendingNavigationReapplyInspection.runtimeReadyState;
                        }

                        if (pendingNavigationReapplyInspection?.runtimeCheckError !== undefined) {
                            payload.runtimeCheckError = pendingNavigationReapplyInspection.runtimeCheckError;
                        }

                        await this.sendDirectResponse(message, payload);
                        return true;
                    }

                case 'SetTabContext': {
                    const context = validateTabContextEnvelope(message.payload);
                    const existingContext = this.getTabContext(context.tabId);
                    registerSetTabContext(
                        context.tabId,
                        context,
                        existingContext,
                        this.tabContexts,
                        this.virtualCookies,
                        this.tabs,
                    );

                    if (context.proxy !== undefined) {
                        this.ensureProxyRoutingListeners();
                    }

                    const runtime = this.tabs.get(context.tabId);
                    const pendingNavigation = this.pendingNavigations.get(context.tabId);
                    const pendingNavigationReapplyInspection = runtime !== null && pendingNavigation !== undefined
                        ? await this.inspectPendingNavigationReapplyAsync(context.tabId, pendingNavigation)
                        : undefined;
                    const canApplyPendingNavigationContext = pendingNavigationReapplyInspection?.canReapply === true;
                    if (runtime !== null
                        && pendingNavigation !== undefined
                        && hasExtendedTabContext(context)
                        && !canApplyPendingNavigationContext) {
                        const browserTab = await this.tryGetBrowserTabAsync(context.tabId);
                        this.emitPendingNavigationGateDebugEvent(
                            'pending-navigation-context-apply-deferred',
                            context.tabId,
                            this.createPendingNavigationGateDebugDetails(
                                'set-tab-context',
                                context.tabId,
                                pendingNavigation,
                                context,
                                browserTab,
                                false,
                                pendingNavigationReapplyInspection,
                            ),
                        );
                    } else {
                        this.pendingNavigationGateDiagnostics.delete(`pending-navigation-context-apply-deferred:${context.tabId}`);
                    }
                    if (runtime !== null && (pendingNavigation === undefined || canApplyPendingNavigationContext)) {
                        try {
                            await runtime.endpoint.applyContext(context);
                        } catch (error) {
                            emitBackgroundDebugEvent(this.config, 'set-tab-context-apply-failed', {
                                tabId: context.tabId,
                                windowId: context.windowId,
                                contextId: context.contextId,
                                url: context.url,
                                isReady: context.isReady,
                                pendingNavigation: pendingNavigation !== undefined,
                                error: toErrorMessage(error),
                            });

                            throw error;
                        }

                        await this.syncDocumentCookieSurface(context.tabId, context, context.url);
                    }

                    await this.sendDirectResponse(message, {
                        tabId: context.tabId,
                        isReady: context.isReady,
                    });
                    return true;
                }

                case 'Navigate': {
                    const tabId = requireTabId(message);
                    const url = readPayloadString(message.payload, 'url', 'Команда навигации не содержит адрес');
                    const trackedTabId = tabId.toString();
                    const previousBrowserTab = await getTab(this.runtime, this.browserHost.tabs, tabId).catch(() => null);
                    const previousRuntime = this.tabs.get(trackedTabId);
                    const currentContext = this.getTabContext(trackedTabId);
                    const requiresActiveNavigateStart = hasDeviceEmulationContext(currentContext);
                    const navigationWindowId = this.resolveNavigationWindowId(previousBrowserTab, currentContext);
                    let updatedTab = await this.updateTabForNavigateAsync(
                        tabId,
                        url,
                        navigationWindowId,
                        requiresActiveNavigateStart,
                    );

                    this.pendingNavigations.set(trackedTabId, {
                        command: 'Navigate',
                        expectedUrl: url,
                        previousUrl: readNonEmptyString(previousBrowserTab?.url),
                        previousEndpoint: previousRuntime?.endpoint ?? null,
                        startedAt: Date.now(),
                        retryCount: 0,
                    });

                    if (!await this.hasObservedNavigateStartAsync(tabId, url, readNonEmptyString(previousBrowserTab?.url), updatedTab)) {
                        updatedTab = await this.updateTabForNavigateAsync(tabId, url, navigationWindowId, true);
                    }

                    if (currentContext !== undefined) {
                        this.updateTrackedTabContext(trackedTabId, {
                            ...currentContext,
                            url,
                            windowId: typeof updatedTab.windowId === 'number'
                                ? updatedTab.windowId.toString()
                                : currentContext.windowId,
                            isReady: false,
                            readyAt: undefined,
                        });
                    }

                    await this.sendDirectResponse(message, {
                        tabId: tabId.toString(),
                        url,
                    });
                    return true;
                }

                case 'Reload': {
                    const tabId = requireTabId(message);
                    const trackedTabId = tabId.toString();
                    const previousRuntime = this.tabs.get(trackedTabId);
                    const currentContext = this.getTabContext(trackedTabId);

                    this.pendingNavigations.set(trackedTabId, {
                        command: 'Reload',
                        expectedUrl: readNonEmptyString(currentContext?.url),
                        previousUrl: readNonEmptyString(currentContext?.url),
                        previousEndpoint: previousRuntime?.endpoint ?? null,
                        startedAt: Date.now(),
                        retryCount: 0,
                    });

                    if (currentContext !== undefined) {
                        this.updateTrackedTabContext(trackedTabId, {
                            ...currentContext,
                            isReady: false,
                            readyAt: undefined,
                        });
                    }

                    await reloadTab(this.runtime, this.browserHost.tabs, tabId);
                    await this.sendDirectResponse(message, {
                        tabId: trackedTabId,
                    });
                    return true;
                }

                case 'GetUrl': {
                    await handleGetUrlCommand(directTabReadCommands, message, requireTabId(message));
                    return true;
                }

                case 'GetTitle': {
                    await handleGetTitleCommand(directTabReadCommands, message, requireTabId(message));
                    return true;
                }

                case 'CaptureScreenshot': {
                    const tabId = requireTabId(message);
                    const tab = await getTab(this.runtime, this.browserHost.tabs, tabId);
                    emitBackgroundDebugEvent(this.config, 'capture-screenshot-start', {
                        tabId,
                        windowId: tab.windowId ?? null,
                        url: tab.url ?? null,
                    });

                    let screenshot: string;
                    try {
                        screenshot = await captureTabPngDataUrl(this.runtime, this.browserHost.tabs, this.browserHost.windows, tabId, tab.windowId);
                    } catch (error) {
                        emitBackgroundDebugEvent(this.config, 'capture-screenshot-failed', {
                            tabId,
                            windowId: tab.windowId ?? null,
                            url: tab.url ?? null,
                            error: toErrorMessage(error),
                        });
                        throw error;
                    }

                    emitBackgroundDebugEvent(this.config, 'capture-screenshot-succeeded', {
                        tabId,
                        windowId: tab.windowId ?? null,
                        payloadLength: screenshot.length,
                    });
                    await this.sendDirectResponse(message, screenshot);
                    return true;
                }

                case 'ExecuteScriptInFrames': {
                    const tabId = requireTabId(message);
                    const script = readPayloadString(message.payload, 'script', 'Команда выполнения по фреймам не содержит script');
                    const world = readOptionalPayloadString(message.payload, 'world') === 'ISOLATED'
                        ? 'ISOLATED'
                        : 'MAIN';
                    const includeMetadata = readOptionalPayloadBoolean(message.payload, 'includeMetadata') === true;
                    const results = await executeScriptInFrames(this.browserHost, this.runtime, tabId, script, world, includeMetadata);
                    await this.sendDirectResponse(message, results);
                    return true;
                }


                    function hasScopedExecuteScriptTarget(value: unknown): boolean {
                        const preferPageContextOnNull = readOptionalPayloadBoolean(value, 'preferPageContextOnNull') === true;
                        const forcePageContextExecution = readOptionalPayloadBoolean(value, 'forcePageContextExecution') === true;

                        if (!isNonEmptyRecord(value)) {
                            return preferPageContextOnNull || forcePageContextExecution;
                        }

                        return typeof value.shadowHostElementId === 'string' && value.shadowHostElementId.trim().length > 0
                            || typeof value.frameHostElementId === 'string' && value.frameHostElementId.trim().length > 0
                            || typeof value.elementId === 'string' && value.elementId.trim().length > 0
                            || preferPageContextOnNull
                            || forcePageContextExecution;
                    }
                case 'OpenTab': {
                    const url = readOptionalPayloadString(message.payload, 'url')
                        ?? (this.config !== null ? createDiscoveryUrl(this.config) : 'about:blank');

                    const tab = await createTab(this.runtime, this.browserHost.tabs, { url, active: true });
                    const payload: JsonRecord = { url: tab.url ?? url };

                    if (typeof tab.id === 'number') {
                        payload.tabId = tab.id.toString();
                    }

                    if (typeof tab.windowId === 'number') {
                        payload.windowId = tab.windowId.toString();
                    }

                    await this.sendDirectResponse(message, payload);
                    return true;
                }

                case 'CloseTab': {
                    const tabId = requireTabId(message);
                    await removeTab(this.runtime, this.browserHost.tabs, tabId);
                    closeTrackedTab(
                        tabId.toString(),
                        this.tabs,
                        this.tabContexts,
                        this.virtualCookies,
                        (count) => this.health.reportTabCount(count),
                    );
                    await this.sendDirectResponse(message);
                    return true;
                }

                case 'ActivateTab': {
                    const tabId = requireTabId(message);
                    await updateTab(this.runtime, this.browserHost.tabs, tabId, { active: true });
                    await this.sendDirectResponse(message);
                    return true;
                }

                case 'OpenWindow': {
                    const url = readOptionalPayloadString(message.payload, 'url')
                        ?? (this.config !== null ? createDiscoveryUrl(this.config) : 'about:blank');
                    await handleOpenWindowCommand(directWindowWriteCommands, message, url, readWindowPosition(message.payload));
                    return true;
                }

                case 'CloseWindow': {
                    const windowId = readWindowId(message.payload);
                    const windowTabs = await queryTabs(this.runtime, this.browserHost.tabs, { windowId });

                    await closeTrackedWindowTabs(windowTabs, this.tabs, this.tabContexts, this.virtualCookies);

                    await removeWindow(this.runtime, this.browserHost.windows, windowId);
                    this.health.reportTabCount(this.tabs.count());
                    await this.sendDirectResponse(message);
                    return true;
                }

                case 'ActivateWindow': {
                    await handleActivateWindowCommand(directWindowWriteCommands, message, readWindowId(message.payload));
                    return true;
                }

                case 'GetWindowBounds': {
                    const tabId = readMessageTabId(message.payload) ?? requireTabId(message);
                    const tab = await getTab(this.runtime, this.browserHost.tabs, tabId);
                    const windowId = requireInteger(tab.windowId, 'Не удалось определить окно для вкладки');

                    await handleGetWindowBoundsCommand(directWindowReadCommands, message, windowId);
                    return true;
                }

                case 'SetCookie': {
                    const tabId = requireTabId(message);
                    const tab = await getTab(this.runtime, this.browserHost.tabs, tabId);
                    const url = requireTabUrl(tab);
                    const name = readPayloadString(message.payload, 'name', 'Команда cookie не содержит имя');
                    const value = readPayloadValueString(message.payload, 'value', 'Команда cookie не содержит значение');
                    const context = this.ensureCookieCommandContext(
                        tabId.toString(),
                        readOptionalPayloadString(message.payload, 'contextId'),
                        typeof tab.windowId === 'number' ? tab.windowId.toString() : undefined,
                    );
                    await handleSetCookieCommand(
                        directCookieCommands,
                        message,
                        url,
                        context,
                        name,
                        value,
                        readOptionalPayloadString(message.payload, 'domain'),
                        readOptionalPayloadString(message.payload, 'path'),
                        readOptionalPayloadBoolean(message.payload, 'secure'),
                        readOptionalPayloadBoolean(message.payload, 'httpOnly'),
                        readOptionalPayloadInteger(message.payload, 'expires'),
                    );
                    await this.syncDocumentCookieSurface(tabId.toString(), context, url);
                    return true;
                }

                case 'GetCookies': {
                    const tabId = requireTabId(message);
                    const tab = await getTab(this.runtime, this.browserHost.tabs, tabId);
                    const url = requireTabUrl(tab);
                    const context = this.ensureCookieCommandContext(
                        tabId.toString(),
                        readOptionalPayloadString(message.payload, 'contextId'),
                        typeof tab.windowId === 'number' ? tab.windowId.toString() : undefined,
                        url,
                    );

                    await handleGetCookiesCommand(directCookieCommands, message, url, context);
                    return true;
                }

                case 'DeleteCookies': {
                    const tabId = requireTabId(message);
                    const tab = await getTab(this.runtime, this.browserHost.tabs, tabId);
                    const url = requireTabUrl(tab);
                    const context = this.ensureCookieCommandContext(
                        tabId.toString(),
                        readOptionalPayloadString(message.payload, 'contextId'),
                        typeof tab.windowId === 'number' ? tab.windowId.toString() : undefined,
                        url,
                    );

                    await handleDeleteCookiesCommand(directCookieCommands, message, url, context);
                    await this.syncDocumentCookieSurface(tabId.toString(), context, url);
                    return true;
                }

                case 'InterceptRequest': {
                    const tabId = requireTabId(message).toString();
                    const enabled = readBooleanPayload(message.payload, 'enabled');
                    const patterns = readStringArrayPayload(message.payload, 'patterns');

                    if (enabled) {
                        this.interceptEnabledTabs.add(tabId);

                        if (patterns.length > 0) {
                            this.interceptPatterns.set(tabId, patterns.map((pattern) => compileInterceptionPattern(pattern)));
                        } else {
                            this.interceptPatterns.delete(tabId);
                        }
                    } else {
                        this.interceptEnabledTabs.delete(tabId);
                        this.interceptPatterns.delete(tabId);
                    }

                    await this.sendDirectResponse(message, {
                        enabled,
                        tabId,
                        patternsCount: patterns.length,
                    });
                    return true;
                }

                default:
                    return false;
            }
        } catch (error) {
            emitBackgroundDebugEvent(this.config, 'direct-command-failed', {
                command,
                tabId: message.tabId ?? null,
                windowId: message.windowId ?? null,
                error: toErrorMessage(error),
            });

            await this.sendDirectErrorResponse(message, error);
            return true;
        }
    }

    private createDirectTabReadCommandContext() {
        return {
            runtime: this.runtime,
            browserHost: this.browserHost,
            sendDirectResponse: (directMessage: BridgeMessage, payload?: unknown) => this.sendDirectResponse(directMessage, payload),
        };
    }

    private createDirectWindowReadCommandContext() {
        return {
            runtime: this.runtime,
            browserHost: this.browserHost,
            sendDirectResponse: (directMessage: BridgeMessage, payload?: unknown) => this.sendDirectResponse(directMessage, payload),
        };
    }

    private createDirectWindowWriteCommandContext() {
        return {
            runtime: this.runtime,
            browserHost: this.browserHost,
            sendDirectResponse: (directMessage: BridgeMessage, payload?: unknown) => this.sendDirectResponse(directMessage, payload),
        };
    }

    private createDirectCookieCommandContext() {
        return {
            runtime: this.runtime,
            browserHost: this.browserHost,
            virtualCookies: this.virtualCookies,
            sendDirectResponse: (directMessage: BridgeMessage, payload?: unknown) => this.sendDirectResponse(directMessage, payload),
            mapBrowserCookie: toJsonCookie,
        };
    }

    private async executeInMainWorld(tabId: string, requestId: string, script: string, preferPageContextOnNull = false, forcePageContextExecution = false): Promise<MainWorldResultEnvelope> {
        try {
            const value = await evaluateMainWorldScript(
                this.browserHost,
                this.runtime,
                Number(tabId),
                script,
                preferPageContextOnNull,
                forcePageContextExecution,
                (kind, details) => {
                    const detailRecord = typeof details === 'object' && details !== null && !Array.isArray(details)
                        ? details as Record<string, unknown>
                        : { value: details };

                    emitBackgroundDebugEvent(this.config, kind, {
                        tabId,
                        requestId,
                        ...detailRecord,
                    });
                },
            );
            return {
                action: 'mainWorldResult',
                requestId,
                status: 'ok',
                value,
            };
        } catch (error) {
            return {
                action: 'mainWorldResult',
                requestId,
                status: 'err',
                error: error instanceof Error && error.message.trim().length > 0
                    ? error.message
                    : 'Не удалось выполнить код в основном мире',
            };
        }
    }

    private async sendDirectResponse(message: BridgeMessage, payload?: unknown): Promise<void> {
        await this.transport.send(createDirectResponse(message, payload));
    }

    private async sendDirectErrorResponse(message: BridgeMessage, error: unknown): Promise<void> {
        await this.transport.send({
            id: message.id,
            type: 'Response',
            tabId: message.tabId,
            windowId: message.windowId,
            status: 'Error',
            error: toErrorMessage(error),
            timestamp: Date.now(),
        });
    }

    private async sendLifecycleEvent(event: any, tabId?: string, windowId?: string, payload?: unknown): Promise<void> {
        try {
            await this.transport.send(createLifecycleEventMessage(
                createInternalMessageId(event.toLowerCase()),
                event,
                tabId,
                windowId,
                payload,
            ));
        } catch (error) {
            console.error('[фоновый вход] Не удалось передать событие жизненного цикла', error);
        }
    }

    private ensureCookieIsolationListeners(): void {
        if (this.cookieIsolationListenersInitialized) {
            return;
        }

        const webRequest = this.browserHost.webRequest;
        if (webRequest?.onBeforeSendHeaders === undefined || webRequest.onHeadersReceived === undefined) {
            return;
        }

        addWebRequestListener(webRequest.onBeforeSendHeaders, this.onBeforeSendHeaders, ['blocking', 'requestHeaders', 'extraHeaders']);
        addWebRequestListener(webRequest.onHeadersReceived, this.onHeadersReceived, ['blocking', 'responseHeaders', 'extraHeaders']);
        this.cookieIsolationListenersInitialized = true;
    }

    private ensureProxyRoutingListeners(): void {
        if (this.proxyRoutingListenersInitialized) {
            return;
        }

        const proxyApi = this.browserHost.proxy;
        const authRequired = this.browserHost.webRequest?.onAuthRequired;
        let registered = false;

        if (typeof proxyApi?.onRequest?.addListener === 'function') {
            proxyApi.onRequest.addListener(this.onProxyRequest, { urls: ['<all_urls>'] });
            registered = true;
        }

        if (typeof authRequired?.addListener === 'function') {
            try {
                authRequired.addListener(this.onProxyAuthRequired, { urls: ['<all_urls>'] }, ['blocking']);
            } catch {
                authRequired.addListener(this.onProxyAuthRequired, { urls: ['<all_urls>'] });
            }

            registered = true;
        }

        if (registered) {
            this.proxyRoutingListenersInitialized = true;
        }
    }

    private resolveProxyContext(tabId: number | undefined): TabContextEnvelope | undefined {
        return typeof tabId === 'number' && Number.isInteger(tabId)
            ? this.getTabContext(tabId.toString())
            : undefined;
    }

    private resolveNavigationProxyRouteToken(context: TabContextEnvelope | undefined): string | undefined {
        if (context?.navigationInterceptionMode !== 'proxy') {
            return undefined;
        }

        const routeToken = context.navigationProxyRouteToken?.trim();
        return routeToken !== undefined && routeToken.length > 0
            ? routeToken
            : undefined;
    }

    private resolveNavigationProxyRoutingResult(context: TabContextEnvelope | undefined): ProxyRoutingResult | undefined {
        const config = this.config;
        if (this.resolveNavigationProxyRouteToken(context) === undefined || config == null) {
            return undefined;
        }

        return {
            type: 'http',
            host: config.host,
            port: config.proxyPort ?? config.port,
        };
    }

    private handleBlockingInterceptedRequest(details: WebRequestDetails, mutation: { requestHeaders?: HeaderLike[] } | undefined) {
        const route = this.resolveInterceptionRoute(details);
        if (route === null) {
            return mutation;
        }

        const requestHeaders = toMutableHeaders(mutation?.requestHeaders ?? details.requestHeaders);
        const decision = this.tryPostInterceptedRequest(route, details, requestHeaders);
        if (decision === null) {
            void this.emitInterceptedRequestEvent(details);
            this.tryPostObservedRequestHeaders(route, details, requestHeaders);
            return mutation;
        }

        if (isNonEmptyRecord(decision.responseHeaders) && typeof details.requestId === 'string' && details.requestId.length > 0) {
            this.pendingResponseHeaderOverrides.set(details.requestId, cloneHeaderMap(decision.responseHeaders));
        }

        if (decision.action === 'abort') {
            return { cancel: true };
        }

        let modified = applyHeaderOverrides(requestHeaders, decision.headers);

        if (decision.action === 'fulfill' && isNavigateRequestType(details.type)) {
            console.error('[фоновый вход] Request-side main_frame fulfill без сетевого fallback не поддерживается текущим browser webRequest API; запрос отменён.');
            this.tryPostObservedRequestHeaders(route, details, requestHeaders);
            return { cancel: true };
        }

        const redirectUrl = this.resolveRedirectUrlForRequestDecision(decision);
        if (redirectUrl !== null) {
            return { redirectUrl };
        }

        this.tryPostObservedRequestHeaders(route, details, requestHeaders);

        if (!modified) {
            return mutation;
        }

        return { requestHeaders };
    }

    private handleBlockingInterceptedResponse(details: WebRequestDetails, mutation: { responseHeaders?: HeaderLike[] } | undefined) {
        const responseHeaders = toMutableHeaders(mutation?.responseHeaders ?? details.responseHeaders);
        const pendingResponseOverrides = typeof details.requestId === 'string'
            ? this.pendingResponseHeaderOverrides.get(details.requestId)
            : undefined;

        let modified = applyHeaderOverrides(responseHeaders, pendingResponseOverrides);
        if (typeof details.requestId === 'string' && pendingResponseOverrides !== undefined) {
            this.pendingResponseHeaderOverrides.delete(details.requestId);
        }

        const headerCountBeforePolicyFilter = responseHeaders.length;
        for (let index = responseHeaders.length - 1; index >= 0; index--) {
            const headerName = responseHeaders[index]?.name?.toLowerCase();
            if (headerName === 'content-security-policy' || headerName === 'content-security-policy-report-only') {
                responseHeaders.splice(index, 1);
            }
        }

        modified = responseHeaders.length != headerCountBeforePolicyFilter || modified;

        const route = this.resolveInterceptionRoute(details);
        if (route === null) {
            return modified ? { responseHeaders } : mutation;
        }

        const decision = this.tryPostInterceptedResponse(route, details, responseHeaders);
        if (decision === null) {
            void this.emitInterceptedResponseEvent(details);
            return modified ? { responseHeaders } : mutation;
        }

        if (decision.action === 'abort') {
            return { cancel: true };
        }

        modified = applyHeaderOverrides(responseHeaders, decision.responseHeaders) || modified;
        if (decision.action === 'fulfill' && typeof details.requestId === 'string') {
            this.tryReplaceResponseBody(details.requestId, decision.bodyBase64);
        }

        if (!modified) {
            return mutation;
        }

        return { responseHeaders };
    }

    private async emitInterceptedRequestEvent(details: WebRequestDetails): Promise<void> {
        const route = this.resolveInterceptionRoute(details);
        if (route === null) {
            return;
        }

        await this.transport.send(createLifecycleEventMessage(
            createInternalMessageId('request-intercepted'),
            'RequestIntercepted',
            route.tabId,
            route.windowId,
            {
                url: details.url ?? '',
                method: details.method ?? 'GET',
                headers: headersToObject(details.requestHeaders),
                requestId: details.requestId,
                type: details.type ?? 'other',
                supportsNavigationFulfillment: this.resolveNavigationFulfillmentSupport(route.tabId, details),
                ts: normalizeEventTimestamp(details.timeStamp),
                isNavigate: isNavigateRequestType(details.type),
            },
        ));
    }

    private async emitInterceptedResponseEvent(details: WebRequestDetails): Promise<void> {
        const route = this.resolveInterceptionRoute(details);
        if (route === null) {
            return;
        }

        await this.transport.send(createLifecycleEventMessage(
            createInternalMessageId('response-received'),
            'ResponseReceived',
            route.tabId,
            route.windowId,
            {
                url: details.url ?? '',
                method: details.method ?? 'GET',
                headers: headersToObject(details.responseHeaders),
                requestId: details.requestId,
                type: details.type ?? 'other',
                ts: normalizeEventTimestamp(details.timeStamp),
                isNavigate: isNavigateRequestType(details.type),
                statusCode: typeof details.statusCode === 'number' && Number.isFinite(details.statusCode)
                    ? Math.trunc(details.statusCode)
                    : 200,
                reasonPhrase: details.statusLine ?? '',
            },
        ));
    }

    private resolveInterceptionRoute(details: WebRequestDetails): InterceptionRoute | null {
        if (!(this.config?.featureFlags.enableInterception ?? false)) {
            return null;
        }

        const tabId = getWebRequestTabId(details.tabId);
        if (tabId === null || !this.interceptEnabledTabs.has(tabId)) {
            return null;
        }

        const url = details.url?.trim();
        if (!url || isBridgeInternalUrl(url, this.config)) {
            return null;
        }

        const patterns = this.interceptPatterns.get(tabId);
        if (patterns !== undefined && patterns.length > 0 && !patterns.some((pattern) => pattern.test(url))) {
            return null;
        }

        const context = this.getTabContext(tabId);
        return {
            tabId,
            windowId: context?.windowId,
        };
    }

    private getTabContext(tabId: string): TabContextEnvelope | undefined {
        return this.tabs.get(tabId)?.context ?? this.tabContexts.get(tabId);
    }

    private async tryGetBrowserTabAsync(tabId: string): Promise<BrowserTab | null> {
        const numericTabId = Number(tabId);
        if (!Number.isInteger(numericTabId) || numericTabId <= 0) {
            return null;
        }

        try {
            return await getTab(this.runtime, this.browserHost.tabs, numericTabId);
        } catch {
            return null;
        }
    }

    private async resolveDebugPortStatusContextAsync(
        tabId: string,
        runtime: ReturnType<InMemoryTabRegistry['get']>,
        context: TabContextEnvelope | undefined,
        browserTab: BrowserTab | null = null,
    ): Promise<TabContextEnvelope | undefined> {
        if (runtime === null || context === undefined || context.isReady === true) {
            return context;
        }

        const numericTabId = Number(tabId);
        if (!Number.isInteger(numericTabId) || numericTabId <= 0) {
            return context;
        }

        const observedBrowserTab = browserTab ?? await this.tryGetBrowserTabAsync(tabId);
        if (observedBrowserTab === null) {
            return context;
        }

        const browserUrl = readNonEmptyString(observedBrowserTab.url);
        const browserUrlReadyForContext = observedBrowserTab.status === 'complete'
            && isCompletedBrowserUrlReadyForContext(browserUrl, context);
        const contextUrl = readNonEmptyString(context.url);
        const hasPendingNavigation = this.pendingNavigations.has(tabId);
        const runtimeConfirmedAtContextUrl = !browserUrlReadyForContext
            && !hasPendingNavigation
            && contextUrl !== undefined
            && await this.hasConfirmedCurrentRuntimeAtUrlAsync(numericTabId, contextUrl);
        if (!browserUrlReadyForContext && !runtimeConfirmedAtContextUrl) {
            return context;
        }

        const readyContext: TabContextEnvelope = {
            ...context,
            windowId: typeof observedBrowserTab.windowId === 'number'
                ? observedBrowserTab.windowId.toString()
                : context.windowId,
            url: runtimeConfirmedAtContextUrl
                ? (contextUrl ?? browserUrl ?? context.url)
                : (browserUrl ?? context.url),
            isReady: true,
            readyAt: Date.now(),
        };

        this.updateTrackedTabContext(tabId, readyContext);
        return readyContext;
    }

    private async resolvePendingNavigationStateAsync(
        tabId: string,
        runtime: ReturnType<InMemoryTabRegistry['get']>,
        context: TabContextEnvelope | undefined,
        initialBrowserTab: BrowserTab | null = null,
    ): Promise<PendingNavigationResolution> {
        const pendingNavigation = this.pendingNavigations.get(tabId);
        if (pendingNavigation === undefined) {
            return {
                pendingNavigation: undefined,
                browserTab: initialBrowserTab,
            };
        }

        if (Date.now() - pendingNavigation.startedAt > 30000) {
            this.pendingNavigations.delete(tabId);
            return {
                pendingNavigation: undefined,
                browserTab: initialBrowserTab,
            };
        }

        const numericTabId = Number(tabId);
        if (!Number.isInteger(numericTabId) || numericTabId <= 0) {
            return {
                pendingNavigation,
                browserTab: initialBrowserTab,
            };
        }

        let browserTab = initialBrowserTab;
        if (browserTab === null) {
            browserTab = await this.tryGetBrowserTabAsync(tabId);
        }

        if (browserTab === null) {
            return {
                pendingNavigation,
                browserTab: null,
            };
        }

        const expectedUrl = pendingNavigation.expectedUrl;
        const hasStartedNavigation = expectedUrl !== undefined
            && hasBrowserTabStartedNavigate(browserTab, expectedUrl, pendingNavigation.previousUrl);

        if (expectedUrl !== undefined
            && pendingNavigation.retryCount === 0
            && Date.now() - pendingNavigation.startedAt >= 150
            && !hasStartedNavigation) {
            try {
                const now = Date.now();
                browserTab = await this.updateTabForNavigateAsync(
                    numericTabId,
                    expectedUrl,
                    this.resolveNavigationWindowId(browserTab, context),
                    true,
                );
                this.pendingNavigations.set(tabId, {
                    ...pendingNavigation,
                    lastRetryAt: now,
                    retryCount: 1,
                });
            } catch {
            }
        }

        const currentBrowserUrl = readNonEmptyString(browserTab.url);
        const pendingBrowserUrl = readNonEmptyString(browserTab.pendingUrl);
        const hasExpectedBrowserUrl = hasPendingNavigationReachedTarget(browserTab, expectedUrl);
        const hasExpectedPendingBrowserUrl = expectedUrl !== undefined
            && pendingBrowserUrl !== undefined
            && areEquivalentUrls(pendingBrowserUrl, expectedUrl);
        const browserUrlMatchesExpected = expectedUrl === undefined
            || (currentBrowserUrl !== undefined && areEquivalentUrls(currentBrowserUrl, expectedUrl));
        const contextUrl = readNonEmptyString(context?.url);
        const staleRuntimeUrl = expectedUrl === undefined
            ? undefined
            : [contextUrl, currentBrowserUrl, pendingNavigation.previousUrl].find(
                (candidate): candidate is string => candidate !== undefined && !areEquivalentUrls(candidate, expectedUrl),
            );
        const hasReadyContext = context?.isReady === true;
        const hasReadyContextAtExpectedUrl = hasReadyContext
            && (expectedUrl === undefined || (contextUrl !== undefined && areEquivalentUrls(contextUrl, expectedUrl)));
        const hasRebootstrappedRuntime = runtime !== null
            && (pendingNavigation.previousEndpoint === null || runtime.endpoint !== pendingNavigation.previousEndpoint);
        const hasReadyNavigateContextAtTarget = pendingNavigation.command === 'Navigate'
            && (browserUrlMatchesExpected || (hasReadyContextAtExpectedUrl && hasRebootstrappedRuntime));
        const resolvedReadyNavigateUrl = browserUrlMatchesExpected
            ? (currentBrowserUrl ?? contextUrl ?? context?.url)
            : (contextUrl ?? currentBrowserUrl ?? context?.url);

        if (hasReadyNavigateContextAtTarget && context !== undefined) {
            this.updateTrackedTabContext(tabId, {
                ...context,
                windowId: typeof browserTab.windowId === 'number'
                    ? browserTab.windowId.toString()
                    : context.windowId,
                url: resolvedReadyNavigateUrl,
                isReady: true,
                readyAt: context.readyAt ?? Date.now(),
            });
            this.pendingNavigations.delete(tabId);
            return {
                pendingNavigation: undefined,
                browserTab,
            };
        }

        if (expectedUrl !== undefined
            && context !== undefined
            && runtime !== null
            && pendingNavigation.retryCount > 0
            && Date.now() - pendingNavigation.startedAt >= 350
            && !hasStartedNavigation
            && staleRuntimeUrl !== undefined) {
            const now = Date.now();
            if (pendingNavigation.runtimeConfirmationAttemptedAt === undefined
                || now - pendingNavigation.runtimeConfirmationAttemptedAt >= 100) {
                this.pendingNavigations.set(tabId, {
                    ...pendingNavigation,
                    runtimeConfirmationAttemptedAt: now,
                });

                const confirmedCurrentRuntimeAtStaleUrl = await this.hasConfirmedCurrentRuntimeAtUrlAsync(
                    numericTabId,
                    staleRuntimeUrl,
                );

                if (confirmedCurrentRuntimeAtStaleUrl) {
                    if (pendingNavigation.command === 'Navigate' && pendingNavigation.retryCount === 1) {
                        try {
                            await this.tryNavigateCurrentRuntimeAsync(numericTabId, expectedUrl);
                            await this.updateTabForNavigateAsync(
                                numericTabId,
                                expectedUrl,
                                this.resolveNavigationWindowId(browserTab, context),
                                true,
                            );
                            this.pendingNavigations.set(tabId, {
                                ...pendingNavigation,
                                lastRetryAt: now,
                                retryCount: 2,
                                runtimeConfirmationAttemptedAt: now,
                            });
                        } catch {
                        }
                    }

                    return {
                        pendingNavigation: this.pendingNavigations.get(tabId) ?? pendingNavigation,
                        browserTab,
                    };
                }
            }
        }

        const pendingNavigationRetryStartedAt = pendingNavigation.lastRetryAt ?? pendingNavigation.startedAt;
        const pendingNavigationAgeMs = Date.now() - pendingNavigationRetryStartedAt;
        const canLateReissueStaleNavigate = pendingNavigation.command === 'Navigate'
            && pendingNavigation.retryCount < maxLateNavigateRetryCount
            && pendingNavigationAgeMs >= 350;
        if (expectedUrl !== undefined
            && context !== undefined
            && runtime !== null
            && canLateReissueStaleNavigate
            && hasExpectedPendingBrowserUrl
            && !hasExpectedBrowserUrl
            && staleRuntimeUrl !== undefined) {
            const now = Date.now();
            if (pendingNavigation.runtimeConfirmationAttemptedAt === undefined
                || now - pendingNavigation.runtimeConfirmationAttemptedAt >= 100) {
                this.pendingNavigations.set(tabId, {
                    ...pendingNavigation,
                    runtimeConfirmationAttemptedAt: now,
                });

                const confirmedCurrentRuntimeAtStaleUrl = await this.hasConfirmedCurrentRuntimeAtUrlAsync(
                    numericTabId,
                    staleRuntimeUrl,
                );

                if (confirmedCurrentRuntimeAtStaleUrl) {
                    try {
                        await this.tryNavigateCurrentRuntimeAsync(numericTabId, expectedUrl);
                        browserTab = await this.updateTabForNavigateAsync(
                            numericTabId,
                            expectedUrl,
                            this.resolveNavigationWindowId(browserTab, context),
                            true,
                        );
                        this.pendingNavigations.set(tabId, {
                            ...pendingNavigation,
                            lastRetryAt: now,
                            retryCount: pendingNavigation.retryCount + 1,
                            runtimeConfirmationAttemptedAt: now,
                        });
                    } catch {
                    }

                    return {
                        pendingNavigation: this.pendingNavigations.get(tabId) ?? pendingNavigation,
                        browserTab,
                    };
                }
            }
        }

        let confirmedCurrentRuntimeAtExpectedUrl = false;
        if (hasExpectedBrowserUrl
            && hasReadyContextAtExpectedUrl
            && runtime !== null
            && pendingNavigation.previousEndpoint !== null
            && runtime.endpoint === pendingNavigation.previousEndpoint) {
            const now = Date.now();
            if (pendingNavigation.runtimeConfirmationAttemptedAt === undefined
                || now - pendingNavigation.runtimeConfirmationAttemptedAt >= 100) {
                this.pendingNavigations.set(tabId, {
                    ...pendingNavigation,
                    runtimeConfirmationAttemptedAt: now,
                });

                confirmedCurrentRuntimeAtExpectedUrl = await this.hasConfirmedCurrentRuntimeAtUrlAsync(
                    numericTabId,
                    expectedUrl,
                );
            }
        }

        if (hasExpectedBrowserUrl && hasReadyContextAtExpectedUrl && (hasRebootstrappedRuntime || confirmedCurrentRuntimeAtExpectedUrl)) {
            this.pendingNavigations.delete(tabId);
            return {
                pendingNavigation: undefined,
                browserTab,
            };
        }

        return {
            pendingNavigation: this.pendingNavigations.get(tabId) ?? pendingNavigation,
            browserTab,
        };
    }

    private async hasObservedNavigateStartAsync(
        tabId: number,
        expectedUrl: string,
        previousUrl: string | undefined,
        initialTab: BrowserTab,
    ): Promise<boolean> {
        let observedTab = initialTab;

        for (let attempt = 0; attempt < 5; attempt++) {
            if (hasBrowserTabStartedNavigate(observedTab, expectedUrl, previousUrl)) {
                return true;
            }

            await new Promise<void>((resolve) => {
                setTimeout(resolve, 50);
            });

            try {
                observedTab = await getTab(this.runtime, this.browserHost.tabs, tabId);
            } catch {
                return true;
            }
        }

        return hasBrowserTabStartedNavigate(observedTab, expectedUrl, previousUrl);
    }

    private async updateTabForNavigateAsync(
        tabId: number,
        url: string,
        windowId: number | undefined,
        active: boolean,
    ): Promise<BrowserTab> {
        if (active) {
            await this.focusNavigationWindowAsync(windowId);
            return updateTab(this.runtime, this.browserHost.tabs, tabId, { url, active: true });
        }

        return updateTab(this.runtime, this.browserHost.tabs, tabId, { url });
    }

    private resolveNavigationWindowId(
        browserTab: BrowserTab | null | undefined,
        context: TabContextEnvelope | undefined,
    ): number | undefined {
        if (browserTab !== null && browserTab !== undefined && typeof browserTab.windowId === 'number') {
            return browserTab.windowId;
        }

        const numericWindowId = Number(context?.windowId);
        if (!Number.isInteger(numericWindowId) || numericWindowId <= 0) {
            return undefined;
        }

        return numericWindowId;
    }

    private async focusNavigationWindowAsync(windowId: number | undefined): Promise<void> {
        if (windowId === undefined) {
            return;
        }

        try {
            await updateWindow(this.runtime, this.browserHost.windows, windowId, { focused: true });
        } catch {
        }
    }

    private async tryNavigateCurrentRuntimeAsync(tabId: number, expectedUrl: string): Promise<boolean> {
        try {
            const result = await evaluateMainWorldScript(
                this.browserHost,
                this.runtime,
                tabId,
                `(() => { globalThis.location.replace(${JSON.stringify(expectedUrl)}); return 'navigate-requested'; })()`,
                false,
                false,
            );
            return result === 'navigate-requested';
        } catch {
            return false;
        }
    }

    private async hasConfirmedCurrentRuntimeAtUrlAsync(tabId: number, expectedUrl: string | undefined): Promise<boolean> {
        return (await this.inspectCurrentRuntimeAtUrlAsync(tabId, expectedUrl)).confirmed;
    }

    private async inspectCurrentRuntimeAtUrlAsync(tabId: number, expectedUrl: string | undefined): Promise<RuntimeUrlInspection> {
        if (expectedUrl === undefined) {
            return {
                confirmed: true,
                status: 'no-expected-url',
            };
        }

        let resultText: unknown;
        try {
            resultText = await evaluateMainWorldScript(
                this.browserHost,
                this.runtime,
                tabId,
                '(() => JSON.stringify({ href: globalThis.location?.href ?? "", readyState: document.readyState ?? "" }))()',
                true,
                false,
                () => {
                },
            );
        } catch (error) {
            return {
                confirmed: false,
                status: 'evaluate-error',
                error: toErrorMessage(error),
            };
        }

        if (typeof resultText !== 'string' || resultText.trim().length === 0) {
            return {
                confirmed: false,
                status: 'empty-result',
            };
        }

        let result: unknown;
        try {
            result = JSON.parse(resultText);
        } catch (error) {
            return {
                confirmed: false,
                status: 'parse-error',
                error: toErrorMessage(error),
            };
        }

        if (!isRecord(result)) {
            return {
                confirmed: false,
                status: 'invalid-payload',
            };
        }

        const href = readNonEmptyString(result.href);
        const readyState = readNonEmptyString(result.readyState);
        if (href === undefined || readyState === undefined) {
            return {
                confirmed: false,
                status: 'missing-runtime-state',
                href,
                readyState,
            };
        }

        if (!areEquivalentUrls(href, expectedUrl)) {
            return {
                confirmed: false,
                status: 'url-mismatch',
                href,
                readyState,
            };
        }

        const confirmed = readyState === 'interactive' || readyState === 'complete';
        return {
            confirmed,
            status: confirmed ? 'confirmed' : 'document-not-ready',
            href,
            readyState,
        };
    }

    private async inspectPendingNavigationReapplyAsync(
        tabId: string,
        pendingNavigation: PendingNavigationState,
    ): Promise<PendingNavigationReapplyInspection> {
        const numericTabId = Number(tabId);
        if (!Number.isInteger(numericTabId) || numericTabId <= 0) {
            return {
                canReapply: false,
                runtimeCheckStatus: 'invalid-tab-id',
            };
        }

        let browserTab: BrowserTab;
        try {
            browserTab = await getTab(this.runtime, this.browserHost.tabs, numericTabId);
        } catch {
            return {
                canReapply: false,
                runtimeCheckStatus: 'browser-tab-unavailable',
            };
        }

        if (hasPendingNavigationReachedTarget(browserTab, pendingNavigation.expectedUrl)) {
            return {
                canReapply: true,
                runtimeCheckStatus: 'browser-target-reached',
            };
        }

        if (pendingNavigation.command !== 'Navigate') {
            return {
                canReapply: false,
                runtimeCheckStatus: 'command-not-navigate',
            };
        }

        const expectedUrl = pendingNavigation.expectedUrl;
        if (expectedUrl === undefined) {
            return {
                canReapply: false,
                runtimeCheckStatus: 'missing-expected-url',
            };
        }

        const contextUrl = readNonEmptyString(this.getTabContext(tabId)?.url);
        if (contextUrl === undefined) {
            return {
                canReapply: false,
                runtimeCheckStatus: 'missing-context-url',
            };
        }

        if (!areEquivalentUrls(contextUrl, expectedUrl)) {
            return {
                canReapply: false,
                runtimeCheckStatus: 'context-url-mismatch',
            };
        }

        const browserTabPendingUrl = readNonEmptyString(browserTab.pendingUrl);
        const hasExpectedPendingBrowserUrl = browserTabPendingUrl !== undefined
            && areEquivalentUrls(browserTabPendingUrl, expectedUrl);
        const hasStartedNavigation = hasBrowserTabStartedNavigate(browserTab, expectedUrl, pendingNavigation.previousUrl);
        if (hasStartedNavigation
            && hasExpectedPendingBrowserUrl
            && Date.now() - pendingNavigation.startedAt < 200) {
            return {
                canReapply: false,
                runtimeCheckStatus: 'navigate-in-flight-grace',
            };
        }

        const runtimeInspection = await this.inspectCurrentRuntimeAtUrlAsync(numericTabId, expectedUrl);
        return {
            canReapply: runtimeInspection.confirmed,
            runtimeCheckStatus: runtimeInspection.status,
            runtimeHref: runtimeInspection.href,
            runtimeReadyState: runtimeInspection.readyState,
            runtimeCheckError: runtimeInspection.error,
        };
    }

    private async canReapplyPendingNavigationContextAsync(
        tabId: string,
        pendingNavigation: PendingNavigationState,
    ): Promise<boolean> {
        return (await this.inspectPendingNavigationReapplyAsync(tabId, pendingNavigation)).canReapply;
    }

    private emitPendingNavigationGateDebugEvent(kind: string, tabId: string, details: JsonRecord): void {
        const signature = this.createPendingNavigationGateDebugSignature(details);
        const key = `${kind}:${tabId}`;
        if (this.pendingNavigationGateDiagnostics.get(key) === signature) {
            return;
        }

        this.pendingNavigationGateDiagnostics.set(key, signature);
        emitBackgroundDebugEvent(this.config, kind, details);
    }

    private createPendingNavigationGateDebugSignature(details: JsonRecord): string {
        return JSON.stringify(Object.fromEntries(
            Object.entries(details)
                .filter(([key]) => key !== 'ageMs')
                .sort(([left], [right]) => left.localeCompare(right)),
        ));
    }

    private clearPendingNavigationGateDebugState(tabId: string): void {
        this.pendingNavigationGateDiagnostics.delete(`pending-navigation-hidden-port:${tabId}`);
        this.pendingNavigationGateDiagnostics.delete(`pending-navigation-context-apply-deferred:${tabId}`);
    }

    private createPendingNavigationGateDebugDetails(
        phase: string,
        tabId: string,
        pendingNavigation: PendingNavigationState,
        context: TabContextEnvelope | undefined,
        browserTab: BrowserTab | null,
        canReapplyPendingNavigationContext: boolean,
        pendingNavigationReapplyInspection?: PendingNavigationReapplyInspection,
    ): JsonRecord {
        const expectedUrl = pendingNavigation.expectedUrl;
        const browserTabUrl = readNonEmptyString(browserTab?.url);
        const browserTabPendingUrl = readNonEmptyString(browserTab?.pendingUrl);
        const browserTabStatus = readNonEmptyString(browserTab?.status);
        const contextUrl = readNonEmptyString(context?.url);
        const hasExpectedBrowserUrl = browserTab !== null && hasPendingNavigationReachedTarget(browserTab, expectedUrl);
        const hasExpectedPendingBrowserUrl = expectedUrl !== undefined
            && browserTabPendingUrl !== undefined
            && areEquivalentUrls(browserTabPendingUrl, expectedUrl);
        const hasStartedNavigation = browserTab !== null
            && expectedUrl !== undefined
            && hasBrowserTabStartedNavigate(browserTab, expectedUrl, pendingNavigation.previousUrl);
        const contextMatchesExpectedUrl = expectedUrl === undefined
            || (contextUrl !== undefined && areEquivalentUrls(contextUrl, expectedUrl));

        const details: JsonRecord = {
            phase,
            tabId,
            command: pendingNavigation.command,
            retryCount: pendingNavigation.retryCount,
            ageMs: Math.max(0, Date.now() - pendingNavigation.startedAt),
            canReapplyPendingNavigationContext,
            expectedUrl: expectedUrl ?? null,
            previousUrl: pendingNavigation.previousUrl ?? null,
            contextId: context?.contextId ?? null,
            contextUrl: contextUrl ?? null,
            contextIsReady: context?.isReady === true,
            contextMatchesExpectedUrl,
            hasExtendedContext: context !== undefined && hasExtendedTabContext(context),
            hasDeviceEmulationContext: hasDeviceEmulationContext(context),
            hasGeolocationContext: context?.geolocation !== undefined,
            hasPrivacySignals: context?.doNotTrack !== undefined || context?.globalPrivacyControl !== undefined,
            hasVirtualMediaContext: context?.virtualMediaDevices !== undefined,
            browserTabUrl: browserTabUrl ?? null,
            browserTabPendingUrl: browserTabPendingUrl ?? null,
            browserTabStatus: browserTabStatus ?? null,
            hasStartedNavigation,
            hasExpectedBrowserUrl,
            hasExpectedPendingBrowserUrl,
            transportConnected: this.transport.connected,
        };

        if (pendingNavigationReapplyInspection?.runtimeCheckStatus !== undefined) {
            details.runtimeCheckStatus = pendingNavigationReapplyInspection.runtimeCheckStatus;
        }

        if (pendingNavigationReapplyInspection?.runtimeHref !== undefined) {
            details.runtimeHref = pendingNavigationReapplyInspection.runtimeHref;
        }

        if (pendingNavigationReapplyInspection?.runtimeReadyState !== undefined) {
            details.runtimeReadyState = pendingNavigationReapplyInspection.runtimeReadyState;
        }

        if (pendingNavigationReapplyInspection?.runtimeCheckError !== undefined) {
            details.runtimeCheckError = pendingNavigationReapplyInspection.runtimeCheckError;
        }

        return details;
    }

    private updateTrackedTabContext(tabId: string, context: TabContextEnvelope): void {
        this.tabContexts.set(tabId, context);

        if (this.tabs.get(tabId) !== null) {
            this.tabs.markReady(tabId, context);
        }
    }

    private async refreshContextFromBrowserTabAsync(
        tabId: string,
        context: TabContextEnvelope,
        fallbackWindowId?: string,
    ): Promise<TabContextEnvelope> {
        const numericTabId = Number(tabId);
        if (!Number.isInteger(numericTabId) || numericTabId <= 0) {
            return context;
        }

        let browserTab: BrowserTab;
        try {
            browserTab = await getTab(this.runtime, this.browserHost.tabs, numericTabId);
        } catch {
            return context;
        }

        const nextWindowId = typeof browserTab.windowId === 'number'
            ? browserTab.windowId.toString()
            : fallbackWindowId ?? context.windowId;
        const nextUrl = typeof browserTab.url === 'string' && browserTab.url.trim().length > 0
            ? browserTab.url
            : context.url;

        if (nextWindowId === context.windowId && nextUrl === context.url) {
            return context;
        }

        return {
            ...context,
            ...(nextWindowId !== undefined ? { windowId: nextWindowId } : {}),
            ...(nextUrl !== undefined ? { url: nextUrl } : {}),
        };
    }

    private resolveNavigationFulfillmentSupport(tabId: string, details: WebRequestDetails): boolean {
        if (!isNavigateRequestType(details.type)) {
            return false;
        }

        return this.resolveNavigationProxyRouteToken(this.getTabContext(tabId)) !== undefined;
    }

    private tryPostInterceptedRequest(route: InterceptionRoute, details: WebRequestDetails, requestHeaders: HeaderLike[]): BlockingInterceptionDecision | null {
        return postBlockingBridgeJson<BlockingInterceptionDecision>(
            this.config,
            '/intercept',
            {
                requestId: details.requestId ?? createInternalMessageId('request'),
                tabId: route.tabId,
                url: details.url ?? '',
                method: details.method ?? 'GET',
                type: details.type ?? 'other',
                supportsNavigationFulfillment: this.resolveNavigationFulfillmentSupport(route.tabId, details),
                headers: headersToObject(requestHeaders),
                timestamp: normalizeEventTimestamp(details.timeStamp),
            },
        );
    }

    private tryPostInterceptedResponse(route: InterceptionRoute, details: WebRequestDetails, responseHeaders: HeaderLike[]): BlockingInterceptionDecision | null {
        return postBlockingBridgeJson<BlockingInterceptionDecision>(
            this.config,
            '/intercept-response',
            {
                requestId: details.requestId ?? createInternalMessageId('response'),
                tabId: route.tabId,
                url: details.url ?? '',
                method: details.method ?? 'GET',
                type: details.type ?? 'other',
                statusCode: details.statusCode,
                reasonPhrase: details.statusLine ?? '',
                headers: headersToObject(responseHeaders),
                timestamp: normalizeEventTimestamp(details.timeStamp),
            },
        );
    }

    private tryPostObservedRequestHeaders(route: InterceptionRoute, details: WebRequestDetails, requestHeaders: HeaderLike[]): void {
        postBlockingBridgeJson(
            this.config,
            '/observed-request-headers',
            {
                requestId: details.requestId ?? createInternalMessageId('request-headers'),
                tabId: route.tabId,
                url: details.url ?? '',
                method: details.method ?? 'GET',
                type: details.type ?? 'other',
                headers: headersToObject(requestHeaders),
                timestamp: normalizeEventTimestamp(details.timeStamp),
            },
            false,
        );
    }

    private resolveRedirectUrlForRequestDecision(decision: BlockingInterceptionDecision): string | null {
        if (typeof decision.url === 'string' && decision.url.trim().length > 0) {
            return decision.url;
        }

        if (decision.action !== 'fulfill' || typeof decision.bodyBase64 !== 'string' || decision.bodyBase64.length === 0) {
            return null;
        }

        const contentType = readHeaderValue(decision.responseHeaders, 'Content-Type') ?? 'text/plain; charset=utf-8';
        return `data:${contentType};base64,${decision.bodyBase64}`;
    }

    private tryReplaceResponseBody(requestId: string, bodyBase64: string | undefined): void {
        if (typeof bodyBase64 !== 'string' || bodyBase64.length === 0) {
            return;
        }

        const filterResponseData = this.browserHost.webRequest?.filterResponseData;
        if (typeof filterResponseData !== 'function') {
            return;
        }

        try {
            const filter = filterResponseData(requestId);
            const bodyBytes = decodeBase64(bodyBase64);

            filter.ondata = () => {
                // Ignore the original body and replace it at stop.
            };

            filter.onstop = () => {
                try {
                    filter.write(bodyBytes);
                } finally {
                    filter.close();
                }
            };

            filter.onerror = () => {
                try {
                    filter.close();
                } catch {
                    // Ignore close failures on teardown.
                }
            };
        } catch {
            // Best-effort body replacement only.
        }
    }

    private createDefaultTabContext(tabId: string, windowId?: string, url?: string): TabContextEnvelope {
        return createDefaultTabContextEnvelope(this.sessionId, tabId, createInternalMessageId, windowId, url);
    }

    private ensureCookieCommandContext(
        tabId: string,
        requestedContextId: string | undefined,
        windowId?: string,
        url?: string,
    ): TabContextEnvelope | undefined {
        const existingContext = this.getTabContext(tabId);

        if (requestedContextId === undefined || requestedContextId.trim().length === 0) {
            return existingContext;
        }

        const nextContext = createCookieCommandContext(
            tabId,
            requestedContextId,
            () => this.createDefaultTabContext(tabId, windowId, url),
            existingContext,
            windowId,
            url,
        );

        this.tabContexts.set(tabId, nextContext);
        ensureVirtualCookieStore(this.virtualCookies, nextContext.contextId);

        if (this.tabs.get(tabId) !== null) {
            this.tabs.markReady(tabId, nextContext);
        }

        return nextContext;
    }

    private async syncDocumentCookieSurface(
        tabId: string,
        context: TabContextEnvelope | undefined,
        url?: string,
    ): Promise<void> {
        if (context === undefined || this.tabs.get(tabId) === null) {
            return;
        }

        const effectiveUrl = typeof url === 'string' && url.trim().length > 0
            ? url
            : context.url;
        const cookieUrl = typeof effectiveUrl === 'string' && effectiveUrl.trim().length > 0
            ? effectiveUrl
            : null;
        const cookieHeader = cookieUrl !== null
            ? buildVirtualCookieHeader(
                getVisibleVirtualCookies(
                    ensureVirtualCookieStore(this.virtualCookies, context.contextId),
                    cookieUrl,
                ),
            ) ?? ''
            : '';

        try {
            const result = await this.executeInMainWorld(
                tabId,
                createInternalMessageId('sync_document_cookie'),
                buildDocumentCookieSyncScript(cookieHeader),
                true,
            );

            if (result.status === 'err') {
                emitBackgroundDebugEvent(this.config, 'cookie-sync-failed', {
                    tabId,
                    contextId: context.contextId,
                    error: result.error,
                });
            } else if (result.value === 'null') {
                emitBackgroundDebugEvent(this.config, 'cookie-sync-failed', {
                    tabId,
                    contextId: context.contextId,
                    error: 'document-cookie-sync-returned-null',
                });
            }
        } catch (error) {
            emitBackgroundDebugEvent(this.config, 'cookie-sync-failed', {
                tabId,
                contextId: context.contextId,
                error: toErrorMessage(error),
            });
        }
    }
}

function buildDocumentCookieSyncScript(cookieHeader: string): string {
    return `(() => {
const syncCookieHeader = globalThis.__atomSyncDocumentCookieHeader;
if (typeof syncCookieHeader === 'function') {
    return syncCookieHeader(${JSON.stringify(cookieHeader)});
}

return '';
})();`;
}

function headersToObject(headers: readonly HeaderLike[] | undefined): Record<string, string> | undefined {
    if (!Array.isArray(headers) || headers.length === 0) {
        return undefined;
    }

    const result: Record<string, string> = {};
    for (const header of headers) {
        if (typeof header?.name !== 'string' || header.name.trim().length === 0) {
            continue;
        }

        result[header.name] = header.value ?? '';
    }

    return Object.keys(result).length > 0 ? result : undefined;
}

function toMutableHeaders(headers: readonly HeaderLike[] | undefined): MutableHeaderLike[] {
    return cloneHeaders(headers).filter((header) => typeof header.name === 'string' && header.name.trim().length > 0);
}

function applyHeaderOverrides(headers: MutableHeaderLike[], overrides: HeaderMap | undefined): boolean {
    if (!isNonEmptyRecord(overrides)) {
        return false;
    }

    let modified = false;
    for (const [name, value] of Object.entries(overrides)) {
        const existing = headers.find((header) => typeof header.name === 'string' && header.name.toLowerCase() === name.toLowerCase());
        if (existing) {
            if (existing.value !== value) {
                existing.value = value;
                modified = true;
            }

            continue;
        }

        headers.push({ name, value });
        modified = true;
    }

    return modified;
}

function readHeaderValue(headers: HeaderMap | undefined, name: string): string | undefined {
    if (!isNonEmptyRecord(headers)) {
        return undefined;
    }

    const expectedName = name.toLowerCase();
    for (const [headerName, value] of Object.entries(headers)) {
        if (headerName.toLowerCase() === expectedName && value.trim().length > 0) {
            return value;
        }
    }

    return undefined;
}

function cloneHeaderMap(headers: HeaderMap): HeaderMap {
    const clone: HeaderMap = {};
    for (const [name, value] of Object.entries(headers)) {
        clone[name] = value;
    }

    return clone;
}

function isNonEmptyRecord(value: unknown): value is HeaderMap {
    return isRecord(value) && Object.keys(value).length > 0;
}

function postBlockingBridgeJson<TResponse = void>(
    config: RuntimeConfig | null,
    path: string,
    payload: Record<string, unknown>,
    expectJsonResponse = true,
): TResponse | null {
    if (config === null || typeof XMLHttpRequest !== 'function') {
        return null;
    }

    try {
        const request = new XMLHttpRequest();
        request.open('POST', createBridgeUtilityUrl(config, path), false);
        request.setRequestHeader('Content-Type', 'application/json');
        request.send(JSON.stringify(payload));

        if (request.status < 200 || request.status >= 300) {
            return null;
        }

        if (!expectJsonResponse) {
            return undefined as TResponse;
        }

        if (typeof request.responseText !== 'string' || request.responseText.trim().length === 0) {
            return null;
        }

        return JSON.parse(request.responseText) as TResponse;
    } catch {
        return null;
    }
}

function createBridgeUtilityUrl(config: RuntimeConfig, path: string): string {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    return `http://${config.host}:${config.port}${normalizedPath}?secret=${encodeURIComponent(config.secret)}`;
}

function decodeBase64(value: string): Uint8Array {
    if (typeof globalThis.atob === 'function') {
        const binary = globalThis.atob(value);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index += 1) {
            bytes[index] = binary.charCodeAt(index);
        }

        return bytes;
    }

    const bufferConstructor = (globalThis as { Buffer?: { from(value: string, encoding: string): Uint8Array } }).Buffer;
    if (bufferConstructor !== undefined) {
        return bufferConstructor.from(value, 'base64');
    }

    throw new Error('Base64 decode is unavailable in the current runtime');
}

function readBooleanPayload(payload: unknown, propertyName: string): boolean {
    if (!isRecord(payload)) {
        return false;
    }

    return payload[propertyName] === true;
}

function readStringArrayPayload(payload: unknown, propertyName: string): string[] {
    if (!isRecord(payload)) {
        return [];
    }

    const value = payload[propertyName];
    if (!Array.isArray(value)) {
        return [];
    }

    return value
        .filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
        .map((item) => item.trim());
}

function compileInterceptionPattern(pattern: string): RegExp {
    const escaped = pattern.replace(/[.+^${}()|[\]\\]/g, '\\$&');
    return new RegExp(`^${escaped.replace(/\*/g, '.*')}$`, 'i');
}

function normalizeEventTimestamp(timestamp: number | undefined): number {
    return typeof timestamp === 'number' && Number.isFinite(timestamp)
        ? Math.trunc(timestamp)
        : Date.now();
}

function isNavigateRequestType(requestType: string | undefined): boolean {
    return requestType === 'main_frame';
}

function isBridgeInternalUrl(url: string, config: RuntimeConfig | null): boolean {
    if (config === null) {
        return false;
    }

    return url.startsWith(`http://${config.host}:${config.port}/`);
}

export async function bootstrapBackgroundRuntime(): Promise<BackgroundRuntimeHost> {
    const runtimeState = globalThis as BackgroundRuntimeGlobalState;
    if (runtimeState.__atomBackgroundRuntimeHost !== undefined) {
        return runtimeState.__atomBackgroundRuntimeHost;
    }

    const host = new BackgroundRuntimeHost();
    runtimeState.__atomBackgroundRuntimeHost = host;
    await host.start();
    return host;
}

function isRecord(value: unknown): value is JsonRecord {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function createDiscoveryUrl(config: RuntimeConfig): string {
    return `http://${config.host}:${config.port}/`;
}

function isCompletedBrowserUrlReadyForContext(browserUrl: string | undefined, context: TabContextEnvelope): boolean {
    const expectedUrl = readNonEmptyString(context.url);
    if (expectedUrl === undefined) {
        return true;
    }

    if (browserUrl === undefined) {
        return false;
    }

    return areEquivalentUrls(browserUrl, expectedUrl);
}

function hasBrowserTabStartedNavigate(tab: BrowserTab, expectedUrl: string, previousUrl: string | undefined): boolean {
    const pendingUrl = readNonEmptyString(tab.pendingUrl);
    if (pendingUrl !== undefined && areEquivalentUrls(pendingUrl, expectedUrl)) {
        return true;
    }

    if (tab.status !== undefined && tab.status !== 'complete') {
        return true;
    }

    const currentUrl = readNonEmptyString(tab.url);
    if (currentUrl === undefined || !areEquivalentUrls(currentUrl, expectedUrl)) {
        return false;
    }

    return previousUrl === undefined || !areEquivalentUrls(currentUrl, previousUrl);
}

function hasPendingNavigationReachedTarget(tab: BrowserTab, expectedUrl: string | undefined): boolean {
    if (tab.status !== 'complete') {
        return false;
    }

    if (expectedUrl === undefined) {
        return true;
    }

    const browserUrl = readNonEmptyString(tab.url);
    return browserUrl !== undefined && areEquivalentUrls(browserUrl, expectedUrl);
}

function readNonEmptyString(value: unknown): string | undefined {
    return typeof value === 'string' && value.trim().length > 0
        ? value
        : undefined;
}

function areEquivalentUrls(left: string, right: string): boolean {
    if (left === right) {
        return true;
    }

    try {
        return new URL(left).href === new URL(right).href;
    } catch {
        return false;
    }
}

function resolveReadyTabContext(existing: TabContextEnvelope | undefined, next: TabContextEnvelope): TabContextEnvelope {
    if (existing === undefined) {
        return next;
    }

    const existingRevision = existing.readyAt ?? existing.connectedAt;
    const nextRevision = next.readyAt ?? next.connectedAt;

    if (nextRevision < existingRevision) {
        return existing;
    }

    if (existing.contextId !== next.contextId
        && hasExtendedTabContext(existing)
        && !hasExtendedTabContext(next)) {
        return existing;
    }

    if (existing.contextId !== next.contextId) {
        return next;
    }

    return {
        ...next,
        windowId: next.windowId ?? existing.windowId,
        url: next.url ?? existing.url,
        proxy: next.proxy !== undefined ? next.proxy : existing.proxy,
        navigationInterceptionMode: next.navigationInterceptionMode ?? existing.navigationInterceptionMode,
        navigationProxyRouteToken: next.navigationProxyRouteToken ?? existing.navigationProxyRouteToken,
        readyAt: next.readyAt ?? existing.readyAt,
        userAgent: next.userAgent ?? existing.userAgent,
        platform: next.platform ?? existing.platform,
        locale: next.locale ?? existing.locale,
        timezone: next.timezone ?? existing.timezone,
        languages: next.languages ?? existing.languages,
        clientHints: next.clientHints ?? existing.clientHints,
        viewport: next.viewport ?? existing.viewport,
        deviceScaleFactor: next.deviceScaleFactor ?? existing.deviceScaleFactor,
        hardwareConcurrency: next.hardwareConcurrency ?? existing.hardwareConcurrency,
        deviceMemory: next.deviceMemory ?? existing.deviceMemory,
        geolocation: next.geolocation ?? existing.geolocation,
        doNotTrack: next.doNotTrack ?? existing.doNotTrack,
        globalPrivacyControl: next.globalPrivacyControl ?? existing.globalPrivacyControl,
        maxTouchPoints: next.maxTouchPoints ?? existing.maxTouchPoints,
        isMobile: next.isMobile ?? existing.isMobile,
        hasTouch: next.hasTouch ?? existing.hasTouch,
        virtualMediaDevices: next.virtualMediaDevices ?? existing.virtualMediaDevices,
    };
}

function hasExtendedTabContext(context: TabContextEnvelope): boolean {
    return context.proxy !== undefined
        || context.navigationInterceptionMode !== undefined
        || context.navigationProxyRouteToken !== undefined
        || context.userAgent !== undefined
        || context.platform !== undefined
        || context.locale !== undefined
        || context.timezone !== undefined
        || context.languages !== undefined
        || context.clientHints !== undefined
        || context.viewport !== undefined
        || context.deviceScaleFactor !== undefined
        || context.hardwareConcurrency !== undefined
        || context.deviceMemory !== undefined
        || context.geolocation !== undefined
        || context.doNotTrack !== undefined
        || context.globalPrivacyControl !== undefined
        || context.maxTouchPoints !== undefined
        || context.isMobile !== undefined
        || context.hasTouch !== undefined
        || context.virtualMediaDevices !== undefined;
}

function hasDeviceEmulationContext(context: TabContextEnvelope | undefined): context is TabContextEnvelope {
    return context !== undefined
        && (context.userAgent !== undefined
            || context.clientHints !== undefined
            || context.viewport !== undefined
            || context.deviceScaleFactor !== undefined
            || context.maxTouchPoints !== undefined
            || context.isMobile !== undefined
            || context.hasTouch !== undefined);
}

function createMainFrameDeviceRequestDebugDetails(
    details: WebRequestDetails,
    context: TabContextEnvelope | undefined,
    headers: readonly HeaderLike[] | undefined,
): JsonRecord | undefined {
    if (details.type !== 'main_frame' || !hasDeviceEmulationContext(context)) {
        return undefined;
    }

    return {
        tabId: context.tabId,
        contextId: context.contextId,
        url: details.url,
        method: details.method,
        requestType: details.type,
        contextUserAgent: context.userAgent,
        contextViewportWidth: context.viewport?.width,
        contextViewportHeight: context.viewport?.height,
        contextDeviceScaleFactor: context.deviceScaleFactor,
        contextIsMobile: context.isMobile,
        contextHasTouch: context.hasTouch,
        contextMaxTouchPoints: context.maxTouchPoints,
        contextClientHintsMobile: context.clientHints?.mobile,
        headerUserAgent: getHeaderValue(headers, 'User-Agent'),
        headerSecChUa: getHeaderValue(headers, 'Sec-CH-UA'),
        headerSecChUaFullVersionList: getHeaderValue(headers, 'Sec-CH-UA-Full-Version-List'),
        headerSecChUaPlatform: getHeaderValue(headers, 'Sec-CH-UA-Platform'),
        headerSecChUaPlatformVersion: getHeaderValue(headers, 'Sec-CH-UA-Platform-Version'),
        headerSecChUaMobile: getHeaderValue(headers, 'Sec-CH-UA-Mobile'),
        headerSecChUaArch: getHeaderValue(headers, 'Sec-CH-UA-Arch'),
        headerSecChUaModel: getHeaderValue(headers, 'Sec-CH-UA-Model'),
        headerSecChUaBitness: getHeaderValue(headers, 'Sec-CH-UA-Bitness'),
    };
}

function createMainFrameDeviceResponseDebugDetails(
    details: WebRequestDetails,
    context: TabContextEnvelope | undefined,
    headers: readonly HeaderLike[] | undefined,
): JsonRecord | undefined {
    if (details.type !== 'main_frame' || !hasDeviceEmulationContext(context)) {
        return undefined;
    }

    return {
        tabId: context.tabId,
        contextId: context.contextId,
        url: details.url,
        requestType: details.type,
        statusCode: details.statusCode,
        statusLine: details.statusLine,
        contentType: getHeaderValue(headers, 'Content-Type'),
        location: getHeaderValue(headers, 'Location'),
    };
}

function getHeaderValue(headers: readonly HeaderLike[] | undefined, name: string): string | undefined {
    if (!Array.isArray(headers)) {
        return undefined;
    }

    const header = headers.find((candidate) => isHeaderNamed(candidate, name));
    return typeof header?.value === 'string' && header.value.length > 0
        ? header.value
        : undefined;
}

function requireTabId(message: BridgeMessage): number {
    if (message.tabId === undefined) {
        throw new Error('Команда моста не содержит идентификатор вкладки');
    }

    const tabId = Number(message.tabId);
    if (!Number.isInteger(tabId) || tabId <= 0) {
        throw new Error('Команда моста содержит неверный идентификатор вкладки');
    }

    return tabId;
}

function toJsonCookie(cookie: BrowserCookie): JsonRecord {
    const payload: JsonRecord = {
        name: cookie.name,
        value: cookie.value ?? '',
        domain: cookie.domain ?? '',
        path: cookie.path ?? '/',
        secure: cookie.secure ?? false,
        httpOnly: cookie.httpOnly ?? false,
    };

    if (typeof cookie.expirationDate === 'number' && Number.isFinite(cookie.expirationDate)) {
        payload.expires = Math.trunc(cookie.expirationDate);
    }

    return payload;
}


function toJsonContext(context: TabContextEnvelope | undefined): JsonRecord | undefined {
    if (context === undefined) {
        return undefined;
    }

    const jsonContext: JsonRecord = {
        sessionId: context.sessionId,
        contextId: context.contextId,
        tabId: context.tabId,
        connectedAt: context.connectedAt,
        isReady: context.isReady,
    };

    if (context.windowId !== undefined) {
        jsonContext.windowId = context.windowId;
    }
    if (context.url !== undefined) {
        jsonContext.url = context.url;
    }
    if (context.proxy !== undefined) {
        jsonContext.proxy = context.proxy;
    }
    if (context.navigationInterceptionMode !== undefined) {
        jsonContext.navigationInterceptionMode = context.navigationInterceptionMode;
    }
    if (context.navigationProxyRouteToken !== undefined) {
        jsonContext.navigationProxyRouteToken = context.navigationProxyRouteToken;
    }
    if (context.readyAt !== undefined) {
        jsonContext.readyAt = context.readyAt;
    }

    if (context.userAgent !== undefined) {
        jsonContext.userAgent = context.userAgent;
    }
    if (context.platform !== undefined) {
        jsonContext.platform = context.platform;
    }
    if (context.locale !== undefined) {
        jsonContext.locale = context.locale;
    }
    if (context.timezone !== undefined) {
        jsonContext.timezone = context.timezone;
    }
    if (context.languages !== undefined) {
        jsonContext.languages = context.languages;
    }
    if (context.clientHints !== undefined) {
        const clientHints = toJsonClientHints(context.clientHints);
        if (clientHints !== undefined) {
            jsonContext.clientHints = clientHints;
        }
    }
    if (context.viewport !== undefined) {
        jsonContext.viewport = {
            width: context.viewport.width,
            height: context.viewport.height,
        };
    }
    if (context.deviceScaleFactor !== undefined) {
        jsonContext.deviceScaleFactor = context.deviceScaleFactor;
    }
    if (context.hardwareConcurrency !== undefined) {
        jsonContext.hardwareConcurrency = context.hardwareConcurrency;
    }
    if (context.deviceMemory !== undefined) {
        jsonContext.deviceMemory = context.deviceMemory;
    }
    if (context.geolocation !== undefined) {
        const geolocation: JsonRecord = {
            latitude: context.geolocation.latitude,
            longitude: context.geolocation.longitude,
        };

        if (context.geolocation.accuracy !== undefined) {
            geolocation.accuracy = context.geolocation.accuracy;
        }

        jsonContext.geolocation = geolocation;
    }
    if (context.doNotTrack !== undefined) {
        jsonContext.doNotTrack = context.doNotTrack;
    }
    if (context.globalPrivacyControl !== undefined) {
        jsonContext.globalPrivacyControl = context.globalPrivacyControl;
    }
    if (context.maxTouchPoints !== undefined) {
        jsonContext.maxTouchPoints = context.maxTouchPoints;
    }
    if (context.isMobile !== undefined) {
        jsonContext.isMobile = context.isMobile;
    }
    if (context.hasTouch !== undefined) {
        jsonContext.hasTouch = context.hasTouch;
    }

    if (context.virtualMediaDevices !== undefined) {
        jsonContext.virtualMediaDevices = {
            ...context.virtualMediaDevices,
        };
    }

    return jsonContext;
}

function toJsonClientHints(clientHints: TabContextEnvelope['clientHints']): JsonRecord | undefined {
    if (clientHints === undefined) {
        return undefined;
    }

    const jsonClientHints: JsonRecord = {};

    if (clientHints.brands !== undefined) {
        jsonClientHints.brands = clientHints.brands.map((brand) => ({
            brand: brand.brand,
            version: brand.version,
        }));
    }

    if (clientHints.fullVersionList !== undefined) {
        jsonClientHints.fullVersionList = clientHints.fullVersionList.map((brand) => ({
            brand: brand.brand,
            version: brand.version,
        }));
    }

    if (clientHints.platform !== undefined) {
        jsonClientHints.platform = clientHints.platform;
    }

    if (clientHints.platformVersion !== undefined) {
        jsonClientHints.platformVersion = clientHints.platformVersion;
    }

    if (clientHints.mobile !== undefined) {
        jsonClientHints.mobile = clientHints.mobile;
    }

    if (clientHints.architecture !== undefined) {
        jsonClientHints.architecture = clientHints.architecture;
    }

    if (clientHints.model !== undefined) {
        jsonClientHints.model = clientHints.model;
    }

    if (clientHints.bitness !== undefined) {
        jsonClientHints.bitness = clientHints.bitness;
    }

    return Object.keys(jsonClientHints).length > 0
        ? jsonClientHints
        : undefined;
}

function resolveProxyRoutingResult(proxy: string): ProxyRoutingResult {
    try {
        const proxyUrl = new URL(proxy);
        const scheme = proxyUrl.protocol.replace(':', '').toLowerCase();
        const type = scheme === 'socks5' || scheme === 'socks'
            ? 'socks'
            : scheme === 'socks4'
                ? 'socks4'
                : 'http';
        const port = readProxyPort(proxyUrl.port, type === 'socks' ? 1080 : 8080);

        if (type === 'socks') {
            return {
                type,
                host: proxyUrl.hostname,
                port,
                proxyDNS: true,
            };
        }

        return {
            type,
            host: proxyUrl.hostname,
            port,
        };
    } catch {
        return { type: 'direct' };
    }
}

function resolveProxyAuthCredentials(proxy: string): { username: string; password: string } | null {
    try {
        const proxyUrl = new URL(proxy);
        if (proxyUrl.username.length === 0 && proxyUrl.password.length === 0) {
            return null;
        }

        return {
            username: decodeURIComponent(proxyUrl.username),
            password: decodeURIComponent(proxyUrl.password),
        };
    } catch {
        return null;
    }
}

function readProxyPort(value: string, fallback: number): number {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}
