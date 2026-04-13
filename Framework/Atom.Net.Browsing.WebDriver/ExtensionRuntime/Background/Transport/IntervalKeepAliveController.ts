import type { IKeepAliveController, KeepAliveSnapshot } from './KeepAliveController';

export class IntervalKeepAliveController implements IKeepAliveController {
    private timerId: ReturnType<typeof globalThis.setInterval> | null = null;
    private snapshot: KeepAliveSnapshot = {
        missedPongCount: 0,
        healthy: true,
    };

    public start(sendPing: () => Promise<void>, intervalMs: number): void {
        this.stop();
        this.snapshot = {
            ...this.snapshot,
            missedPongCount: 0,
            healthy: true,
        };

        this.timerId = globalThis.setInterval(() => {
            this.snapshot = {
                ...this.snapshot,
                lastPingAt: Date.now(),
                missedPongCount: this.snapshot.missedPongCount + 1,
                healthy: this.snapshot.missedPongCount < 3,
            };

            void sendPing().catch((error) => {
                console.error('[мостовой канал] Не удалось отправить контрольный запрос', error);
                this.snapshot = {
                    ...this.snapshot,
                    healthy: false,
                };
            });
        }, intervalMs);
    }

    public stop(): void {
        if (this.timerId !== null) {
            globalThis.clearInterval(this.timerId);
            this.timerId = null;
        }
    }

    public notePong(receivedAt = Date.now()): void {
        this.snapshot = {
            ...this.snapshot,
            lastPongAt: receivedAt,
            missedPongCount: 0,
            healthy: true,
        };
    }

    public getSnapshot(): KeepAliveSnapshot {
        return { ...this.snapshot };
    }
}