import type { ITabRuntimeChannel } from './ContentRuntimeChannel';

export interface IContentDispatchLoop {
    start(channel: ITabRuntimeChannel): Promise<void>;

    stop(): Promise<void>;
}