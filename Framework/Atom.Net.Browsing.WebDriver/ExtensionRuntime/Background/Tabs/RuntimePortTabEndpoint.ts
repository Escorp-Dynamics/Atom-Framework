import type { BridgeCommand } from '../../Shared/Protocol/BridgeCommand';
import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { ContentCommandName } from '../../Shared/Protocol/ContentCommandName';
import type {
    BackgroundToContentCommandEnvelope,
    ContentEventEnvelope,
    ContentReadyEnvelope,
    ContentResponseEnvelope,
    ExecuteInMainRequestEnvelope,
    MainWorldResultEnvelope,
} from '../../Shared/Protocol/ContentPortEnvelope';
import type { JsonValue } from '../../Shared/Protocol/JsonValue';
import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';
import type { ITabRuntimeEndpoint } from './TabRuntimeEndpoint';

type MessageListener = (message: unknown) => void;
type DisconnectListener = () => void;

export interface RuntimePortLike {
    sender?: {
        tab?: {
            id?: number;
            windowId?: number;
        };
    };
    postMessage(message: unknown): void;
    disconnect?(): void;
    onMessage: {
        addListener(listener: MessageListener): void;
        removeListener?(listener: MessageListener): void;
    };
    onDisconnect?: {
        addListener(listener: DisconnectListener): void;
        removeListener?(listener: DisconnectListener): void;
    };
}

export interface RuntimePortTabEndpointCallbacks {
    forwardToBridge(message: BridgeMessage): Promise<void>;
    executeInMainWorld(requestId: string, script: string, preferPageContextOnNull: boolean, forcePageContextExecution: boolean): Promise<MainWorldResultEnvelope>;
    markReady(tabId: string, context: TabContextEnvelope): void;
    onDisconnected(tabId: string): void;
}

export class RuntimePortTabEndpoint implements ITabRuntimeEndpoint {
    private active = true;
    private readonly onMessage = (message: unknown) => {
        this.handlePortMessage(message);
    };
    private readonly onDisconnect = () => {
        this.dispose();
    };

    public readonly tabId: string;
    public readonly windowId?: string;

    public constructor(
        private readonly port: RuntimePortLike,
        private readonly callbacks: RuntimePortTabEndpointCallbacks,
    ) {
        const rawTabId = port.sender?.tab?.id;
        if (!Number.isInteger(rawTabId) || rawTabId === undefined) {
            throw new Error('Порт вкладки не содержит идентификатор вкладки');
        }

        this.tabId = rawTabId.toString();

        const rawWindowId = port.sender?.tab?.windowId;
        if (Number.isInteger(rawWindowId) && rawWindowId !== undefined) {
            this.windowId = rawWindowId.toString();
        }

        this.port.onMessage.addListener(this.onMessage);
        this.port.onDisconnect?.addListener(this.onDisconnect);
    }

    public get connected(): boolean {
        return this.active;
    }

    public async send(message: BridgeMessage): Promise<void> {
        if (!this.active) {
            throw new Error('Порт вкладки уже отключён');
        }

        if (message.command === undefined) {
            throw new Error('Команда моста не указана');
        }

        const envelope: BackgroundToContentCommandEnvelope = {
            id: message.id,
            command: toContentCommandName(message.command),
            payload: message.payload,
        };

        this.port.postMessage(envelope);
    }

    public async applyContext(context: TabContextEnvelope): Promise<void> {
        if (!this.active) {
            throw new Error('Порт вкладки уже отключён');
        }

        this.port.postMessage({
            id: createInternalMessageId('apply_context'),
            command: 'ApplyContext',
            payload: toJsonContext(context),
        } satisfies BackgroundToContentCommandEnvelope);
    }

    public async disconnect(_reason?: string): Promise<void> {
        this.dispose();
    }

    private dispose(): void {
        if (!this.active) {
            return;
        }

        this.active = false;
        this.port.onMessage.removeListener?.(this.onMessage);
        this.port.onDisconnect?.removeListener?.(this.onDisconnect);
        this.port.disconnect?.();
        this.callbacks.onDisconnected(this.tabId);
    }

    private handlePortMessage(message: unknown): void {
        if (isContentResponseEnvelope(message)) {
            this.forwardToBridge({
                id: message.id,
                type: 'Response',
                tabId: this.tabId,
                windowId: this.windowId,
                status: message.status,
                payload: message.payload,
                error: message.error,
                timestamp: Date.now(),
            });
            return;
        }

        if (isContentEventEnvelope(message)) {
            this.forwardToBridge({
                id: createInternalMessageId('event'),
                type: 'Event',
                tabId: this.tabId,
                windowId: this.windowId,
                event: message.event,
                payload: message.data,
                timestamp: Date.now(),
            });
            return;
        }

        if (isContentReadyEnvelope(message)) {
            this.callbacks.markReady(this.tabId, message.context);
            return;
        }

        if (isExecuteInMainRequestEnvelope(message)) {
            this.executeInMainWorld(
                message.requestId,
                message.script,
                message.preferPageContextOnNull === true,
                message.forcePageContextExecution === true,
            );
        }
    }

    private forwardToBridge(message: BridgeMessage): void {
        void this.callbacks.forwardToBridge(message).catch((error) => {
            console.error('[порт вкладки] Не удалось передать сообщение в мостовой канал', error);
        });
    }

    private executeInMainWorld(requestId: string, script: string, preferPageContextOnNull: boolean, forcePageContextExecution: boolean): void {
        void this.callbacks.executeInMainWorld(requestId, script, preferPageContextOnNull, forcePageContextExecution)
            .then((envelope) => {
                if (!this.active) {
                    return;
                }

                this.port.postMessage(envelope);
            })
            .catch((error) => {
                if (!this.active) {
                    return;
                }

                const envelope: MainWorldResultEnvelope = {
                    action: 'mainWorldResult',
                    requestId,
                    status: 'err',
                    error: error instanceof Error && error.message.trim().length > 0
                        ? error.message
                        : 'Не удалось выполнить код в основном мире',
                };

                this.port.postMessage(envelope);
            });
    }
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isContentResponseEnvelope(value: unknown): value is ContentResponseEnvelope {
    if (!isRecord(value)) {
        return false;
    }

    return value.action === 'response'
        && typeof value.id === 'string'
        && typeof value.status === 'string';
}

function isContentEventEnvelope(value: unknown): value is ContentEventEnvelope {
    if (!isRecord(value)) {
        return false;
    }

    return value.action === 'event'
        && typeof value.event === 'string';
}

function isContentReadyEnvelope(value: unknown): value is ContentReadyEnvelope {
    if (!isRecord(value)) {
        return false;
    }

    return value.action === 'ready' && isRecord(value.context);
}

function isExecuteInMainRequestEnvelope(value: unknown): value is ExecuteInMainRequestEnvelope {
    if (!isRecord(value)) {
        return false;
    }

    return value.action === 'executeInMain'
        && typeof value.requestId === 'string'
        && typeof value.script === 'string'
        && (value.preferPageContextOnNull === undefined || typeof value.preferPageContextOnNull === 'boolean')
        && (value.forcePageContextExecution === undefined || typeof value.forcePageContextExecution === 'boolean');
}

function toContentCommandName(command: BridgeCommand): ContentCommandName {
    switch (command) {
        case 'ExecuteScript':
        case 'FindElement':
        case 'FindElements':
        case 'GetElementProperty':
        case 'ResolveElementScreenPoint':
        case 'DescribeElement':
        case 'FocusElement':
        case 'ScrollElementIntoView':
        case 'WaitForElement':
        case 'CheckShadowRoot':
            return command;
        default:
            throw new Error('Команда пока не поддерживается каналом вкладки');
    }
}

function createInternalMessageId(prefix: string): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `${prefix}_${crypto.randomUUID()}`;
    }

    return `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}

function toJsonContext(context: TabContextEnvelope): JsonValue {
    const jsonContext: Record<string, JsonValue> = {
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
        jsonContext.geolocation = {
            latitude: context.geolocation.latitude,
            longitude: context.geolocation.longitude,
        };

        if (context.geolocation.accuracy !== undefined) {
            jsonContext.geolocation.accuracy = context.geolocation.accuracy;
        }
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

function toJsonClientHints(clientHints: TabContextEnvelope['clientHints']): JsonValue | undefined {
    if (clientHints === undefined) {
        return undefined;
    }

    const jsonClientHints: Record<string, JsonValue> = {};

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