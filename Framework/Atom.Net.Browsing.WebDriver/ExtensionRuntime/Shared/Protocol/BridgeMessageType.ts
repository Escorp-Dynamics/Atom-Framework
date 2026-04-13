export const bridgeMessageTypes = [
    'Request',
    'Response',
    'Event',
    'Handshake',
    'Ping',
    'Pong',
] as const;

export type BridgeMessageType = (typeof bridgeMessageTypes)[number];