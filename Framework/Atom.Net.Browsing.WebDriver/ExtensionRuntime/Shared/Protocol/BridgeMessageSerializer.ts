import type { BridgeMessage } from './BridgeMessage';
import { validateBridgeMessageEnvelope } from './TransportEnvelopeValidator';

export function serializeBridgeMessage(message: BridgeMessage): string {
    validateBridgeMessageEnvelope(message);
    return JSON.stringify(message);
}

export function deserializeBridgeMessage(payload: string): BridgeMessage {
    let parsed: unknown;

    try {
        parsed = JSON.parse(payload);
    } catch {
        throw new Error('Мостовое сообщение содержит неверный JSON');
    }

    return validateBridgeMessageEnvelope(parsed);
}