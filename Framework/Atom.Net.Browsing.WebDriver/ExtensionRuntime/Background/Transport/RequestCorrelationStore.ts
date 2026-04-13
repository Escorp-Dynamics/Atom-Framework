import type { BridgeCommand } from '../../Shared/Protocol/BridgeCommand';
import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { BridgeStatus } from '../../Shared/Protocol/BridgeStatus';

export interface PendingBridgeRequest {
    messageId: string;
    tabId?: string;
    command?: BridgeCommand;
    createdAt: number;
    timeoutAt: number;
}

export interface IRequestCorrelationStore {
    register(message: BridgeMessage, timeoutMs: number): PendingBridgeRequest;

    complete(response: BridgeMessage): PendingBridgeRequest | null;

    fail(messageId: string, status: BridgeStatus, error?: string): PendingBridgeRequest | null;

    failAllForTab(tabId: string, status: BridgeStatus, error?: string): readonly PendingBridgeRequest[];

    get(messageId: string): PendingBridgeRequest | null;

    count(): number;
}