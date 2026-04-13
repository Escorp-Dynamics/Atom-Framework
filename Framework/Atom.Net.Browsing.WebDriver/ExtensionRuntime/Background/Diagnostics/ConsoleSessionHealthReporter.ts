import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { ISessionHealthReporter, SessionHealthSnapshot } from '../Session/SessionHealthReporter';
import type { SessionLifecycleState } from '../Session/SessionLifecycleState';

export class ConsoleSessionHealthReporter implements ISessionHealthReporter {
    private snapshot: SessionHealthSnapshot;

    public constructor(sessionId: string) {
        this.snapshot = {
            sessionId,
            state: 'Idle',
            transportConnected: false,
            connectedTabCount: 0,
            pendingRequestCount: 0,
        };
    }

    public reportState(state: SessionLifecycleState, degradedReason?: string): void {
        this.snapshot = {
            ...this.snapshot,
            state,
            degradedReason,
        };

        console.info('[фоновый вход] Состояние сеанса изменено', {
            state,
            degradedReason,
        });
    }

    public reportTransportConnected(connected: boolean): void {
        this.snapshot = {
            ...this.snapshot,
            transportConnected: connected,
        };

        console.info('[фоновый вход] Состояние мостового соединения изменено', {
            connected,
        });
    }

    public reportTabCount(tabCount: number): void {
        this.snapshot = {
            ...this.snapshot,
            connectedTabCount: tabCount,
        };
    }

    public reportPendingRequestCount(count: number): void {
        this.snapshot = {
            ...this.snapshot,
            pendingRequestCount: count,
        };
    }

    public reportInboundMessage(_message: BridgeMessage): void {
        this.snapshot = {
            ...this.snapshot,
            lastMessageAt: Date.now(),
        };
    }

    public createSnapshot(sessionId: string): SessionHealthSnapshot {
        if (this.snapshot.sessionId !== sessionId) {
            this.snapshot = {
                ...this.snapshot,
                sessionId,
            };
        }

        return { ...this.snapshot };
    }
}