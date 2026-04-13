import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';
import type { IContentReadySignal } from './ContentReadySignal';

const defaultReadyTimeoutMs = 10_000;
type TimeoutHandle = ReturnType<typeof globalThis.setTimeout>;

export class DeferredContentReadySignal implements IContentReadySignal {
    private currentContext: TabContextEnvelope | null = null;
    private waiters = new Set<{
        resolve: (context: TabContextEnvelope) => void;
        reject: (error: unknown) => void;
        timerId: TimeoutHandle;
    }>();

    public async emitReady(context: TabContextEnvelope): Promise<void> {
        this.currentContext = context;

        for (const waiter of this.waiters) {
            globalThis.clearTimeout(waiter.timerId);
            waiter.resolve(context);
        }

        this.waiters.clear();
    }

    public waitUntilReady(timeoutMs = defaultReadyTimeoutMs): Promise<TabContextEnvelope> {
        if (this.currentContext !== null) {
            return Promise.resolve(this.currentContext);
        }

        return new Promise<TabContextEnvelope>((resolve, reject) => {
            const waiter = {
                resolve: (context: TabContextEnvelope) => {
                    this.waiters.delete(waiter);
                    resolve(context);
                },
                reject: (error: unknown) => {
                    this.waiters.delete(waiter);
                    reject(error);
                },
                timerId: globalThis.setTimeout(() => {
                    waiter.reject(new Error('Канал вкладки не перешёл в состояние готовности в течение таймаута'));
                }, timeoutMs),
            };

            this.waiters.add(waiter);
        });
    }
}