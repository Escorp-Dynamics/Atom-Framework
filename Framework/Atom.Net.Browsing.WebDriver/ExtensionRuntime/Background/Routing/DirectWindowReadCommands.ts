import type { BridgeMessage } from '../../Shared/Protocol';
import { getWindow, type BrowserHost, type BrowserWindowInfo } from '../index';

export interface DirectWindowReadCommandContext {
    readonly runtime: any;
    readonly browserHost: BrowserHost;
    readonly sendDirectResponse: (message: BridgeMessage, payload?: unknown) => Promise<void>;
}

export async function handleGetWindowBoundsCommand(
    context: DirectWindowReadCommandContext,
    message: BridgeMessage,
    windowId: number,
): Promise<void> {
    const windowInfo = await getWindow(context.runtime, context.browserHost.windows, windowId);

    await context.sendDirectResponse(message, toWindowBoundsPayload(windowId, windowInfo));
}

function toWindowBoundsPayload(windowId: number, windowInfo: BrowserWindowInfo): Record<string, unknown> {
    return {
        windowId: windowId.toString(),
        left: typeof windowInfo.left === 'number' ? windowInfo.left : 0,
        top: typeof windowInfo.top === 'number' ? windowInfo.top : 0,
        width: typeof windowInfo.width === 'number' ? windowInfo.width : 0,
        height: typeof windowInfo.height === 'number' ? windowInfo.height : 0,
        state: typeof windowInfo.state === 'string' ? windowInfo.state : 'normal',
    };
}