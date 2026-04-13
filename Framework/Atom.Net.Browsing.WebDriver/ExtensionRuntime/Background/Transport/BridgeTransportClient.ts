import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';

export type BridgeInboundMessageHandler = (message: BridgeMessage) => Promise<void> | void;

export interface BridgeTransportSubscription {
    dispose(): void;
}

export interface BridgeTransportConnectionInfo {
    url: string;
    requestTimeoutMs?: number;
    pingIntervalMs?: number;
    maxMessageSize?: number;
}

export interface IBridgeTransportClient {
    readonly connected: boolean;

    connect(connection: BridgeTransportConnectionInfo): Promise<void>;

    disconnect(reason?: string): Promise<void>;

    send(message: BridgeMessage): Promise<void>;

    subscribe(handler: BridgeInboundMessageHandler): BridgeTransportSubscription;
}