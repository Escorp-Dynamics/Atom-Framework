import type { HeaderLike, MutableHeaderLike } from '../Cookies/VirtualCookies';

export interface WebRequestDetails {
    readonly tabId?: number;
    readonly url?: string;
    readonly method?: string;
    readonly requestId?: string;
    readonly type?: string;
    readonly statusCode?: number;
    readonly statusLine?: string;
    readonly timeStamp?: number;
    readonly requestHeaders?: readonly HeaderLike[];
    readonly responseHeaders?: readonly HeaderLike[];
}

export interface WebRequestHeaderMutation {
    readonly requestHeaders?: MutableHeaderLike[];
    readonly responseHeaders?: MutableHeaderLike[];
}

export interface WebRequestListenerResult extends WebRequestHeaderMutation {
    readonly cancel?: boolean;
    readonly redirectUrl?: string;
}

export function addWebRequestListener(
    event: any,
    listener: (details: WebRequestDetails) => WebRequestListenerResult | undefined,
    extraInfoSpec: string[],
): void {
    const filter = { urls: ['<all_urls>'] };

    try {
        event.addListener(listener, filter, extraInfoSpec);
        return;
    } catch {
        // fallback below
    }

    try {
        event.addListener(listener, filter, extraInfoSpec.filter((item) => item !== 'extraHeaders'));
        return;
    } catch {
        // fallback below
    }

    event.addListener(listener, filter);
}