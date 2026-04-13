import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';
import type { ITabRuntimeEndpoint } from './TabRuntimeEndpoint';

export interface RegisteredTabRuntime {
    endpoint: ITabRuntimeEndpoint;
    context?: TabContextEnvelope;
}

export interface ITabRegistry {
    register(endpoint: ITabRuntimeEndpoint, context?: TabContextEnvelope): void;

    unregister(tabId: string): RegisteredTabRuntime | null;

    get(tabId: string): RegisteredTabRuntime | null;

    list(): readonly RegisteredTabRuntime[];

    markReady(tabId: string, context: TabContextEnvelope): void;

    count(): number;
}