import {
    type BackgroundPortEnvelope,
    type ContentReadyEnvelope,
    type ExecuteInMainRequestEnvelope,
    type MainWorldResultEnvelope,
    validateTabContextEnvelope,
    type BackgroundToContentCommandEnvelope,
    type BridgeEventName,
    type BridgeMessage,
    type JsonValue,
    type TabContextEnvelope,
} from '../../Shared/Protocol';
import type {
    ContentRuntimeSubscription,
    ITabRuntimeChannel,
    TabRuntimeCommandHandler,
    TabRuntimeContextHandler,
} from './ContentRuntimeChannel';

interface RuntimePortLike {
    postMessage(message: unknown): void;
    onMessage: {
        addListener(listener: (message: unknown) => void): void;
        removeListener(listener: (message: unknown) => void): void;
    };
    onDisconnect?: {
        addListener(listener: () => void): void;
        removeListener?(listener: () => void): void;
    };
    disconnect?(): void;
}

interface RuntimeApiLike {
    connect(): RuntimePortLike;
}

interface BrowserRuntimeLike {
    runtime?: RuntimeApiLike;
}

function getRuntimeApi(): RuntimeApiLike {
    const runtimeHost = (globalThis as typeof globalThis & {
        browser?: BrowserRuntimeLike;
        chrome?: BrowserRuntimeLike;
    }).browser ?? (globalThis as typeof globalThis & { chrome?: BrowserRuntimeLike }).chrome;

    const runtime = runtimeHost?.runtime;
    if (runtime === undefined) {
        throw new Error('Средства выполнения браузера недоступны');
    }

    return runtime;
}

export class BrowserRuntimePortChannel implements ITabRuntimeChannel {
    private port: RuntimePortLike | null = null;
    private readonly commandHandlers = new Set<TabRuntimeCommandHandler>();
    private readonly contextHandlers = new Set<TabRuntimeContextHandler>();
    private readonly pendingMainWorldRequests = new Map<string, {
        resolve: (value: string) => void;
        reject: (error: unknown) => void;
    }>();
    private readonly onMessage = (message: unknown) => {
        this.handleInboundMessage(message);
    };
    private readonly onDisconnect = () => {
        this.rejectPendingMainWorldRequests(new Error('Порт канала вкладки был отключён во время ожидания ответа основного мира'));
        this.port = null;
    };

    public get connected(): boolean {
        return this.port !== null;
    }

    public async connect(): Promise<void> {
        if (this.port !== null) {
            return;
        }

        const port = getRuntimeApi().connect();
        port.onMessage.addListener(this.onMessage);
        port.onDisconnect?.addListener(this.onDisconnect);
        this.port = port;
    }

    public async disconnect(_reason?: string): Promise<void> {
        if (this.port === null) {
            return;
        }

        const port = this.port;
        this.port = null;
        this.rejectPendingMainWorldRequests(new Error('Порт канала вкладки был отключён'));
        port.onMessage.removeListener(this.onMessage);
        port.onDisconnect?.removeListener?.(this.onDisconnect);
        port.disconnect?.();
    }

    public async emitReady(context: TabContextEnvelope): Promise<void> {
        const envelope: ContentReadyEnvelope = {
            action: 'ready',
            context,
        };

        this.requirePort().postMessage(envelope);
    }

    public executeInMain(script: string, preferPageContextOnNull = false, forcePageContextExecution = false): Promise<string> {
        const requestId = createRequestId();
        const envelope: ExecuteInMainRequestEnvelope = {
            action: 'executeInMain',
            requestId,
            script,
        };

        if (preferPageContextOnNull) {
            envelope.preferPageContextOnNull = true;
        }

        if (forcePageContextExecution) {
            envelope.forcePageContextExecution = true;
        }

        this.requirePort().postMessage(envelope);

        return new Promise<string>((resolve, reject) => {
            this.pendingMainWorldRequests.set(requestId, { resolve, reject });
        });
    }

    public async sendResponse(message: BridgeMessage): Promise<void> {
        if (message.status === undefined) {
            throw new Error('Ответ канала вкладки не содержит состояние');
        }

        this.requirePort().postMessage({
            action: 'response',
            id: message.id,
            status: message.status,
            payload: message.payload,
            error: message.error,
        });
    }

    public async sendEvent(eventName: BridgeEventName, payload?: JsonValue): Promise<void> {
        this.requirePort().postMessage({
            action: 'event',
            event: eventName,
            data: payload,
        });
    }

    public subscribeCommands(handler: TabRuntimeCommandHandler): ContentRuntimeSubscription {
        this.commandHandlers.add(handler);
        return this.createSubscription(this.commandHandlers, handler);
    }

    public subscribeContext(handler: TabRuntimeContextHandler): ContentRuntimeSubscription {
        this.contextHandlers.add(handler);
        return this.createSubscription(this.contextHandlers, handler);
    }

    private requirePort(): RuntimePortLike {
        if (this.port === null) {
            throw new Error('Порт канала вкладки ещё не подключён');
        }

        return this.port;
    }

    private createSubscription<T>(handlers: Set<T>, handler: T): ContentRuntimeSubscription {
        return {
            dispose: () => {
                handlers.delete(handler);
            },
        };
    }

    private handleInboundMessage(message: unknown): void {
        if (isMainWorldResultEnvelope(message)) {
            this.resolveMainWorldRequest(message);
            return;
        }

        if (!isBackgroundCommandEnvelope(message)) {
            return;
        }

        if (message.command === 'ApplyContext') {
            this.dispatchContext(message.payload);
            return;
        }

        const bridgeMessage: BridgeMessage = {
            id: message.id,
            type: 'Request',
            command: message.command,
            payload: message.payload,
        };

        for (const handler of this.commandHandlers) {
            void Promise.resolve(handler(bridgeMessage)).catch((error) => {
                console.error('[канал вкладки] Обработчик команды завершился с ошибкой', error);
            });
        }
    }

    private dispatchContext(payload: JsonValue | undefined): void {
        let context: TabContextEnvelope;
        try {
            context = validateTabContextEnvelope(payload);
        } catch (error) {
            console.error('[канал вкладки] Получены неверные данные применения контекста', error);
            return;
        }

        for (const handler of this.contextHandlers) {
            void Promise.resolve(handler(context)).catch((error) => {
                console.error('[канал вкладки] Обработчик контекста завершился с ошибкой', error);
            });
        }
    }

    private resolveMainWorldRequest(message: MainWorldResultEnvelope): void {
        const pending = this.pendingMainWorldRequests.get(message.requestId);
        if (pending === undefined) {
            return;
        }

        this.pendingMainWorldRequests.delete(message.requestId);

        if (message.status === 'ok') {
            pending.resolve(typeof message.value === 'string' ? message.value : '');
            return;
        }

        pending.reject(new Error(typeof message.error === 'string' && message.error.trim().length > 0
            ? message.error
            : 'Основной мир вернул ошибку выполнения'));
    }

    private rejectPendingMainWorldRequests(error: Error): void {
        for (const pending of this.pendingMainWorldRequests.values()) {
            pending.reject(error);
        }

        this.pendingMainWorldRequests.clear();
    }
}

function isBackgroundCommandEnvelope(value: unknown): value is BackgroundToContentCommandEnvelope {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return false;
    }

    const envelope = value as BackgroundPortEnvelope & Record<string, unknown>;
    return typeof envelope.id === 'string' && typeof envelope.command === 'string';
}

function isMainWorldResultEnvelope(value: unknown): value is MainWorldResultEnvelope {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return false;
    }

    const envelope = value as BackgroundPortEnvelope & Record<string, unknown>;
    return envelope.action === 'mainWorldResult'
        && typeof envelope.requestId === 'string'
        && (envelope.status === 'ok' || envelope.status === 'err');
}

function createRequestId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `main_world_${crypto.randomUUID()}`;
    }

    return `main_world_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}