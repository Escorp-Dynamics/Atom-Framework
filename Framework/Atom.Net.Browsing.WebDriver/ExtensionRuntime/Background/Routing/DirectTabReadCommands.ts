import type { BridgeMessage } from '../../Shared/Protocol';
import { getTab, type BrowserHost } from '../index';

export interface DirectTabReadCommandContext {
    readonly runtime: any;
    readonly browserHost: BrowserHost;
    readonly sendDirectResponse: (message: BridgeMessage, payload?: unknown) => Promise<void>;
}

export async function handleGetUrlCommand(
    context: DirectTabReadCommandContext,
    message: BridgeMessage,
    tabId: number,
): Promise<void> {
    const tab = await getTab(context.runtime, context.browserHost.tabs, tabId);
    await context.sendDirectResponse(message, {
        url: tab.url ?? '',
    });
}

export async function handleGetTitleCommand(
    context: DirectTabReadCommandContext,
    message: BridgeMessage,
    tabId: number,
): Promise<void> {
    const tab = await getTab(context.runtime, context.browserHost.tabs, tabId);
    await context.sendDirectResponse(message, {
        title: tab.title ?? '',
    });
}