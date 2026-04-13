import type { BridgeEventName } from './BridgeEventName';
import type { BridgeStatus } from './BridgeStatus';
import type { ContentCommandName } from './ContentCommandName';
import type { JsonValue } from './JsonValue';
import type { TabContextEnvelope } from './TabContextEnvelope';

export const mainWorldExecutionStatuses = ['ok', 'err'] as const;

export type MainWorldExecutionStatus = (typeof mainWorldExecutionStatuses)[number];

export interface BackgroundToContentCommandEnvelope {
    id: string;
    command: ContentCommandName;
    payload?: JsonValue;
}

export interface ContentResponseEnvelope {
    action: 'response';
    id: string;
    status: BridgeStatus;
    payload?: JsonValue;
    error?: string;
}

export interface ContentEventEnvelope {
    action: 'event';
    event: BridgeEventName;
    data?: JsonValue;
}

export interface ExecuteInMainRequestEnvelope {
    action: 'executeInMain';
    requestId: string;
    script: string;
    preferPageContextOnNull?: boolean;
    forcePageContextExecution?: boolean;
}

export interface MainWorldResultEnvelope {
    action: 'mainWorldResult';
    requestId: string;
    status: MainWorldExecutionStatus;
    value?: string;
    error?: string;
}

export interface ContentReadyEnvelope {
    action: 'ready';
    context: TabContextEnvelope;
}

export type BackgroundPortEnvelope =
    | BackgroundToContentCommandEnvelope
    | MainWorldResultEnvelope;

export type ContentPortEnvelope =
    | ContentResponseEnvelope
    | ContentEventEnvelope
    | ExecuteInMainRequestEnvelope
    | ContentReadyEnvelope;