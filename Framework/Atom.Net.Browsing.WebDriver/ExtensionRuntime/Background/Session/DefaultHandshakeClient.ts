import type { RuntimeConfig } from '../../Shared/Config/RuntimeConfig';
import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import {
    createHandshakeRequestMessage,
    parseHandshakeResponse,
    type HandshakeCapabilities,
    type HandshakeResult,
    type IHandshakeClient,
} from './HandshakeClient';

export class DefaultHandshakeClient implements IHandshakeClient {
    public constructor(
        private readonly requestIdFactory: () => string = createDefaultRequestId,
        private readonly capabilitiesFactory: (config: RuntimeConfig) => HandshakeCapabilities | undefined = () => undefined,
    ) { }

    public createRequest(config: RuntimeConfig): BridgeMessage {
        return createHandshakeRequestMessage(
            config,
            this.requestIdFactory(),
            this.capabilitiesFactory(config),
        );
    }

    public parseResponse(message: BridgeMessage): HandshakeResult {
        return parseHandshakeResponse(message);
    }
}

function createDefaultRequestId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID();
    }

    return `handshake_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}