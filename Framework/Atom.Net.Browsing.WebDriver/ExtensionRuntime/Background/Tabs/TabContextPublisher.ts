import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';

export interface ITabContextPublisher {
    publish(tabId: string, context: TabContextEnvelope): Promise<void>;

    clear(tabId: string): Promise<void>;
}