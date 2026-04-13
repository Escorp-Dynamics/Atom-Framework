export const bridgeEventNames = [
    'RequestIntercepted',
    'ResponseReceived',
    'FrameDetached',
    'DomContentLoaded',
    'NavigationCompleted',
    'PageLoaded',
    'ScriptError',
    'ConsoleMessage',
    'Callback',
    'CallbackFinalized',
    'TabConnected',
    'TabDisconnected',
] as const;

export type BridgeEventName = (typeof bridgeEventNames)[number];