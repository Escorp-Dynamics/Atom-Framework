import type { BridgeEventName, BridgeMessage, JsonValue } from '../../Shared/Protocol';

export function createDirectResponse(message: BridgeMessage, payload?: unknown): BridgeMessage {
    return {
        id: message.id,
        type: 'Response',
        tabId: message.tabId,
        windowId: message.windowId,
        status: 'Ok',
        payload: payload as JsonValue | undefined,
        timestamp: Date.now(),
    };
}

export function createLifecycleEventMessage(
    id: string,
    event: BridgeEventName,
    tabId?: string,
    windowId?: string,
    payload?: unknown,
): BridgeMessage {
    return {
        id,
        type: 'Event',
        event,
        tabId,
        windowId,
        payload: payload as JsonValue | undefined,
        timestamp: Date.now(),
    };
}