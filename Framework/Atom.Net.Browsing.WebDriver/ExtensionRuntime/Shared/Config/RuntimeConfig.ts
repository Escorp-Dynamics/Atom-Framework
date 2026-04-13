import type { RuntimeFeatureFlags } from './RuntimeFeatureFlags';

export interface RuntimeConfig {
    host: string;
    port: number;
    proxyPort?: number;
    transportUrl?: string;
    sessionId: string;
    secret: string;
    protocolVersion: number;
    browserFamily: string;
    extensionVersion: string;
    featureFlags: RuntimeFeatureFlags;
}