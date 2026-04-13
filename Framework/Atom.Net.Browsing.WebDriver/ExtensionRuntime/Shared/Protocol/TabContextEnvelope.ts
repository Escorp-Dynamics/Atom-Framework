export type TabContextNavigationInterceptionMode = 'webrequest' | 'proxy';

export interface TabContextViewportEnvelope {
    width: number;
    height: number;
}

export interface TabContextVirtualMediaDevicesEnvelope {
    audioInputEnabled?: boolean;
    audioInputLabel?: string;
    audioInputBrowserDeviceId?: string;
    videoInputEnabled?: boolean;
    videoInputLabel?: string;
    videoInputBrowserDeviceId?: string;
    audioOutputEnabled?: boolean;
    audioOutputLabel?: string;
    groupId?: string;
}

export interface TabContextGeolocationEnvelope {
    latitude: number;
    longitude: number;
    accuracy?: number;
}

export interface TabContextClientHintBrandEnvelope {
    brand: string;
    version: string;
}

export interface TabContextClientHintsEnvelope {
    brands?: TabContextClientHintBrandEnvelope[];
    fullVersionList?: TabContextClientHintBrandEnvelope[];
    platform?: string;
    platformVersion?: string;
    mobile?: boolean;
    architecture?: string;
    model?: string;
    bitness?: string;
}

export interface TabContextEnvelope {
    sessionId: string;
    contextId: string;
    tabId: string;
    windowId?: string;
    url?: string;
    proxy?: string | null;
    navigationInterceptionMode?: TabContextNavigationInterceptionMode;
    navigationProxyRouteToken?: string;
    connectedAt: number;
    readyAt?: number;
    isReady: boolean;
    userAgent?: string;
    platform?: string;
    locale?: string;
    timezone?: string;
    languages?: string[];
    clientHints?: TabContextClientHintsEnvelope;
    viewport?: TabContextViewportEnvelope;
    deviceScaleFactor?: number;
    hardwareConcurrency?: number;
    deviceMemory?: number;
    geolocation?: TabContextGeolocationEnvelope;
    doNotTrack?: boolean;
    globalPrivacyControl?: boolean;
    maxTouchPoints?: number;
    isMobile?: boolean;
    hasTouch?: boolean;
    virtualMediaDevices?: TabContextVirtualMediaDevicesEnvelope;
}

type JsonRecord = Record<string, unknown>;

function isJsonRecord(value: unknown): value is JsonRecord {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function requireString(value: unknown, message: string): string {
    if (typeof value !== 'string' || value.trim().length === 0) {
        throw new Error(message);
    }

    return value;
}

function requireNumber(value: unknown, message: string): number {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
        throw new Error(message);
    }

    return value;
}

function requireBoolean(value: unknown, message: string): boolean {
    if (typeof value !== 'boolean') {
        throw new Error(message);
    }

    return value;
}

function readOptionalString(value: unknown, message: string): string | undefined {
    if (value === undefined) {
        return undefined;
    }

    return requireString(value, message);
}

function readOptionalNullableString(value: unknown, message: string): string | null | undefined {
    if (value === undefined || value === null) {
        return value as null | undefined;
    }

    return requireString(value, message);
}

function readOptionalNavigationInterceptionMode(value: unknown, message: string): TabContextNavigationInterceptionMode | undefined {
    const mode = readOptionalString(value, message);
    if (mode === undefined) {
        return undefined;
    }

    if (mode === 'webrequest' || mode === 'proxy') {
        return mode;
    }

    throw new Error(message);
}

function readOptionalNumber(value: unknown, message: string): number | undefined {
    if (value === undefined) {
        return undefined;
    }

    return requireNumber(value, message);
}

function readOptionalBoolean(value: unknown, message: string): boolean | undefined {
    if (value === undefined) {
        return undefined;
    }

    return requireBoolean(value, message);
}

function readOptionalStringArray(value: unknown, message: string): string[] | undefined {
    if (value === undefined) {
        return undefined;
    }

    if (!Array.isArray(value)) {
        throw new Error(message);
    }

    return value.map((item) => requireString(item, message));
}

function readOptionalViewport(value: unknown, message: string): TabContextViewportEnvelope | undefined {
    if (value === undefined) {
        return undefined;
    }

    if (!isJsonRecord(value)) {
        throw new Error(message);
    }

    return {
        width: requireNumber(value.width, message),
        height: requireNumber(value.height, message),
    };
}

function readOptionalGeolocation(value: unknown, message: string): TabContextGeolocationEnvelope | undefined {
    if (value === undefined) {
        return undefined;
    }

    if (!isJsonRecord(value)) {
        throw new Error(message);
    }

    return {
        latitude: requireNumber(value.latitude, message),
        longitude: requireNumber(value.longitude, message),
        accuracy: readOptionalNumber(value.accuracy, message),
    };
}

function readOptionalClientHintBrandArray(value: unknown, message: string): TabContextClientHintBrandEnvelope[] | undefined {
    if (value === undefined) {
        return undefined;
    }

    if (!Array.isArray(value)) {
        throw new Error(message);
    }

    return value.map((item) => {
        if (!isJsonRecord(item)) {
            throw new Error(message);
        }

        return {
            brand: requireString(item.brand, message),
            version: requireString(item.version, message),
        };
    });
}

function readOptionalClientHints(value: unknown, message: string): TabContextClientHintsEnvelope | undefined {
    if (value === undefined) {
        return undefined;
    }

    if (!isJsonRecord(value)) {
        throw new Error(message);
    }

    return {
        brands: readOptionalClientHintBrandArray(value.brands, message),
        fullVersionList: readOptionalClientHintBrandArray(value.fullVersionList, message),
        platform: readOptionalString(value.platform, message),
        platformVersion: readOptionalString(value.platformVersion, message),
        mobile: readOptionalBoolean(value.mobile, message),
        architecture: readOptionalString(value.architecture, message),
        model: readOptionalString(value.model, message),
        bitness: readOptionalString(value.bitness, message),
    };
}

function readOptionalVirtualMediaDevices(value: unknown, message: string): TabContextVirtualMediaDevicesEnvelope | undefined {
    if (value === undefined) {
        return undefined;
    }

    if (!isJsonRecord(value)) {
        throw new Error(message);
    }

    return {
        audioInputEnabled: readOptionalBoolean(value.audioInputEnabled, message),
        audioInputLabel: readOptionalString(value.audioInputLabel, message),
        audioInputBrowserDeviceId: readOptionalString(value.audioInputBrowserDeviceId, message),
        videoInputEnabled: readOptionalBoolean(value.videoInputEnabled, message),
        videoInputLabel: readOptionalString(value.videoInputLabel, message),
        videoInputBrowserDeviceId: readOptionalString(value.videoInputBrowserDeviceId, message),
        audioOutputEnabled: readOptionalBoolean(value.audioOutputEnabled, message),
        audioOutputLabel: readOptionalString(value.audioOutputLabel, message),
        groupId: readOptionalString(value.groupId, message),
    };
}

export function validateTabContextEnvelope(value: unknown): TabContextEnvelope {
    if (!isJsonRecord(value)) {
        throw new Error('Контекст вкладки имеет неверную форму');
    }

    const context: TabContextEnvelope = {
        sessionId: requireString(value.sessionId, 'Контекст вкладки не содержит sessionId'),
        contextId: requireString(value.contextId, 'Контекст вкладки не содержит contextId'),
        tabId: requireString(value.tabId, 'Контекст вкладки не содержит tabId'),
        connectedAt: requireNumber(value.connectedAt, 'Контекст вкладки содержит неверный connectedAt'),
        isReady: requireBoolean(value.isReady, 'Контекст вкладки содержит неверный isReady'),
    };

    if (value.windowId !== undefined) {
        context.windowId = requireString(value.windowId, 'Контекст вкладки содержит пустой windowId');
    }

    if (value.url !== undefined) {
        context.url = requireString(value.url, 'Контекст вкладки содержит пустой url');
    }

    if (value.proxy !== undefined) {
        context.proxy = readOptionalNullableString(value.proxy, 'Контекст вкладки содержит неверный proxy');
    }

    context.navigationInterceptionMode = readOptionalNavigationInterceptionMode(
        value.navigationInterceptionMode,
        'Контекст вкладки содержит неверный navigationInterceptionMode');

    context.navigationProxyRouteToken = readOptionalString(
        value.navigationProxyRouteToken,
        'Контекст вкладки содержит неверный navigationProxyRouteToken');

    if (value.readyAt !== undefined) {
        context.readyAt = requireNumber(value.readyAt, 'Контекст вкладки содержит неверный readyAt');
    }

    context.userAgent = readOptionalString(value.userAgent, 'Контекст вкладки содержит неверный userAgent');
    context.platform = readOptionalString(value.platform, 'Контекст вкладки содержит неверный platform');
    context.locale = readOptionalString(value.locale, 'Контекст вкладки содержит неверный locale');
    context.timezone = readOptionalString(value.timezone, 'Контекст вкладки содержит неверный timezone');
    context.languages = readOptionalStringArray(value.languages, 'Контекст вкладки содержит неверный languages');
    context.clientHints = readOptionalClientHints(value.clientHints, 'Контекст вкладки содержит неверный clientHints');
    context.viewport = readOptionalViewport(value.viewport, 'Контекст вкладки содержит неверный viewport');
    context.deviceScaleFactor = readOptionalNumber(value.deviceScaleFactor, 'Контекст вкладки содержит неверный deviceScaleFactor');
    context.hardwareConcurrency = readOptionalNumber(value.hardwareConcurrency, 'Контекст вкладки содержит неверный hardwareConcurrency');
    context.deviceMemory = readOptionalNumber(value.deviceMemory, 'Контекст вкладки содержит неверный deviceMemory');
    context.geolocation = readOptionalGeolocation(value.geolocation, 'Контекст вкладки содержит неверный geolocation');
    context.doNotTrack = readOptionalBoolean(value.doNotTrack, 'Контекст вкладки содержит неверный doNotTrack');
    context.globalPrivacyControl = readOptionalBoolean(value.globalPrivacyControl, 'Контекст вкладки содержит неверный globalPrivacyControl');
    context.maxTouchPoints = readOptionalNumber(value.maxTouchPoints, 'Контекст вкладки содержит неверный maxTouchPoints');
    context.isMobile = readOptionalBoolean(value.isMobile, 'Контекст вкладки содержит неверный isMobile');
    context.hasTouch = readOptionalBoolean(value.hasTouch, 'Контекст вкладки содержит неверный hasTouch');
    context.virtualMediaDevices = readOptionalVirtualMediaDevices(value.virtualMediaDevices, 'Контекст вкладки содержит неверный virtualMediaDevices');

    return context;
}