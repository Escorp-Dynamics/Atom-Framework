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
    /**
     * Дублировать cookie вкладки в НАСТОЯЩУЮ банку браузера, а не только в виртуальное хранилище.
     *
     * ★ Виртуальное хранилище попадает в сеть единственным путём — блокирующим onBeforeSendHeaders
     * (handleCookieRequestInterception). В Chromium расширение живёт в service worker'е Manifest V3,
     * где блокирующего webRequest нет вовсе (в журнале это «blocking-interception-unavailable»), и
     * поставленная драйвером cookie до запроса НЕ доезжала: JavaScript её видел (document.cookie
     * синхронизируется отдельно), а заголовка Cookie в запросе не было. Для проверки Cloudflare это
     * прямое противоречие — и клиренс доставался сессии БЕЗ __cf_bm, который потом предъявлял
     * владелец задачи (22.09: заголовка Cookie нет ни на одной навигации на lift-api).
     *
     * Firefox блокирующий перехват имеет, там виртуальное хранилище самодостаточно и банку браузера
     * трогать незачем — изоляция вкладок держится именно на нём.
     */
    readonly writesThroughToBrowserJar: boolean;
}

/**
 * Снимает из настоящей банки браузера все cookie, видимые по этому адресу.
 */
async function removeBrowserCookies(context: DirectCookieCommandContext, url: string): Promise<void> {
    const cookies = await getCookies(context.runtime, context.browserHost.cookies, { url });
    await Promise.all(cookies.map((cookie: BrowserCookie) => removeCookie(context.runtime, context.browserHost.cookies, {
        url: buildCookieUrl(cookie, url),
        name: cookie.name,
    })));
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
        const cookie = createDirectVirtualCookie(url, name, value, domain, path, {
            secure,
            httpOnly,
            expires,
        });
        upsertVirtualCookie(store, cookie);

        if (context.writesThroughToBrowserJar) {
            // Адрес строим по самой cookie, а не по вкладке: вкладка в момент подстановки обычно
            // стоит на about:blank или на прошлой странице, и банка браузера такой набор отвергла бы.
            await setCookie(context.runtime, context.browserHost.cookies, {
                url: buildCookieUrl(cookie, url),
                name: cookie.name,
                value: cookie.value,
                domain: cookie.hostOnly ? undefined : cookie.domain,
                path: cookie.path,
                secure: cookie.secure,
                httpOnly: cookie.httpOnly,
                expirationDate: expires,
            });
        }

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

        // Чистка обязана снимать и то, что записано сквозь (см. writesThroughToBrowserJar): иначе
        // цикл личности вкладки менял бы адрес и виртуальное хранилище, а след в банке браузера
        // оставался бы прежним.
        if (context.writesThroughToBrowserJar) {
            await removeBrowserCookies(context, url);
        }

        await context.sendDirectResponse(message);
        return;
    }

    await removeBrowserCookies(context, url);
    await context.sendDirectResponse(message);
}