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

/**
 * Виртуальное хранилище cookie обслуживает ТОЛЬКО верхний документ вкладки.
 *
 * ★ Сетевой и видимый из JavaScript набор cookie обязаны совпадать внутри одного фрейма. Прослойка
 * `document.cookie` (buildCookieIsolationScript) ставится контент-скриптом, объявленным с
 * `all_frames: false`, то есть только в верхнем документе. Сетевая же подмена работала для ВСЕХ
 * запросов вкладки: из ответов подчинённых фреймов вырезались все `Set-Cookie`, и браузер их не
 * получал вовсе. Кросс-доменный фрейм проверки Cloudflare (`challenges.cloudflare.com`) при этом
 * читал настоящий `document.cookie`, не находил собственных cookie, выставленных сервером, и
 * проверка ходила по кругу: «Verifying…» → снова чекбокс, ни одного токена на Firefox. Chrome этого
 * не проявлял — блокирующий webRequest есть только у Firefox, и путь этот Firefox-only.
 *
 * Подчинённые фреймы работают с настоящим хранилищем браузера — согласованно и в сети, и в JS.
 */
function isTopFrameRequest(details: WebRequestDetails): boolean {
    return details.frameId === undefined || details.frameId === 0;
}

export function handleCookieRequestInterception(
    details: WebRequestDetails,
    getTabContext: (tabId: string) => TabContextEnvelope | undefined,
    ensureVirtualCookieStore: (contextId: string) => VirtualCookie[],
): WebRequestHeaderMutation | undefined {
    const tabId = getWebRequestTabId(details.tabId);
    if (tabId === null || typeof details.url !== 'string' || details.url.trim().length === 0) {
        return undefined;
    }

    if (!isTopFrameRequest(details)) {
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

    if (!isTopFrameRequest(details)) {
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