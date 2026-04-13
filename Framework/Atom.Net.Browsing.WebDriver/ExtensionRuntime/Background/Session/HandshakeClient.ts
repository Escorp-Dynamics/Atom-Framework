import type { RuntimeConfig } from '../../Shared/Config/RuntimeConfig';
import type { BridgeMessage } from '../../Shared/Protocol/BridgeMessage';
import type { JsonValue } from '../../Shared/Protocol/JsonValue';

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

export interface HandshakeCapabilities {
    readonly [key: string]: JsonValue;
}

export interface HandshakeRequestPayload {
    [key: string]: JsonValue | undefined;
    sessionId: string;
    secret: string;
    protocolVersion: number;
    browserFamily: string;
    extensionVersion: string;
    browserVersion?: string;
    capabilities?: HandshakeCapabilities;
}

export interface HandshakeAcceptPayload {
    [key: string]: JsonValue | undefined;
    sessionId: string;
    negotiatedProtocolVersion: number;
    requestTimeoutMs: number;
    pingIntervalMs: number;
    maxMessageSize: number;
    serverTimeUnixMs?: number;
}

export interface HandshakeRejectPayload {
    [key: string]: JsonValue | undefined;
    retryable?: boolean;
    supportedProtocolVersion?: number;
}

export interface HandshakeResult {
    accepted: boolean;
    message: BridgeMessage;
    acceptPayload?: HandshakeAcceptPayload;
    rejectPayload?: HandshakeRejectPayload;
}

export interface IHandshakeClient {
    createRequest(config: RuntimeConfig): BridgeMessage;
    parseResponse(message: BridgeMessage): HandshakeResult;
}

export function createHandshakeRequestPayload(
    config: RuntimeConfig,
    capabilities?: HandshakeCapabilities,
): HandshakeRequestPayload {
    const payload: HandshakeRequestPayload = {
        sessionId: config.sessionId,
        secret: config.secret,
        protocolVersion: config.protocolVersion,
        browserFamily: config.browserFamily,
        extensionVersion: config.extensionVersion,
    };

    if (capabilities !== undefined && Object.keys(capabilities).length > 0) {
        payload.capabilities = capabilities;
    }

    return payload;
}

export function createHandshakeRequestMessage(
    config: RuntimeConfig,
    requestId: string,
    capabilities?: HandshakeCapabilities,
): BridgeMessage {
    const payload = createHandshakeRequestPayload(config, capabilities) as JsonValue;

    return {
        id: requestId,
        type: 'Handshake',
        payload,
    };
}

export function parseHandshakeAcceptPayload(payload: JsonValue | undefined): HandshakeAcceptPayload {
    if (!isJsonRecord(payload)) {
        throw new Error('Данные принятия соединения имеют неверную форму');
    }

    const acceptPayload: HandshakeAcceptPayload = {
        sessionId: requireNonEmptyString(payload.sessionId, 'Данные принятия соединения не содержат идентификатор сеанса'),
        negotiatedProtocolVersion: requirePositiveInteger(payload.negotiatedProtocolVersion, 'Данные принятия соединения содержат неверную версию протокола'),
        requestTimeoutMs: requirePositiveInteger(payload.requestTimeoutMs, 'Данные принятия соединения содержат неверный таймаут запроса'),
        pingIntervalMs: requirePositiveInteger(payload.pingIntervalMs, 'Данные принятия соединения содержат неверный интервал контрольного запроса'),
        maxMessageSize: requirePositiveInteger(payload.maxMessageSize, 'Данные принятия соединения содержат неверный размер сообщения'),
    };

    if (payload.serverTimeUnixMs !== undefined) {
        acceptPayload.serverTimeUnixMs = requirePositiveInteger(payload.serverTimeUnixMs, 'Данные принятия соединения содержат неверное время сервера');
    }

    return acceptPayload;
}

export function parseHandshakeRejectPayload(payload: JsonValue | undefined): HandshakeRejectPayload | undefined {
    if (payload === undefined) {
        return undefined;
    }

    if (!isJsonRecord(payload)) {
        throw new Error('Данные отклонения соединения имеют неверную форму');
    }

    const rejectPayload: HandshakeRejectPayload = {};

    if (payload.retryable !== undefined) {
        if (typeof payload.retryable !== 'boolean') {
            throw new Error('Данные отклонения соединения содержат неверный признак повтора');
        }

        rejectPayload.retryable = payload.retryable;
    }

    if (payload.supportedProtocolVersion !== undefined) {
        rejectPayload.supportedProtocolVersion = requirePositiveInteger(
            payload.supportedProtocolVersion,
            'Данные отклонения соединения содержат неверную поддерживаемую версию протокола',
        );
    }

    return rejectPayload;
}

export function parseHandshakeResponse(message: BridgeMessage): HandshakeResult {
    if (message.type !== 'Handshake') {
        throw new Error('Ожидался ответ согласования от мостового слоя');
    }

    if (message.status === 'Ok') {
        return {
            accepted: true,
            message,
            acceptPayload: parseHandshakeAcceptPayload(message.payload),
        };
    }

    if (message.status === 'Error') {
        return {
            accepted: false,
            message,
            rejectPayload: parseHandshakeRejectPayload(message.payload),
        };
    }

    throw new Error('Ответ согласования содержит неподдерживаемый статус');
}