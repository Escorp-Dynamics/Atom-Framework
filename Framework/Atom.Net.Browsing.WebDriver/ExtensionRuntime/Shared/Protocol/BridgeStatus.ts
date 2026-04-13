export const bridgeStatuses = [
    'Ok',
    'Error',
    'NotFound',
    'Timeout',
    'Disconnected',
] as const;

export type BridgeStatus = (typeof bridgeStatuses)[number];