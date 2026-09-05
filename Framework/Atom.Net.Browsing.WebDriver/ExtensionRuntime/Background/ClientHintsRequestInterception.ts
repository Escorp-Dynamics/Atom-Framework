import type {
    TabContextClientHintBrandEnvelope,
    TabContextEnvelope,
} from '../Shared/Protocol/TabContextEnvelope';
import { getWebRequestTabId } from './Browser/BrowserApi';
import { cloneHeaders, setHeaderValue, type HeaderLike } from './Cookies/VirtualCookies';
import type { WebRequestDetails, WebRequestHeaderMutation } from './WebRequest/WebRequestListenerPolicy';

/**
 * Работает ли расширение в браузере на движке Gecko.
 *
 * Определяется по API расширений, которого нет в Chromium: <c>browser.runtime.getBrowserInfo</c>
 * реализует только Firefox. По строке агента судить нельзя — драйвер задаёт Firefox
 * <c>general.useragent.override</c>, и собственная страница расширения видит ту же подменённую
 * строку, что и сайт.
 */
let geckoRuntime: boolean | undefined;

function runsOnGecko(): boolean {
    if (geckoRuntime === undefined) {
        try {
            const namespaces = globalThis as { browser?: { runtime?: { getBrowserInfo?: unknown } } };
            geckoRuntime = typeof namespaces.browser?.runtime?.getBrowserInfo === 'function';
        } catch {
            geckoRuntime = false;
        }
    }

    return geckoRuntime;
}

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

    // ★ Заявленный WebKit НЕ ШЛЁТ клиентских подсказок вовсе.
    //
    // Client Hints — расширение Chromium; Safari его не реализует. Замер на живом Cloudflare
    // показывал ровно это противоречие: строка агента объявляла iPhone/Safari, а запрос нёс
    // 'Sec-CH-UA-Mobile: ?1', 'Sec-CH-UA-Platform: "iOS"' и 'Sec-CH-UA-Model'. Такое расхождение
    // видно ДО исполнения любого JavaScript, прямо в заголовках. Поэтому для профиля на WebKit
    // подсказки не подменяются, а УДАЛЯЮТСЯ — как их и не бывает у настоящего Safari.
    const declaresWebKit = userAgent !== undefined
        && (userAgent.indexOf('iPhone') >= 0
            || userAgent.indexOf('iPad') >= 0
            || userAgent.indexOf('iPod') >= 0
            || (userAgent.indexOf('Safari/') >= 0
                && userAgent.indexOf('Version/') >= 0
                && userAgent.indexOf('Chrome/') < 0
                && userAgent.indexOf('Chromium/') < 0));

    // ★ То же самое верно для Gecko: клиентских подсказок у Firefox нет ВОВСЕ.
    //
    // Замер на живом Cloudflare: браузер Firefox с профилем устройства отдавал
    // 'Sec-CH-UA: "Chromium";v="131"' при собственном фаерфоксовом 'Accept' — и виджет отвечал
    // error-callback 600010 («среда слишком ограничена»), 0 задач из 14. Тот же Firefox БЕЗ
    // профиля устройства решал 3 из 3 за ~5 секунд. Признак читается прямо в заголовках, до
    // любого JavaScript.
    const declaresGecko = userAgent !== undefined
        && userAgent.indexOf('Firefox/') >= 0
        && userAgent.indexOf('Chrome/') < 0
        && userAgent.indexOf('Chromium/') < 0;

    // ★ И наоборот: на НАСТОЯЩЕМ Gecko подсказок не бывает, какую бы строку агента мы ни заявляли.
    // Их вообще некому породить — их добавляем только мы. Профиль «Chrome на Windows», выданный
    // браузеру Firefox, уезжал с полным набором Sec-CH-UA*, которого движок физически отдать не
    // может: замер 0 задач из 3, error-callback 600010 на каждой.
    if (declaresWebKit || declaresGecko || runsOnGecko()) {
        const webKitHeaders = cloneHeaders(baseHeaders ?? details.requestHeaders);
        setHeaderValue(webKitHeaders, 'User-Agent', userAgent);

        for (const hint of [
            'Sec-CH-UA',
            'Sec-CH-UA-Full-Version-List',
            'Sec-CH-UA-Platform',
            'Sec-CH-UA-Platform-Version',
            'Sec-CH-UA-Mobile',
            'Sec-CH-UA-Arch',
            'Sec-CH-UA-Model',
            'Sec-CH-UA-Bitness',
            'Sec-CH-UA-Full-Version',
            'Sec-CH-UA-WoW64',
            'Sec-CH-Prefers-Color-Scheme',
            'Sec-CH-Prefers-Reduced-Motion',
            'Sec-CH-Viewport-Width',
            'Sec-CH-DPR',
            'Device-Memory',
            'Downlink',
            'RTT',
            'ECT',
        ]) {
            setHeaderValue(webKitHeaders, hint, undefined);
        }

        // Кодировки: Chromium предлагает zstd, Safari — нет. Заголовок читается тем же запросом,
        // что и строка агента, и расходится с ней так же явно, как подсказки. Firefox zstd шлёт,
        // поэтому его это не касается.
        const acceptEncoding = readHeaderValue(webKitHeaders, 'Accept-Encoding');
        if (declaresWebKit && acceptEncoding !== undefined && acceptEncoding.indexOf('zstd') >= 0) {
            setHeaderValue(webKitHeaders, 'Accept-Encoding', 'gzip, deflate, br');
        }

        // Список принимаемых типов у Chromium заметно длиннее: он перечисляет свои форматы
        // изображений (avif/apng) и подписанный обмен. Safari таких значений не шлёт, а тип
        // запроса виден из 'Sec-Fetch-Dest' — по нему и подставляется значение того же ресурса.
        const acceptForDestination = declaresWebKit
            ? resolveWebKitAccept(readHeaderValue(webKitHeaders, 'Sec-Fetch-Dest'))
            : undefined;
        if (acceptForDestination !== undefined && readHeaderValue(webKitHeaders, 'Accept') !== undefined) {
            setHeaderValue(webKitHeaders, 'Accept', acceptForDestination);
        }

        return { requestHeaders: webKitHeaders };
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
 * Значение заголовка Accept, как его шлёт Safari для запроса такого типа.
 */
function resolveWebKitAccept(destination: string | undefined): string | undefined {
    switch (destination) {
        case 'document':
        case 'iframe':
        case 'frame':
            return 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8';
        case 'image':
            return 'image/webp,image/avif,video/*;q=0.8,image/png,image/svg+xml,image/*;q=0.8,*/*;q=0.5';
        case 'style':
            return 'text/css,*/*;q=0.1';
        case 'script':
        case 'worker':
        case 'empty':
            return '*/*';
        default:
            return undefined;
    }
}

/**
 * Текущее значение заголовка запроса.
 */
function readHeaderValue(headers: readonly HeaderLike[], name: string): string | undefined {
    const lowered = name.toLowerCase();

    for (const header of headers) {
        if (typeof header?.name === 'string' && header.name.toLowerCase() === lowered) {
            return typeof header.value === 'string' ? header.value : undefined;
        }
    }

    return undefined;
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