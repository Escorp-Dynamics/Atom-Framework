import type { RuntimeConfig } from '../../Shared/Config/RuntimeConfig';
import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { BridgeTransportSubscription } from '../Transport/BridgeTransportClient';
import type { IKeepAliveController } from '../Transport/KeepAliveController';
import type { IRequestCorrelationStore } from '../Transport/RequestCorrelationStore';
import {
    advanceSessionRuntimeState,
    applyHandshakeAccept,
    createInitialSessionRuntimeState,
    createSessionStartResult,
    createTransportConnectionInfo,
    type ISessionCoordinator,
    type SessionCoordinatorDependencies,
    type SessionRuntimeState,
    type SessionStartResult,
} from './SessionCoordinator';
import type { HandshakeResult } from './HandshakeClient';

const defaultHandshakeTimeoutMs = 10_000;
type TimeoutHandle = ReturnType<typeof globalThis.setTimeout>;

export interface BridgeSessionCoordinatorDependencies extends SessionCoordinatorDependencies {
    correlation: IRequestCorrelationStore;
    keepAlive: IKeepAliveController;
}

export class BridgeSessionCoordinator implements ISessionCoordinator {
    private runtimeState: SessionRuntimeState;
    private transportSubscription: BridgeTransportSubscription | null = null;
    private pendingHandshake:
        | {
            resolve: (result: HandshakeResult) => void;
            reject: (error: unknown) => void;
            timerId: TimeoutHandle;
        }
        | null = null;

    public constructor(
        private readonly dependencies: BridgeSessionCoordinatorDependencies,
        sessionId: string,
    ) {
        this.runtimeState = createInitialSessionRuntimeState(sessionId);
    }

    public get state() {
        return this.runtimeState.state;
    }

    public async start(config: RuntimeConfig): Promise<SessionStartResult> {
        this.ensureTransportSubscription();
        this.transitionTo('ConfigLoaded');
        this.transitionTo('TransportConnecting');

        await this.dependencies.transport.connect(createTransportConnectionInfo(config));
        this.dependencies.health.reportTransportConnected(true);

        this.transitionTo('Handshaking');
        const request = this.dependencies.handshake.createRequest(config);
        const handshakeResultPromise = this.createHandshakePromise();

        await this.dependencies.transport.send(request);
        const handshakeResult = await handshakeResultPromise;

        if (!handshakeResult.accepted || handshakeResult.acceptPayload === undefined) {
            const reason = handshakeResult.message.error ?? 'Согласование отклонено мостовым слоем';
            this.transitionTo('Degraded', reason);
            throw new Error(reason);
        }

        this.runtimeState = applyHandshakeAccept(this.runtimeState, handshakeResult.acceptPayload);
        this.dependencies.health.reportState(this.runtimeState.state);

        if (config.featureFlags.enableKeepAlive) {
            this.dependencies.keepAlive.start(async () => {
                await this.dependencies.transport.send({
                    id: createRuntimeMessageId('ping'),
                    type: 'Ping',
                    timestamp: Date.now(),
                });
            }, handshakeResult.acceptPayload.pingIntervalMs);
        }

        return createSessionStartResult(config, handshakeResult.acceptPayload, this.runtimeState.startedAt);
    }

    public async stop(reason?: string): Promise<void> {
        this.dependencies.keepAlive.stop();
        this.rejectPendingHandshake(new Error(reason ?? 'Сеанс остановлен до завершения согласования'));

        if (this.transportSubscription !== null) {
            this.transportSubscription.dispose();
            this.transportSubscription = null;
        }

        await this.dependencies.transport.disconnect(reason);
        this.dependencies.health.reportTransportConnected(false);
        this.transitionTo('Closed', reason);
    }

    public async handleInbound(message: BridgeMessage): Promise<void> {
        this.dependencies.health.reportInboundMessage(message);

        if (message.type === 'Handshake') {
            this.resolvePendingHandshake(message);
            return;
        }

        if (message.type === 'Pong') {
            this.dependencies.keepAlive.notePong(message.timestamp);
            return;
        }

        if (message.type === 'Response') {
            this.dependencies.correlation.complete(message);
            this.dependencies.health.reportPendingRequestCount(this.dependencies.correlation.count());
            return;
        }

        if (message.type === 'Request') {
            await this.dependencies.commandRouter.route(message);
            return;
        }

        if (message.type === 'Event') {
            await this.dependencies.eventRouter.route(message);
            this.dependencies.health.reportTabCount(this.dependencies.tabs.count());
        }
    }

    public async handleTransportClosed(reason?: string): Promise<void> {
        this.dependencies.keepAlive.stop();
        this.dependencies.health.reportTransportConnected(false);
        this.rejectPendingHandshake(new Error(reason ?? 'Мостовое соединение закрыто'));
        this.transitionTo('Degraded', reason ?? 'Мостовое соединение закрыто');
    }

    private ensureTransportSubscription(): void {
        if (this.transportSubscription !== null) {
            return;
        }

        this.transportSubscription = this.dependencies.transport.subscribe((message) => this.handleInbound(message));
    }

    private transitionTo(nextState: SessionRuntimeState['state'], error?: string): void {
        this.runtimeState = advanceSessionRuntimeState(this.runtimeState, nextState, error);
        this.dependencies.health.reportState(this.runtimeState.state, error);
    }

    private createHandshakePromise(): Promise<HandshakeResult> {
        this.rejectPendingHandshake(new Error('Предыдущее согласование было вытеснено новым запуском'));

        return new Promise<HandshakeResult>((resolve, reject) => {
            const timerId = globalThis.setTimeout(() => {
                if (this.pendingHandshake === null || this.pendingHandshake.timerId !== timerId) {
                    return;
                }

                this.pendingHandshake = null;
                reject(new Error('Согласование не завершилось в течение таймаута'));
            }, defaultHandshakeTimeoutMs);

            this.pendingHandshake = {
                resolve: (result) => {
                    globalThis.clearTimeout(timerId);
                    this.pendingHandshake = null;
                    resolve(result);
                },
                reject: (error) => {
                    globalThis.clearTimeout(timerId);
                    this.pendingHandshake = null;
                    reject(error);
                },
                timerId,
            };
        });
    }

    private resolvePendingHandshake(message: BridgeMessage): void {
        if (this.pendingHandshake === null) {
            return;
        }

        try {
            const result = this.dependencies.handshake.parseResponse(message);
            this.pendingHandshake.resolve(result);
        } catch (error) {
            this.pendingHandshake.reject(error);
        }
    }

    private rejectPendingHandshake(error: unknown): void {
        if (this.pendingHandshake === null) {
            return;
        }

        this.pendingHandshake.reject(error);
    }
}

function createRuntimeMessageId(prefix: string): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `${prefix}_${crypto.randomUUID()}`;
    }

    return `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}