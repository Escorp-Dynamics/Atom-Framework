import { bridgeCommands } from './BridgeCommand';
import type { BridgeCommand } from './BridgeCommand';
import { bridgeEventNames } from './BridgeEventName';
import type { BridgeEventName } from './BridgeEventName';
import type { BridgeMessage } from './BridgeMessage';
import { bridgeMessageTypes } from './BridgeMessageType';
import type { BridgeMessageType } from './BridgeMessageType';
import { bridgeStatuses } from './BridgeStatus';
import type { BridgeStatus } from './BridgeStatus';
import type { JsonValue } from './JsonValue';

const bridgeCommandSet = new Set<string>(bridgeCommands);
const bridgeEventNameSet = new Set<string>(bridgeEventNames);
const bridgeMessageTypeSet = new Set<string>(bridgeMessageTypes);
const bridgeStatusSet = new Set<string>(bridgeStatuses);

type JsonRecord = Record<string, unknown>;

function isJsonRecord(value: unknown): value is JsonRecord {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isFiniteNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value);
}

function isNonEmptyString(value: unknown): value is string {
    return typeof value === 'string' && value.trim().length > 0;
}

function isBridgeMessageType(value: string): value is BridgeMessageType {
    return bridgeMessageTypeSet.has(value);
}

function isBridgeCommand(value: string): value is BridgeCommand {
    return bridgeCommandSet.has(value);
}

function isBridgeEventName(value: string): value is BridgeEventName {
    return bridgeEventNameSet.has(value);
}

function isBridgeStatus(value: string): value is BridgeStatus {
    return bridgeStatusSet.has(value);
}

function isJsonValue(value: unknown): value is JsonValue {
    if (value === null) {
        return true;
    }

    if (typeof value === 'string' || typeof value === 'boolean') {
        return true;
    }

    if (typeof value === 'number') {
        return Number.isFinite(value);
    }

    if (Array.isArray(value)) {
        return value.every(isJsonValue);
    }

    if (!isJsonRecord(value)) {
        return false;
    }

    return Object.values(value).every(isJsonValue);
}

function requireNonEmptyString(value: unknown, message: string): string {
    if (!isNonEmptyString(value)) {
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

export function validateBridgeMessageEnvelope(value: unknown): BridgeMessage {
    if (!isJsonRecord(value)) {
        throw new Error('Мостовое сообщение имеет неверную форму');
    }

    const id = requireNonEmptyString(value.id, 'Мостовое сообщение не содержит идентификатор');
    const type = requireNonEmptyString(value.type, 'Мостовое сообщение не содержит тип');
    if (!isBridgeMessageType(type)) {
        throw new Error(`Мостовое сообщение содержит неподдерживаемый тип '${type}'`);
    }

    const message: BridgeMessage = {
        id,
        type,
    };

    const windowId = requireOptionalNonEmptyString(value.windowId, 'Мостовое сообщение содержит пустой windowId');
    if (windowId !== undefined) {
        message.windowId = windowId;
    }

    const tabId = requireOptionalNonEmptyString(value.tabId, 'Мостовое сообщение содержит пустой tabId');
    if (tabId !== undefined) {
        message.tabId = tabId;
    }

    if (value.command !== undefined) {
        const command = requireNonEmptyString(value.command, 'Мостовое сообщение содержит пустую команду');
        if (!isBridgeCommand(command)) {
            throw new Error(`Мостовое сообщение содержит неподдерживаемую команду '${command}'`);
        }

        message.command = command;
    }

    if (value.event !== undefined) {
        const eventName = requireNonEmptyString(value.event, 'Мостовое сообщение содержит пустое имя события');
        if (!isBridgeEventName(eventName)) {
            throw new Error(`Мостовое сообщение содержит неподдерживаемое событие '${eventName}'`);
        }

        message.event = eventName;
    }

    if (value.status !== undefined) {
        const status = requireNonEmptyString(value.status, 'Мостовое сообщение содержит пустой статус');
        if (!isBridgeStatus(status)) {
            throw new Error(`Мостовое сообщение содержит неподдерживаемый статус '${status}'`);
        }

        message.status = status;
    }

    if (value.payload !== undefined) {
        if (!isJsonValue(value.payload)) {
            throw new Error('Мостовое сообщение содержит неверный payload');
        }

        message.payload = value.payload;
    }

    const error = requireOptionalNonEmptyString(value.error, 'Мостовое сообщение содержит пустую ошибку');
    if (error !== undefined) {
        message.error = error;
    }

    if (value.timestamp !== undefined) {
        if (!isFiniteNumber(value.timestamp)) {
            throw new Error('Мостовое сообщение содержит неверный timestamp');
        }

        message.timestamp = value.timestamp;
    }

    if (message.type === 'Request' && message.command === undefined) {
        throw new Error('Мостовой запрос не содержит команду');
    }

    if (message.type === 'Response' && message.status === undefined) {
        throw new Error('Мостовой ответ не содержит статус');
    }

    if (message.type === 'Event' && message.event === undefined) {
        throw new Error('Мостовое событие не содержит имя события');
    }

    return message;
}