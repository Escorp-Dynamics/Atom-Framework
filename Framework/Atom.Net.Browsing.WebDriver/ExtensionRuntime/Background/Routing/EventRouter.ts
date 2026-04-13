import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';

export interface IEventRouter {
    route(message: BridgeMessage): Promise<void>;
}