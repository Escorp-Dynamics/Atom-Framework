import type { BridgeMessage, TabContextEnvelope } from '../../Shared/Protocol';
import {
    buildCookieUrl,
    createDirectVirtualCookie,
    deleteVisibleVirtualCookies,
    ensureVirtualCookieStore,
    getCookies,
    getVisibleVirtualCookies,
    removeCookie,
    setCookie,
    toJsonVirtualCookie,
    upsertVirtualCookie,
    type BrowserCookie,
    type BrowserHost,
    type VirtualCookie,
} from '../index';

export interface DirectCookieCommandContext {
    readonly runtime: any;
    readonly browserHost: BrowserHost;
    readonly virtualCookies: Map<string, VirtualCookie[]>;
    readonly sendDirectResponse: (message: BridgeMessage, payload?: unknown) => Promise<void>;
    readonly mapBrowserCookie: (cookie: BrowserCookie) => Record<string, unknown>;
}

export async function handleSetCookieCommand(
    context: DirectCookieCommandContext,
    message: BridgeMessage,
    url: string,
    runtimeContext: TabContextEnvelope | undefined,
    name: string,
    value: string,
    domain?: string,
    path?: string,
    secure?: boolean,
    httpOnly?: boolean,
    expires?: number,
): Promise<void> {
    if (runtimeContext !== undefined) {
        const store = ensureVirtualCookieStore(context.virtualCookies, runtimeContext.contextId);
        upsertVirtualCookie(store, createDirectVirtualCookie(url, name, value, domain, path, {
            secure,
            httpOnly,
            expires,
        }));
        await context.sendDirectResponse(message);
        return;
    }

    await setCookie(context.runtime, context.browserHost.cookies, {
        url,
        name,
        value,
        domain,
        path,
        secure,
        httpOnly,
        expirationDate: expires,
    });
    await context.sendDirectResponse(message);
}

export async function handleGetCookiesCommand(
    context: DirectCookieCommandContext,
    message: BridgeMessage,
    url: string,
    runtimeContext: TabContextEnvelope | undefined,
): Promise<void> {
    if (runtimeContext !== undefined) {
        const cookies = getVisibleVirtualCookies(ensureVirtualCookieStore(context.virtualCookies, runtimeContext.contextId), url);
        await context.sendDirectResponse(message, cookies.map(toJsonVirtualCookie));
        return;
    }

    const cookies = await getCookies(context.runtime, context.browserHost.cookies, { url });
    await context.sendDirectResponse(message, cookies.map(context.mapBrowserCookie));
}

export async function handleDeleteCookiesCommand(
    context: DirectCookieCommandContext,
    message: BridgeMessage,
    url: string,
    runtimeContext: TabContextEnvelope | undefined,
): Promise<void> {
    if (runtimeContext !== undefined) {
        deleteVisibleVirtualCookies(ensureVirtualCookieStore(context.virtualCookies, runtimeContext.contextId), url);
        await context.sendDirectResponse(message);
        return;
    }

    const cookies = await getCookies(context.runtime, context.browserHost.cookies, { url });
    await Promise.all(cookies.map((cookie: BrowserCookie) => removeCookie(context.runtime, context.browserHost.cookies, {
        url: buildCookieUrl(cookie, url),
        name: cookie.name,
    })));
    await context.sendDirectResponse(message);
}