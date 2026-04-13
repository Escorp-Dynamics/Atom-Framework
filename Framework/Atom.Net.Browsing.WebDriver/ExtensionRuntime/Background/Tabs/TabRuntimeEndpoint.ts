import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { TabContextEnvelope } from '../../Shared/Protocol/TabContextEnvelope';

export interface ITabRuntimeEndpoint {
    readonly tabId: string;
    readonly windowId?: string;
    readonly connected: boolean;

    send(message: BridgeMessage): Promise<void>;

    applyContext(context: TabContextEnvelope): Promise<void>;

    disconnect(reason?: string): Promise<void>;
}