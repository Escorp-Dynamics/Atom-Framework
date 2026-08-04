import type { IKeepAliveController, KeepAliveSnapshot } from './KeepAliveController';

export interface IntervalKeepAliveControllerOptions {
    /**
     * Сколько подряд Pong может быть пропущено, прежде чем канал объявляется нездоровым.
     */
    readonly maxMissedPongCount?: number;

    /**
     * Одноразовое уведомление о переходе канала в нездоровое состояние
     * (после восстановления Pong уведомление сбрасывается и может сработать снова).
     */
    readonly onUnhealthy?: (missedPongCount: number) => void;
}

const defaultMaxMissedPongCount = 3;

export class IntervalKeepAliveController implements IKeepAliveController {
    private timerId: ReturnType<typeof globalThis.setInterval> | null = null;
    private snapshot: KeepAliveSnapshot = {
        missedPongCount: 0,
        healthy: true,
    };
    private readonly maxMissedPongCount: number;
    private readonly onUnhealthy?: (missedPongCount: number) => void;
    private unhealthyNotified = false;

    public constructor(options: IntervalKeepAliveControllerOptions = {}) {
        this.maxMissedPongCount = options.maxMissedPongCount ?? defaultMaxMissedPongCount;
        this.onUnhealthy = options.onUnhealthy;
    }

    public start(sendPing: () => Promise<void>, intervalMs: number): void {
        this.stop();
        this.unhealthyNotified = false;
        this.snapshot = {
            ...this.snapshot,
            missedPongCount: 0,
            healthy: true,
        };

        this.timerId = globalThis.setInterval(() => {
            // Счётчик сначала увеличивается, и порог оценивается уже по новому значению:
            // N подряд пропущенных Pong должны давать ровно N в missedPongCount и unhealthy-снимок,
            // а не «unhealthy после N+1-го» (исходная off-by-one оценивала старое значение).
            const missedPongCount = this.snapshot.missedPongCount + 1;
            const healthy = missedPongCount < this.maxMissedPongCount;
            this.snapshot = {
                ...this.snapshot,
                lastPingAt: Date.now(),
                missedPongCount,
                healthy,
            };

            if (!healthy) {
                this.notifyUnhealthy(missedPongCount);
            }

            void sendPing().catch((error) => {
                console.error('[мостовой канал] Не удалось отправить контрольный запрос', error);
                this.snapshot = {
                    ...this.snapshot,
                    healthy: false,
                };

                this.notifyUnhealthy(this.snapshot.missedPongCount);
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
        this.unhealthyNotified = false;
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

    private notifyUnhealthy(missedPongCount: number): void {
        if (this.unhealthyNotified) {
            return;
        }

        this.unhealthyNotified = true;
        this.onUnhealthy?.(missedPongCount);
    }
}
