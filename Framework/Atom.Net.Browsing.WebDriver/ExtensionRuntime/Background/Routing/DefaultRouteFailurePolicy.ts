import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { IRouteFailurePolicy, RouteFailureContext } from './RouteFailurePolicy';

export class DefaultRouteFailurePolicy implements IRouteFailurePolicy {
    public toResponse(context: RouteFailureContext): BridgeMessage {
        return {
            id: context.request.id,
            type: 'Response',
            tabId: context.request.tabId,
            windowId: context.request.windowId,
            status: 'Error',
            error: describeRouteError(context.error),
            timestamp: Date.now(),
        };
    }

    public isRetryable(_error: unknown): boolean {
        return false;
    }
}

function describeRouteError(error: unknown): string {
    if (error instanceof Error && error.message.trim().length > 0) {
        return error.message;
    }

    return 'Не удалось доставить команду во вкладку';
}