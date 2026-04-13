import type { TabContextEnvelope } from '../../Shared/Protocol';
import type { BrowserTab } from '../Browser/BrowserApi';
import type { VirtualCookie } from '../Cookies/VirtualCookies';
import { releaseTabContext } from '../Context/TabContextLifecycle';
import type { ITabRegistry, RegisteredTabRuntime } from './TabRegistry';

export function closeTrackedTab(
    tabId: string,
    tabs: ITabRegistry,
    tabContexts: Map<string, TabContextEnvelope>,
    virtualCookies: Map<string, VirtualCookie[]>,
    reportTabCount: (count: number) => void,
): RegisteredTabRuntime | null {
    const runtimeTab = tabs.unregister(tabId);
    releaseTabContext(tabId, tabContexts, virtualCookies, runtimeTab?.context);
    reportTabCount(tabs.count());
    return runtimeTab;
}

export async function closeTrackedWindowTabs(
    windowTabs: readonly BrowserTab[],
    tabs: ITabRegistry,
    tabContexts: Map<string, TabContextEnvelope>,
    virtualCookies: Map<string, VirtualCookie[]>,
): Promise<void> {
    for (const tab of windowTabs) {
        if (typeof tab.id !== 'number') {
            continue;
        }

        const runtimeTab = tabs.unregister(tab.id.toString());
        releaseTabContext(tab.id.toString(), tabContexts, virtualCookies, runtimeTab?.context);
        if (runtimeTab !== null) {
            await runtimeTab.endpoint.disconnect('Окно закрыто фоновой командой');
        }
    }
}