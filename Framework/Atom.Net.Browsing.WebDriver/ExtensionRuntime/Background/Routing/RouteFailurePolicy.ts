import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';

export interface RouteFailureContext {
    request: BridgeMessage;
    error: unknown;
}

export interface IRouteFailurePolicy {
    toResponse(context: RouteFailureContext): BridgeMessage;

    isRetryable(error: unknown): boolean;
}