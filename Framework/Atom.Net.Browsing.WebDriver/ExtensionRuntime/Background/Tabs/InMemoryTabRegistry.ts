import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';
import type { ITabRegistry, RegisteredTabRuntime } from './TabRegistry';
import type { ITabRuntimeEndpoint } from './TabRuntimeEndpoint';

export class InMemoryTabRegistry implements ITabRegistry {
    private readonly runtimes = new Map<string, RegisteredTabRuntime>();

    public register(endpoint: ITabRuntimeEndpoint, context?: TabContextEnvelope): void {
        this.runtimes.set(endpoint.tabId, {
            endpoint,
            context,
        });
    }

    public unregister(tabId: string): RegisteredTabRuntime | null {
        const runtime = this.runtimes.get(tabId) ?? null;
        if (runtime !== null) {
            this.runtimes.delete(tabId);
        }

        return runtime;
    }

    public get(tabId: string): RegisteredTabRuntime | null {
        return this.runtimes.get(tabId) ?? null;
    }

    public list(): readonly RegisteredTabRuntime[] {
        return [...this.runtimes.values()];
    }

    public markReady(tabId: string, context: TabContextEnvelope): void {
        const runtime = this.runtimes.get(tabId);
        if (runtime === undefined) {
            return;
        }

        runtime.context = context;
    }

    public count(): number {
        return this.runtimes.size;
    }
}