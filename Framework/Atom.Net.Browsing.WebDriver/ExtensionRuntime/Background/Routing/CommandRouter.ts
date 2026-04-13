import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';

export interface ICommandRouter {
    route(message: BridgeMessage): Promise<void>;
}