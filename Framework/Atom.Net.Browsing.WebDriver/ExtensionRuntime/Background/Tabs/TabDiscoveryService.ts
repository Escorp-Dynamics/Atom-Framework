export interface TabDiscoveryCandidate {
    tabId: string;
    windowId?: string;
    url?: string;
    ready: boolean;
}

export interface ITabDiscoveryService {
    discover(): Promise<readonly TabDiscoveryCandidate[]>;

    ensureConnected(tabId: string): Promise<void>;
}