import type { BridgeMessage } from '../../Shared/Protocol';
import {
    createWindow,
    findFirstWindowTab,
    resolveWindowTab,
    updateWindow,
    type BrowserHost,
} from '../index';

export interface DirectWindowWriteCommandContext {
    readonly runtime: any;
    readonly browserHost: BrowserHost;
    readonly sendDirectResponse: (message: BridgeMessage, payload?: unknown) => Promise<void>;
}

export async function handleOpenWindowCommand(
    context: DirectWindowWriteCommandContext,
    message: BridgeMessage,
    url: string,
    windowPosition?: { left?: number; top?: number },
): Promise<void> {
    const windowInfo = await createWindow(context.runtime, context.browserHost.windows, {
        url,
        focused: true,
        ...(windowPosition ?? {}),
    });
    const openedTab = resolveWindowTab(windowInfo)
        ?? await findFirstWindowTab(context.runtime, context.browserHost.tabs, windowInfo.id);

    const payload: Record<string, unknown> = {};
    if (typeof windowInfo.id === 'number') {
        payload.windowId = windowInfo.id.toString();
    }
    if (typeof openedTab?.id === 'number') {
        payload.tabId = openedTab.id.toString();
    }

    await context.sendDirectResponse(message, payload);
}

export async function handleActivateWindowCommand(
    context: DirectWindowWriteCommandContext,
    message: BridgeMessage,
    windowId: number,
): Promise<void> {
    await updateWindow(context.runtime, context.browserHost.windows, windowId, { focused: true });
    await context.sendDirectResponse(message);
}