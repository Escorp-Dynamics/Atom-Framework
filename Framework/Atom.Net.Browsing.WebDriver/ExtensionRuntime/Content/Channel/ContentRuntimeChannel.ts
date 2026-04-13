import type { BridgeEventName } from '../../Shared/Protocol/BridgeEventName';
import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { JsonValue } from '../../Shared/Protocol/JsonValue';
import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';

export type TabRuntimeCommandHandler = (message: BridgeMessage) => Promise<void> | void;
export type TabRuntimeContextHandler = (context: TabContextEnvelope) => Promise<void> | void;

export interface ContentRuntimeSubscription {
    dispose(): void;
}

export interface ITabRuntimeChannel {
    readonly connected: boolean;

    connect(): Promise<void>;

    disconnect(reason?: string): Promise<void>;

    sendResponse(message: BridgeMessage): Promise<void>;

    sendEvent(eventName: BridgeEventName, payload?: JsonValue): Promise<void>;

    subscribeCommands(handler: TabRuntimeCommandHandler): ContentRuntimeSubscription;

    subscribeContext(handler: TabRuntimeContextHandler): ContentRuntimeSubscription;
}