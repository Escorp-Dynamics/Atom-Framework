import type { RuntimeConfig } from '../../Shared/Config';
import type { BrowserHost } from '../Browser/BrowserApi';
import { createTab, findBootstrapTab, queryTabs, reloadTab, updateTab } from '../Browser/BrowserApi';
import { emitBackgroundDebugEvent, toErrorMessage } from '../Diagnostics/BackgroundDebugEvents';

export async function ensureDiscoveryTab(
    runtime: any,
    browserHost: BrowserHost,
    config: RuntimeConfig,
    isRegisteredTab: (tabId: string) => boolean,
    discoveryUrl: string,
): Promise<void> {
    if (browserHost.tabs === undefined) {
        return;
    }

    try {
        const httpTabs = await queryTabs(runtime, browserHost.tabs, { url: 'http://*/*' });

        if (httpTabs.length === 0) {
            const bootstrapTab = await findBootstrapTab(runtime, browserHost);
            if (bootstrapTab?.id !== undefined) {
                await updateTab(runtime, browserHost.tabs, bootstrapTab.id, {
                    url: discoveryUrl,
                    active: true,
                });

                emitBackgroundDebugEvent(config, 'discovery-tab-updated', {
                    tabId: bootstrapTab.id,
                    url: discoveryUrl,
                });
                return;
            }

            await createTab(runtime, browserHost.tabs, {
                url: discoveryUrl,
                active: true,
            });

            emitBackgroundDebugEvent(config, 'discovery-tab-created', {
                url: discoveryUrl,
            });
            return;
        }

        for (const tab of httpTabs) {
            const tabId = tab.id;
            if (typeof tabId !== 'number' || !Number.isInteger(tabId)) {
                continue;
            }

            if (isRegisteredTab(tabId.toString())) {
                continue;
            }

            await reloadTab(runtime, browserHost.tabs, tabId);
            emitBackgroundDebugEvent(config, 'discovery-tab-reloaded', {
                tabId,
                url: tab.url ?? '',
            });
        }
    } catch (error) {
        emitBackgroundDebugEvent(config, 'discovery-tab-failed', {
            error: toErrorMessage(error),
        });

        console.error('[фоновый вход] Не удалось подготовить discovery путь', error);
    }
}