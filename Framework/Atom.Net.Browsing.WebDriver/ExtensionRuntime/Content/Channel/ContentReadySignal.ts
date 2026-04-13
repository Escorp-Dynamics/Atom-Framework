import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';

export interface IContentReadySignal {
    emitReady(context: TabContextEnvelope): Promise<void>;

    waitUntilReady(timeoutMs?: number): Promise<TabContextEnvelope>;
}