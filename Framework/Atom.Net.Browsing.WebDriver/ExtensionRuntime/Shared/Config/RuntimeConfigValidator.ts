import type { RuntimeConfig } from './RuntimeConfig';
import type { RuntimeFeatureFlags } from './RuntimeFeatureFlags';

type JsonRecord = Record<string, unknown>;

function isJsonRecord(value: unknown): value is JsonRecord {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function requireNonEmptyString(value: unknown, message: string): string {
    if (typeof value !== 'string' || value.trim().length === 0) {
        throw new Error(message);
    }

    return value;
}

function requirePositiveInteger(value: unknown, message: string): number {
    if (typeof value !== 'number' || !Number.isInteger(value) || value <= 0) {
        throw new Error(message);
    }

    return value;
}

function requireOptionalNonEmptyString(value: unknown, message: string): string | undefined {
    if (value === undefined) {
        return undefined;
    }

    return requireNonEmptyString(value, message);
}

function requireOptionalPositiveInteger(value: unknown, message: string): number | undefined {
    if (value === undefined) {
        return undefined;
    }

    return requirePositiveInteger(value, message);
}

function requireBoolean(value: unknown, message: string): boolean {
    if (typeof value !== 'boolean') {
        throw new Error(message);
    }

    return value;
}

export function validateRuntimeFeatureFlags(value: unknown): RuntimeFeatureFlags {
    if (!isJsonRecord(value)) {
        throw new Error('Флаги runtime имеют неверную форму');
    }

    return {
        enableNavigationEvents: requireBoolean(value.enableNavigationEvents, 'Флаг enableNavigationEvents должен быть логическим значением'),
        enableCallbackHooks: requireBoolean(value.enableCallbackHooks, 'Флаг enableCallbackHooks должен быть логическим значением'),
        enableInterception: requireBoolean(value.enableInterception, 'Флаг enableInterception должен быть логическим значением'),
        enableDiagnostics: requireBoolean(value.enableDiagnostics, 'Флаг enableDiagnostics должен быть логическим значением'),
        enableKeepAlive: requireBoolean(value.enableKeepAlive, 'Флаг enableKeepAlive должен быть логическим значением'),
    };
}

export function validateRuntimeConfig(value: unknown): RuntimeConfig {
    if (!isJsonRecord(value)) {
        throw new Error('Конфигурация runtime имеет неверную форму');
    }

    return {
        host: requireNonEmptyString(value.host, 'Конфигурация runtime не содержит host'),
        port: requirePositiveInteger(value.port, 'Конфигурация runtime содержит неверный port'),
        proxyPort: requireOptionalPositiveInteger(value.proxyPort, 'Конфигурация runtime содержит неверный proxyPort'),
        transportUrl: requireOptionalNonEmptyString(value.transportUrl, 'Конфигурация runtime содержит неверный transportUrl'),
        sessionId: requireNonEmptyString(value.sessionId, 'Конфигурация runtime не содержит sessionId'),
        secret: requireNonEmptyString(value.secret, 'Конфигурация runtime не содержит secret'),
        protocolVersion: requirePositiveInteger(value.protocolVersion, 'Конфигурация runtime содержит неверную protocolVersion'),
        browserFamily: requireNonEmptyString(value.browserFamily, 'Конфигурация runtime не содержит browserFamily'),
        extensionVersion: requireNonEmptyString(value.extensionVersion, 'Конфигурация runtime не содержит extensionVersion'),
        featureFlags: validateRuntimeFeatureFlags(value.featureFlags),
    };
}