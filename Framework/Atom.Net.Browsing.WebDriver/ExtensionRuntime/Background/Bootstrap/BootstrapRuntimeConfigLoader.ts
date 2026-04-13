import { validateRuntimeConfig, type RuntimeConfig } from '../../Shared/Config';
import { type BrowserHost, invokeBrowserCall } from '../Browser/BrowserApi';

type JsonRecord = Record<string, unknown>;
type BrowserRuntimeGlobalState = typeof globalThis & {
    browser?: BrowserHost;
    chrome?: BrowserHost;
};

export type BootstrapRuntimeConfigSource = 'managed-storage' | 'local-storage' | 'bundled-file';

export interface BootstrapRuntimeConfigResult {
    readonly config: RuntimeConfig;
    readonly source: BootstrapRuntimeConfigSource;
}

export async function loadBootstrapRuntimeConfigWithSource(runtime: any, browserHost: BrowserHost): Promise<BootstrapRuntimeConfigResult> {
    if (detectBrowserFamily() === 'firefox') {
        const localConfig = await tryLoadStorageBootstrapRuntimeConfig(runtime, browserHost.storage?.local, ['config']);
        if (localConfig !== null) {
            return { config: localConfig, source: 'local-storage' };
        }

        try {
            return {
                config: await loadBundledBootstrapRuntimeConfig(runtime),
                source: 'bundled-file',
            };
        } catch {
            // Fall back to managed storage only after Firefox-specific local and bundled sources are exhausted.
        }

        const managedFirefoxConfig = await tryLoadStorageBootstrapRuntimeConfig(runtime, browserHost.storage?.managed, null);
        if (managedFirefoxConfig !== null) {
            return { config: managedFirefoxConfig, source: 'managed-storage' };
        }
    }

    const managedConfig = await tryLoadStorageBootstrapRuntimeConfig(runtime, browserHost.storage?.managed, null);
    if (managedConfig !== null) {
        return { config: managedConfig, source: 'managed-storage' };
    }

    const localConfig = await tryLoadStorageBootstrapRuntimeConfig(runtime, browserHost.storage?.local, ['config']);
    if (localConfig !== null) {
        return { config: localConfig, source: 'local-storage' };
    }

    return {
        config: await loadBundledBootstrapRuntimeConfig(runtime),
        source: 'bundled-file',
    };
}

export async function loadStorageBootstrapRuntimeConfig(runtime: any, storageArea: any, query: unknown): Promise<RuntimeConfig | null> {
    if (storageArea === undefined) {
        return null;
    }

    const items = await invokeBrowserCall<any>(runtime, storageArea.get, storageArea, query);
    const candidate = query === null ? items : isRecord(items.config) ? items.config : items;
    if (!isRecord(candidate) || Object.keys(candidate).length === 0) {
        return null;
    }

    return normalizeBootstrapConfig(candidate, runtime);
}

async function tryLoadStorageBootstrapRuntimeConfig(runtime: any, storageArea: any, query: unknown): Promise<RuntimeConfig | null> {
    try {
        return await loadStorageBootstrapRuntimeConfig(runtime, storageArea, query);
    } catch {
        return null;
    }
}

export async function loadBundledBootstrapRuntimeConfig(runtime: any): Promise<RuntimeConfig> {
    const configUrl = runtime.getURL('config.json');

    let rawJson: string | undefined;
    let parsed: unknown;
    try {
        const response = await fetch(configUrl);
        if (!response.ok) {
            throw new Error('Файл конфигурации запуска недоступен');
        }

        if (typeof response.text === 'function') {
            rawJson = await response.text();
        } else if (typeof response.json === 'function') {
            parsed = await response.json();
        } else {
            throw new Error('Файл конфигурации запуска недоступен');
        }
    } catch {
        rawJson = await loadBundledBootstrapRuntimeConfigViaXmlHttpRequest(configUrl);
    }

    if (parsed === undefined) {
        if (typeof rawJson !== 'string') {
            throw new Error('Файл конфигурации запуска недоступен');
        }

        try {
            parsed = JSON.parse(rawJson);
        } catch {
            throw new Error('Файл конфигурации запуска содержит неверный JSON');
        }
    }

    const normalized = normalizeBootstrapConfig(parsed, runtime);
    return validateRuntimeConfig(normalized);
}

async function loadBundledBootstrapRuntimeConfigViaXmlHttpRequest(configUrl: string): Promise<string> {
    if (typeof XMLHttpRequest !== 'function') {
        throw new Error('Файл конфигурации запуска недоступен');
    }

    return await new Promise<string>((resolve, reject) => {
        const request = new XMLHttpRequest();
        request.open('GET', configUrl, true);

        request.onload = () => {
            if (request.status >= 200 && request.status < 300) {
                resolve(request.responseText);
                return;
            }

            reject(new Error('Файл конфигурации запуска недоступен'));
        };

        request.onerror = () => reject(new Error('Файл конфигурации запуска недоступен'));
        request.onabort = () => reject(new Error('Файл конфигурации запуска недоступен'));

        try {
            request.send();
        } catch {
            reject(new Error('Файл конфигурации запуска недоступен'));
        }
    });
}

export function normalizeBootstrapConfig(value: unknown, runtime: any): RuntimeConfig {
    try {
        return validateRuntimeConfig(value);
    } catch {
        if (!isRecord(value)) {
            throw new Error('Конфигурация запуска имеет неверную форму');
        }

        return {
            host: requireNonEmptyString(value.host, 'Конфигурация запуска не содержит адрес узла'),
            port: requirePositiveInteger(value.port, 'Конфигурация запуска содержит неверный номер порта'),
            proxyPort: typeof value.proxyPort === 'number' && Number.isInteger(value.proxyPort) && value.proxyPort > 0
                ? value.proxyPort
                : undefined,
            transportUrl: typeof value.transportUrl === 'string' && value.transportUrl.trim().length > 0 ? value.transportUrl : undefined,
            secret: requireNonEmptyString(value.secret, 'Конфигурация запуска не содержит секрет'),
            sessionId: typeof value.sessionId === 'string' && value.sessionId.trim().length > 0 ? value.sessionId : createSessionId(),
            protocolVersion: typeof value.protocolVersion === 'number' && Number.isInteger(value.protocolVersion) && value.protocolVersion > 0 ? value.protocolVersion : 1,
            browserFamily: typeof value.browserFamily === 'string' && value.browserFamily.trim().length > 0 ? value.browserFamily : detectBrowserFamily(),
            extensionVersion: typeof value.extensionVersion === 'string' && value.extensionVersion.trim().length > 0
                ? value.extensionVersion
                : runtime.getManifest?.().version ?? '0.0.0-stage1',
            featureFlags: {
                enableNavigationEvents: readBoolean(value, 'enableNavigationEvents', true),
                enableCallbackHooks: readBoolean(value, 'enableCallbackHooks', true),
                enableInterception: readBoolean(value, 'enableInterception', true),
                enableDiagnostics: readBoolean(value, 'enableDiagnostics', true),
                enableKeepAlive: readBoolean(value, 'enableKeepAlive', true),
            },
        };
    }
}

export function detectBrowserFamily(): string {
    const runtimeState = globalThis as BrowserRuntimeGlobalState;
    if ('browser' in runtimeState && !('chrome' in runtimeState)) {
        return 'firefox';
    }

    return 'chromium';
}

function isRecord(value: unknown): value is JsonRecord {
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

function readBoolean(source: JsonRecord, key: string, fallback: boolean): boolean {
    const featureFlags = isRecord(source.featureFlags) ? source.featureFlags : source;
    const value = featureFlags[key];
    return typeof value === 'boolean' ? value : fallback;
}

function createSessionId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `background_${crypto.randomUUID()}`;
    }

    return `background_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}