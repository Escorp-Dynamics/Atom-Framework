import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { IEventRouter } from './EventRouter';

export class PassiveEventRouter implements IEventRouter {
    public async route(message: BridgeMessage): Promise<void> {
        console.info('[фоновый вход] Получено событие мостового слоя', {
            event: message.event,
            tabId: message.tabId,
            windowId: message.windowId,
        });
    }
}