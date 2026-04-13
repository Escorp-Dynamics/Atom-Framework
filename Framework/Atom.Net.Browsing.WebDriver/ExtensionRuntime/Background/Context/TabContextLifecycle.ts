import type { TabContextEnvelope } from '../../Shared/Protocol';
import type { ITabRegistry } from '../Tabs/TabRegistry';
import { ensureVirtualCookieStore, moveVirtualCookieStore, type VirtualCookie } from '../index';

export function createDefaultTabContext(
    sessionId: string,
    tabId: string,
    createInternalMessageId: (prefix: string) => string,
    windowId?: string,
    url?: string,
): TabContextEnvelope {
    const now = Date.now();
    return {
        sessionId,
        contextId: createInternalMessageId(`tabctx_${tabId}`),
        tabId,
        windowId,
        url,
        connectedAt: now,
        readyAt: now,
        isReady: true,
    };
}

export function createCookieCommandContext(
    tabId: string,
    requestedContextId: string,
    createDefaultContext: () => TabContextEnvelope,
    existingContext?: TabContextEnvelope,
    windowId?: string,
    url?: string,
): TabContextEnvelope {
    if (existingContext !== undefined) {
        return {
            ...existingContext,
            contextId: requestedContextId,
            windowId: existingContext.windowId ?? windowId,
            url: url ?? existingContext.url,
            readyAt: Date.now(),
            isReady: true,
        };
    }

    return {
        ...createDefaultContext(),
        tabId,
        contextId: requestedContextId,
        windowId,
        url,
    };
}

export function releaseTabContext<TStore>(
    tabId: string,
    tabContexts: Map<string, TabContextEnvelope>,
    virtualCookies: Map<string, TStore>,
    runtimeContext?: TabContextEnvelope,
): void {
    const context = tabContexts.get(tabId) ?? runtimeContext;
    tabContexts.delete(tabId);

    if (context !== undefined) {
        virtualCookies.delete(context.contextId);
    }
}

export function registerSetTabContext(
    tabId: string,
    nextContext: TabContextEnvelope,
    existingContext: TabContextEnvelope | undefined,
    tabContexts: Map<string, TabContextEnvelope>,
    virtualCookies: Map<string, VirtualCookie[]>,
    tabs: ITabRegistry,
): void {
    if (existingContext !== undefined && existingContext.contextId !== nextContext.contextId) {
        moveVirtualCookieStore(virtualCookies, existingContext.contextId, nextContext.contextId);
    }

    tabContexts.set(tabId, nextContext);
    ensureVirtualCookieStore(virtualCookies, nextContext.contextId);
    tabs.markReady(tabId, nextContext);
}