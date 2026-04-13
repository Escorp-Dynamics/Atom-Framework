export interface KeepAliveSnapshot {
    lastPingAt?: number;
    lastPongAt?: number;
    missedPongCount: number;
    healthy: boolean;
}

export interface IKeepAliveController {
    start(sendPing: () => Promise<void>, intervalMs: number): void;

    stop(): void;

    notePong(receivedAt?: number): void;

    getSnapshot(): KeepAliveSnapshot;
}