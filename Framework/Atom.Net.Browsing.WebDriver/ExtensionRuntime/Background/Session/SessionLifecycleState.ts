export const sessionLifecycleStates = [
    'Idle',
    'ConfigLoaded',
    'TransportConnecting',
    'Handshaking',
    'Ready',
    'Degraded',
    'Closed',
] as const;

export type SessionLifecycleState = (typeof sessionLifecycleStates)[number];

const allowedSessionTransitions: Record<SessionLifecycleState, readonly SessionLifecycleState[]> = {
    Idle: ['ConfigLoaded', 'Closed'],
    ConfigLoaded: ['TransportConnecting', 'Closed'],
    TransportConnecting: ['Handshaking', 'Degraded', 'Closed'],
    Handshaking: ['Ready', 'Degraded', 'Closed'],
    Ready: ['Degraded', 'Closed'],
    Degraded: ['TransportConnecting', 'Handshaking', 'Closed'],
    Closed: [],
};

export function canTransitionSessionState(from: SessionLifecycleState, to: SessionLifecycleState): boolean {
    if (from === to) {
        return true;
    }

    return allowedSessionTransitions[from].includes(to);
}

export function assertSessionStateTransition(from: SessionLifecycleState, to: SessionLifecycleState): void {
    if (!canTransitionSessionState(from, to)) {
        throw new Error(`Переход состояния сеанса '${from}' -> '${to}' не поддерживается`);
    }
}