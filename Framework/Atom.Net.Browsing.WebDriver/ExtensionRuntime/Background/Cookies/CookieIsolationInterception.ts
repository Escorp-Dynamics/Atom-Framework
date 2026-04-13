import type { TabContextEnvelope } from '../../Shared/Protocol';
import {
    buildVirtualCookieHeader,
    cloneHeaders,
    getVisibleVirtualCookies,
    getWebRequestTabId,
    isHeaderNamed,
    parseSetCookieHeader,
    setHeaderValue,
    upsertVirtualCookie,
    type MutableHeaderLike,
    type VirtualCookie,
    type WebRequestDetails,
    type WebRequestHeaderMutation,
} from '../index';

export function handleCookieRequestInterception(
    details: WebRequestDetails,
    getTabContext: (tabId: string) => TabContextEnvelope | undefined,
    ensureVirtualCookieStore: (contextId: string) => VirtualCookie[],
): WebRequestHeaderMutation | undefined {
    const tabId = getWebRequestTabId(details.tabId);
    if (tabId === null || typeof details.url !== 'string' || details.url.trim().length === 0) {
        return undefined;
    }

    const context = getTabContext(tabId);
    if (context === undefined) {
        return undefined;
    }

    const cookies = getVisibleVirtualCookies(ensureVirtualCookieStore(context.contextId), details.url);
    const requestHeaders = cloneHeaders(details.requestHeaders);
    setHeaderValue(requestHeaders, 'Cookie', buildVirtualCookieHeader(cookies));
    return { requestHeaders };
}

export function handleCookieResponseInterception(
    details: WebRequestDetails,
    getTabContext: (tabId: string) => TabContextEnvelope | undefined,
    ensureVirtualCookieStore: (contextId: string) => VirtualCookie[],
): WebRequestHeaderMutation | undefined {
    const tabId = getWebRequestTabId(details.tabId);
    if (tabId === null || typeof details.url !== 'string' || details.url.trim().length === 0) {
        return undefined;
    }

    const context = getTabContext(tabId);
    if (context === undefined) {
        return undefined;
    }

    const responseHeaders = cloneHeaders(details.responseHeaders);
    const remainingHeaders: MutableHeaderLike[] = [];
    let changed = false;
    const store = ensureVirtualCookieStore(context.contextId);

    for (const header of responseHeaders) {
        if (!isHeaderNamed(header, 'Set-Cookie')) {
            remainingHeaders.push(header);
            continue;
        }

        changed = true;
        if (typeof header.value !== 'string' || header.value.trim().length === 0) {
            continue;
        }

        const cookie = parseSetCookieHeader(header.value, details.url);
        if (cookie !== null) {
            upsertVirtualCookie(store, cookie);
        }
    }

    return changed ? { responseHeaders: remainingHeaders } : undefined;
}