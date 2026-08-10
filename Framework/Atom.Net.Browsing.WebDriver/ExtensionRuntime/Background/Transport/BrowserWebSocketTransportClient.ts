import {
    deserializeBridgeMessage,
    serializeBridgeMessage,
    type BridgeMessage,
} from '../../Shared/Protocol';
import type {
    BridgeInboundMessageHandler,
    BridgeTransportCloseHandler,
    BridgeTransportCloseInfo,
    BridgeTransportConnectionInfo,
    BridgeTransportSubscription,
    IBridgeTransportClient,
} from './BridgeTransportClient';

export class BrowserWebSocketTransportClient implements IBridgeTransportClient {
    private socket: WebSocket | null = null;
    private connectionPromise: Promise<void> | null = null;
    private readonly handlers = new Set<BridgeInboundMessageHandler>();
    private readonly closeHandlers = new Set<BridgeTransportCloseHandler>();

    public get connected(): boolean {
        return this.socket?.readyState === WebSocket.OPEN;
    }

    public connect(connection: BridgeTransportConnectionInfo): Promise<void> {
        if (this.connected) {
            return Promise.resolve();
        }

        if (this.connectionPromise !== null) {
            return this.connectionPromise;
        }

        // Предыдущий сокет мог остаться в CONNECTING/CLOSING после сброшенного промиса.
        // Без явного закрытия он оставался бы висеть навсегда, продолжая доставлять события.
        this.closeLingeringSocket();

        const socket = new WebSocket(connection.url);
        this.socket = socket;
        console.info('[мостовой канал] Открываем соединение', {
            url: connection.url,
        });

        this.connectionPromise = new Promise<void>((resolve, reject) => {
            let settled = false;

            socket.addEventListener('open', () => {
                settled = true;
                this.connectionPromise = null;
                console.info('[мостовой канал] Соединение открыто', {
                    url: connection.url,
                });
                resolve();
            }, { once: true });

            socket.addEventListener('error', () => {
                if (settled) {
                    return;
                }

                settled = true;
                this.connectionPromise = null;
                reject(new Error(`Не удалось открыть мостовой канал: url=${connection.url}, readyState=${describeReadyState(socket.readyState)}`));
            }, { once: true });

            socket.addEventListener('close', (event) => {
                if (settled) {
                    return;
                }

                settled = true;
                this.connectionPromise = null;
                reject(new Error(`Мостовой канал закрылся до открытия: url=${connection.url}, code=${event.code}, reason=${event.reason || 'без причины'}, clean=${event.wasClean}`));
            }, { once: true });
        });

        socket.addEventListener('message', (event) => {
            this.handleInboundMessage(event);
        });

        socket.addEventListener('error', () => {
            // Ошибка без последующего close оставляла «полумёртвый» сокет текущим:
            // считаем его недействительным сразу (рендер close обработается отдельно).
            if (this.socket === socket) {
                this.socket = null;
            }
        });

        socket.addEventListener('close', (event) => {
            if (this.socket === socket) {
                this.socket = null;
            }

            this.connectionPromise = null;
            console.info('[мостовой канал] Соединение закрыто', {
                url: connection.url,
                code: event.code,
                reason: event.reason,
                wasClean: event.wasClean,
            });

            this.notifyClosed({
                url: connection.url,
                code: event.code,
                reason: event.reason,
                wasClean: event.wasClean,
            });
        });

        return this.connectionPromise;
    }

    public async disconnect(reason?: string): Promise<void> {
        if (this.socket === null) {
            return;
        }

        const socket = this.socket;
        this.socket = null;
        this.connectionPromise = null;

        if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
            socket.close(1000, reason);
        }
    }

    public async send(message: BridgeMessage): Promise<void> {
        if (!this.connected || this.socket === null) {
            throw new Error('Мостовой канал ещё не подключён');
        }

        this.socket.send(serializeBridgeMessage(message));
    }

    public subscribe(handler: BridgeInboundMessageHandler): BridgeTransportSubscription {
        this.handlers.add(handler);

        return {
            dispose: () => {
                this.handlers.delete(handler);
            },
        };
    }

    public subscribeClosed(handler: BridgeTransportCloseHandler): BridgeTransportSubscription {
        this.closeHandlers.add(handler);

        return {
            dispose: () => {
                this.closeHandlers.delete(handler);
            },
        };
    }

    private closeLingeringSocket(): void {
        const socket = this.socket;
        if (socket === null) {
            return;
        }

        this.socket = null;

        if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
            try {
                socket.close(1000, 'Вытеснено новым подключением');
            } catch (error) {
                console.error('[мостовой канал] Не удалось закрыть вытесненный сокет', error);
            }
        }
    }

    private notifyClosed(info: BridgeTransportCloseInfo): void {
        for (const handler of this.closeHandlers) {
            void Promise.resolve(handler(info)).catch((error) => {
                console.error('[мостовой канал] Обработчик закрытия соединения завершился с ошибкой', error);
            });
        }
    }

    private handleInboundMessage(event: MessageEvent): void {
        if (typeof event.data !== 'string') {
            console.error('[мостовой канал] Получен неподдерживаемый бинарный кадр');
            return;
        }

        let message: BridgeMessage;
        try {
            message = deserializeBridgeMessage(event.data);
        } catch (error) {
            console.error('[мостовой канал] Получено неверное мостовое сообщение', error);
            return;
        }

        for (const handler of this.handlers) {
            void Promise.resolve(handler(message)).catch((error) => {
                console.error('[мостовой канал] Обработчик входящего сообщения завершился с ошибкой', error);
            });
        }
    }
}

function describeReadyState(readyState: number): string {
    switch (readyState) {
        case WebSocket.CONNECTING:
            return 'CONNECTING';
        case WebSocket.OPEN:
            return 'OPEN';
        case WebSocket.CLOSING:
            return 'CLOSING';
        case WebSocket.CLOSED:
            return 'CLOSED';
        default:
            return `UNKNOWN(${readyState.toString()})`;
    }
}