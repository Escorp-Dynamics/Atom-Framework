import { bootstrapBackgroundRuntime } from './Background/BackgroundRuntimeHost';

void bootstrapBackgroundRuntime().catch((error) => {
    console.error('[фоновый вход] Фатальный сбой bootstrap background runtime', error);
});

function toErrorMessage(error: unknown): string {
    if (error instanceof Error && error.message.trim().length > 0) {
        return error.message;
    }

    return String(error);
}