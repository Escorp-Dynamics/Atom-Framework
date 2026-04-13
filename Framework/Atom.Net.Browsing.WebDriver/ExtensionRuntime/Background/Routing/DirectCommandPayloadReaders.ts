type JsonRecord = Record<string, unknown>;

export function readPayloadString(payload: unknown, key: string, errorMessage: string): string {
    if (!isRecord(payload)) {
        throw new Error(errorMessage);
    }

    const value = payload[key];
    if (typeof value !== 'string' || value.trim().length === 0) {
        throw new Error(errorMessage);
    }

    return value;
}

export function readOptionalPayloadString(payload: unknown, key: string): string | undefined {
    if (!isRecord(payload)) {
        return undefined;
    }

    const value = payload[key];
    return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

export function readOptionalPayloadBoolean(payload: unknown, key: string): boolean | undefined {
    if (!isRecord(payload)) {
        return undefined;
    }

    const value = payload[key];
    return typeof value === 'boolean' ? value : undefined;
}

export function readOptionalPayloadInteger(payload: unknown, key: string): number | undefined {
    if (!isRecord(payload)) {
        return undefined;
    }

    const parsed = typeof payload[key] === 'string' ? Number(payload[key]) : payload[key];
    return typeof parsed === 'number' && Number.isInteger(parsed) ? parsed : undefined;
}

export function readPayloadValueString(payload: unknown, key: string, errorMessage: string): string {
    if (!isRecord(payload)) {
        throw new Error(errorMessage);
    }

    const value = payload[key];
    if (typeof value !== 'string') {
        throw new Error(errorMessage);
    }

    return value;
}

export function readWindowId(payload: unknown): number {
    if (!isRecord(payload)) {
        throw new Error('Команда окна не содержит идентификатор окна');
    }

    return requireInteger(payload.windowId, 'Команда окна содержит неверный идентификатор окна');
}

export function readMessageTabId(payload: unknown): number | undefined {
    if (!isRecord(payload) || payload.tabId === undefined) {
        return undefined;
    }

    return requireInteger(payload.tabId, 'Команда окна содержит неверный идентификатор вкладки');
}

export function readWindowPosition(payload: unknown): { left?: number; top?: number } | undefined {
    if (!isRecord(payload) || !isRecord(payload.windowPosition)) {
        return undefined;
    }

    const left = typeof payload.windowPosition.x === 'number' && Number.isFinite(payload.windowPosition.x)
        ? payload.windowPosition.x
        : undefined;
    const top = typeof payload.windowPosition.y === 'number' && Number.isFinite(payload.windowPosition.y)
        ? payload.windowPosition.y
        : undefined;

    if (left === undefined && top === undefined) {
        return undefined;
    }

    const position: { left?: number; top?: number } = {};
    if (left !== undefined) {
        position.left = left;
    }
    if (top !== undefined) {
        position.top = top;
    }
    return position;
}

export function requireInteger(value: unknown, errorMessage: string): number {
    const parsed = typeof value === 'string' ? Number(value) : value;
    if (typeof parsed !== 'number' || !Number.isInteger(parsed) || parsed <= 0) {
        throw new Error(errorMessage);
    }

    return parsed;
}

function isRecord(value: unknown): value is JsonRecord {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}