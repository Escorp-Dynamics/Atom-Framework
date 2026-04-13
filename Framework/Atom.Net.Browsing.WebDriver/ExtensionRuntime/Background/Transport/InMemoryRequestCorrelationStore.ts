import type { BridgeMessage, BridgeStatus } from '../../Shared/Protocol';
import type { IRequestCorrelationStore, PendingBridgeRequest } from './RequestCorrelationStore';

export class InMemoryRequestCorrelationStore implements IRequestCorrelationStore {
    private readonly pendingRequests = new Map<string, PendingBridgeRequest>();

    public register(message: BridgeMessage, timeoutMs: number): PendingBridgeRequest {
        const now = Date.now();
        const request: PendingBridgeRequest = {
            messageId: message.id,
            tabId: message.tabId,
            command: message.command,
            createdAt: now,
            timeoutAt: now + timeoutMs,
        };

        this.pendingRequests.set(request.messageId, request);
        return request;
    }

    public complete(response: BridgeMessage): PendingBridgeRequest | null {
        const request = this.pendingRequests.get(response.id) ?? null;
        if (request === null) {
            return null;
        }

        this.pendingRequests.delete(response.id);
        return request;
    }

    public fail(messageId: string, _status: BridgeStatus, _error?: string): PendingBridgeRequest | null {
        const request = this.pendingRequests.get(messageId) ?? null;
        if (request === null) {
            return null;
        }

        this.pendingRequests.delete(messageId);
        return request;
    }

    public failAllForTab(tabId: string, _status: BridgeStatus, _error?: string): readonly PendingBridgeRequest[] {
        const failedRequests: PendingBridgeRequest[] = [];

        for (const [messageId, request] of this.pendingRequests) {
            if (request.tabId !== tabId) {
                continue;
            }

            this.pendingRequests.delete(messageId);
            failedRequests.push(request);
        }

        return failedRequests;
    }

    public get(messageId: string): PendingBridgeRequest | null {
        return this.pendingRequests.get(messageId) ?? null;
    }

    public count(): number {
        return this.pendingRequests.size;
    }
}