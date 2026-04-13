import { type RuntimeConfig } from '../../Shared/Config';

export function emitBackgroundDebugEvent(config: RuntimeConfig | null, kind: string, details: unknown): void {
    if (config === null || !config.featureFlags.enableDiagnostics) {
        return;
    }

    const url = `http://${config.host}:${config.port}/debug-event?secret=${encodeURIComponent(config.secret)}`;
    const payload = JSON.stringify({
        kind,
        sessionId: config.sessionId,
        details,
    });

    void fetch(url, {
        method: 'POST',
        headers: {
            'content-type': 'text/plain;charset=UTF-8',
        },
        body: payload,
        keepalive: true,
    }).catch((error) => {
        console.debug('[фоновый вход] Не удалось передать debug-event', {
            kind,
            error: toErrorMessage(error),
        });
    });
}

export function toErrorMessage(error: unknown): string {
    if (error instanceof Error && error.message.trim().length > 0) {
        return error.message;
    }

    return String(error);
}