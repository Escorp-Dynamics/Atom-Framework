import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { SessionLifecycleState } from './SessionLifecycleState';

export interface SessionHealthSnapshot {
    sessionId: string;
    state: SessionLifecycleState;
    transportConnected: boolean;
    connectedTabCount: number;
    pendingRequestCount: number;
    lastMessageAt?: number;
    degradedReason?: string;
}

export interface ISessionHealthReporter {
    reportState(state: SessionLifecycleState, degradedReason?: string): void;
    reportTransportConnected(connected: boolean): void;
    reportTabCount(tabCount: number): void;
    reportPendingRequestCount(count: number): void;
    reportInboundMessage(message: BridgeMessage): void;
    createSnapshot(sessionId: string): SessionHealthSnapshot;
}