import type { RuntimeConfig } from '../../Shared/Config/RuntimeConfig';
import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { ICommandRouter } from '../Routing/CommandRouter';
import type { IEventRouter } from '../Routing/EventRouter';
import type { ITabRegistry } from '../Tabs/TabRegistry';
import type { BridgeTransportConnectionInfo, IBridgeTransportClient } from '../Transport/BridgeTransportClient';
import type { HandshakeAcceptPayload, IHandshakeClient } from './HandshakeClient';
import type { ISessionHealthReporter } from './SessionHealthReporter';
import { assertSessionStateTransition } from './SessionLifecycleState';
import type { SessionLifecycleState } from './SessionLifecycleState';

export interface SessionStartResult {
    sessionId: string;
    startedAt: number;
    negotiatedProtocolVersion?: number;
    requestTimeoutMs?: number;
    pingIntervalMs?: number;
    maxMessageSize?: number;
}

export interface SessionRuntimeState {
    sessionId: string;
    state: SessionLifecycleState;
    startedAt?: number;
    stoppedAt?: number;
    requestTimeoutMs?: number;
    pingIntervalMs?: number;
    maxMessageSize?: number;
    lastError?: string;
}

export interface SessionCoordinatorDependencies {
    transport: IBridgeTransportClient;
    handshake: IHandshakeClient;
    tabs: ITabRegistry;
    commandRouter: ICommandRouter;
    eventRouter: IEventRouter;
    health: ISessionHealthReporter;
}

export interface ISessionCoordinator {
    readonly state: SessionLifecycleState;

    start(config: RuntimeConfig): Promise<SessionStartResult>;

    stop(reason?: string): Promise<void>;

    handleInbound(message: BridgeMessage): Promise<void>;

    handleTransportClosed(reason?: string): Promise<void>;
}

export function createInitialSessionRuntimeState(sessionId: string): SessionRuntimeState {
    return {
        sessionId,
        state: 'Idle',
    };
}

export function advanceSessionRuntimeState(
    state: SessionRuntimeState,
    nextState: SessionLifecycleState,
    error?: string,
): SessionRuntimeState {
    assertSessionStateTransition(state.state, nextState);

    return {
        ...state,
        state: nextState,
        lastError: error ?? state.lastError,
        stoppedAt: nextState === 'Closed' ? Date.now() : state.stoppedAt,
    };
}

export function createTransportConnectionInfo(config: RuntimeConfig): BridgeTransportConnectionInfo {
    return {
        url: config.transportUrl ?? `ws://${config.host}:${config.port}/?secret=${encodeURIComponent(config.secret)}`,
    };
}

export function createSessionStartResult(
    config: RuntimeConfig,
    handshake: HandshakeAcceptPayload,
    startedAt = Date.now(),
): SessionStartResult {
    return {
        sessionId: config.sessionId,
        startedAt,
        negotiatedProtocolVersion: handshake.negotiatedProtocolVersion,
        requestTimeoutMs: handshake.requestTimeoutMs,
        pingIntervalMs: handshake.pingIntervalMs,
        maxMessageSize: handshake.maxMessageSize,
    };
}

export function applyHandshakeAccept(
    state: SessionRuntimeState,
    handshake: HandshakeAcceptPayload,
    startedAt = Date.now(),
): SessionRuntimeState {
    const readyState = advanceSessionRuntimeState(state, 'Ready');

    return {
        ...readyState,
        startedAt,
        requestTimeoutMs: handshake.requestTimeoutMs,
        pingIntervalMs: handshake.pingIntervalMs,
        maxMessageSize: handshake.maxMessageSize,
    };
}