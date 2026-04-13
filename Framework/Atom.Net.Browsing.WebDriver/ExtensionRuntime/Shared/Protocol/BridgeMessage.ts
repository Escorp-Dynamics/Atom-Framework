import type { BridgeCommand } from './BridgeCommand';
import type { BridgeEventName } from './BridgeEventName';
import type { BridgeMessageType } from './BridgeMessageType';
import type { BridgeStatus } from './BridgeStatus';
import type { JsonValue } from './JsonValue';

export interface BridgeMessage {
    id: string;
    type: BridgeMessageType;
    windowId?: string;
    tabId?: string;
    command?: BridgeCommand;
    event?: BridgeEventName;
    status?: BridgeStatus;
    payload?: JsonValue;
    error?: string;
    timestamp?: number;
}