import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { ITabRegistry } from '../Tabs/TabRegistry';
import type { IBridgeTransportClient } from '../Transport/BridgeTransportClient';
import type { ICommandRouter } from './CommandRouter';
import type { IRouteFailurePolicy } from './RouteFailurePolicy';

export interface TabPortCommandRouterDependencies {
    tabs: ITabRegistry;
    transport: IBridgeTransportClient;
    failures: IRouteFailurePolicy;
    routeDirect?: (message: BridgeMessage) => Promise<boolean>;
}

export class TabPortCommandRouter implements ICommandRouter {
    public constructor(private readonly dependencies: TabPortCommandRouterDependencies) {
    }

    public async route(message: BridgeMessage): Promise<void> {
        try {
            if (message.command === undefined) {
                throw new Error('Команда моста не указана');
            }

            if (this.dependencies.routeDirect !== undefined && await this.dependencies.routeDirect(message)) {
                return;
            }

            if (message.tabId === undefined) {
                throw new Error('Команда моста не содержит идентификатор вкладки');
            }

            const runtime = this.dependencies.tabs.get(message.tabId);
            if (runtime === null || !runtime.endpoint.connected) {
                throw new Error('Канал вкладки ещё не подключён');
            }

            await runtime.endpoint.send(message);
        } catch (error) {
            await this.reportFailure(message, error);
        }
    }

    private async reportFailure(message: BridgeMessage, error: unknown): Promise<void> {
        // Без идентификатора сообщения ответ невозможно скоррелировать: мостовой слой
        // отбросит его как «ответ на неизвестный запрос».
        if (typeof message.id !== 'string' || message.id.length === 0) {
            console.error('[маршрутизатор вкладок] Команда без идентификатора не может быть завершена ошибкой', error);
            return;
        }

        try {
            await this.dependencies.transport.send(this.dependencies.failures.toResponse({
                request: message,
                error,
            }));
        } catch (sendError) {
            // Канал уже недоступен: запрос завершится по таймауту на стороне моста.
            console.error('[маршрутизатор вкладок] Не удалось отправить ответ об ошибке маршрутизации', sendError);
        }
    }
}