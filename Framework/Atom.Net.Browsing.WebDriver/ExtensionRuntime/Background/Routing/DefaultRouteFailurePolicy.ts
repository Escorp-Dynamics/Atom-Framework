import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { IRouteFailurePolicy, RouteFailureContext } from './RouteFailurePolicy';

export class DefaultRouteFailurePolicy implements IRouteFailurePolicy {
    public toResponse(context: RouteFailureContext): BridgeMessage {
        const response: BridgeMessage = {
            id: context.request.id,
            type: 'Response',
            status: 'Error',
            error: describeRouteError(context.error),
            timestamp: Date.now(),
        };

        // Вкладка и окно указываются только если они известны: сбой мог произойти до
        // их разбора, а пустые строки мостовой слой трактует как рассогласование маршрута.
        if (typeof context.request.tabId === 'string' && context.request.tabId.length > 0) {
            response.tabId = context.request.tabId;
        }

        if (typeof context.request.windowId === 'string' && context.request.windowId.length > 0) {
            response.windowId = context.request.windowId;
        }

        return response;
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