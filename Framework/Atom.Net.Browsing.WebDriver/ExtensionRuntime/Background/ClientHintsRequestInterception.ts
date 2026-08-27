import type {
    TabContextClientHintBrandEnvelope,
    TabContextEnvelope,
} from '../Shared/Protocol/TabContextEnvelope';
import { getWebRequestTabId } from './Browser/BrowserApi';
import { cloneHeaders, setHeaderValue, type HeaderLike } from './Cookies/VirtualCookies';
import type { WebRequestDetails, WebRequestHeaderMutation } from './WebRequest/WebRequestListenerPolicy';

export function handleClientHintsRequestInterception(
    details: WebRequestDetails,
    getTabContext: (tabId: string) => TabContextEnvelope | undefined,
    baseHeaders?: readonly HeaderLike[],
): WebRequestHeaderMutation | undefined {
    const tabId = getWebRequestTabId(details.tabId);
    if (tabId === null) {
        return undefined;
    }

    const context = getTabContext(tabId);
    const clientHints = context?.clientHints;

    // User-Agent переписывается ЗДЕСЬ ЖЕ, из того же контекста вкладки. Иначе получается разрыв
    // отпечатка: navigator.userAgent и Sec-CH-UA* подменены под заявленную платформу, а сам заголовок
    // User-Agent продолжает нести настоящий браузер и ОС. Такое противоречие между заголовком и JS
    // однозначно выдаёт подделку — на реальном таргете это давало 0 решений из 54 при явных
    // error-callback от Cloudflare, тогда как без подмены вовсе — 151 из 151.
    // Правка сознательно живёт в рантайме и per-tab: подмена должна применяться из кода в любой
    // момент и для любого браузера, а не флагом запуска (--user-agent есть только у Chromium,
    // действует на весь процесс и не меняется на лету).
    const userAgent = normalizeHeaderValue(context?.userAgent);

    if (clientHints === undefined && userAgent === undefined) {
        return undefined;
    }

    const secChUa = formatSecChUa(clientHints?.brands);
    const secChUaFullVersionList = formatSecChUa(clientHints?.fullVersionList ?? clientHints?.brands);
    const secChUaPlatform = formatQuotedClientHintValue(clientHints?.platform);
    const secChUaPlatformVersion = formatQuotedClientHintValue(clientHints?.platformVersion);
    const secChUaMobile = clientHints?.mobile === undefined
        ? undefined
        : clientHints.mobile
            ? '?1'
            : '?0';
    const secChUaArch = formatQuotedClientHintValue(clientHints?.architecture);
    const secChUaModel = formatQuotedClientHintValue(clientHints?.model);
    const secChUaBitness = formatQuotedClientHintValue(clientHints?.bitness);

    if (userAgent === undefined
        && secChUa === undefined
        && secChUaFullVersionList === undefined
        && secChUaPlatform === undefined
        && secChUaPlatformVersion === undefined
        && secChUaMobile === undefined
        && secChUaArch === undefined
        && secChUaModel === undefined
        && secChUaBitness === undefined) {
        return undefined;
    }

    const requestHeaders = cloneHeaders(baseHeaders ?? details.requestHeaders);
    setHeaderValue(requestHeaders, 'User-Agent', userAgent);
    setHeaderValue(requestHeaders, 'Sec-CH-UA', secChUa);
    setHeaderValue(requestHeaders, 'Sec-CH-UA-Full-Version-List', secChUaFullVersionList);
    setHeaderValue(requestHeaders, 'Sec-CH-UA-Platform', secChUaPlatform);
    setHeaderValue(requestHeaders, 'Sec-CH-UA-Platform-Version', secChUaPlatformVersion);
    setHeaderValue(requestHeaders, 'Sec-CH-UA-Mobile', secChUaMobile);
    setHeaderValue(requestHeaders, 'Sec-CH-UA-Arch', secChUaArch);
    setHeaderValue(requestHeaders, 'Sec-CH-UA-Model', secChUaModel);
    setHeaderValue(requestHeaders, 'Sec-CH-UA-Bitness', secChUaBitness);
    return { requestHeaders };
}

/**
 * Значение заголовка из контекста вкладки: пустое/непереданное значение подменять нельзя —
 * иначе вместо настоящего заголовка ушёл бы пустой.
 */
function normalizeHeaderValue(value: string | undefined): string | undefined {
    return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

function formatSecChUa(brands: TabContextClientHintBrandEnvelope[] | undefined): string | undefined {
    if (!Array.isArray(brands) || brands.length === 0) {
        return undefined;
    }

    const value = brands
        .filter((brand) => typeof brand.brand === 'string'
            && brand.brand.trim().length > 0
            && typeof brand.version === 'string'
            && brand.version.trim().length > 0)
        .map((brand) => `"${escapeStructuredHeaderString(brand.brand)}";v="${escapeStructuredHeaderString(brand.version)}"`)
        .join(', ');

    return value.length === 0 ? undefined : value;
}

function formatQuotedClientHintValue(value: string | undefined): string | undefined {
    if (typeof value !== 'string' || value.trim().length === 0) {
        return undefined;
    }

    return `"${escapeStructuredHeaderString(value)}"`;
}

function escapeStructuredHeaderString(value: string): string {
    return value.replaceAll('\\', '\\\\').replaceAll('"', '\\"');
}