/**
 * Чтение shadow root узла, включая ЗАКРЫТЫЙ.
 *
 * `host.shadowRoot` по спецификации отдаёт `null` для `mode: 'closed'`, и раньше драйвер на этом
 * останавливался: закрытые корни считались непрозрачными. Но у content script есть привилегия,
 * которой нет у страницы: `chrome.dom.openOrClosedShadowRoot(host)` (Chrome 88+) и его аналог
 * `browser.dom.openOrClosedShadowRoot` в Firefox возвращают корень независимо от режима.
 *
 * Обёртка пробует привилегированный путь первой и падает обратно на `host.shadowRoot` там, где
 * API нет (старые сборки, среда тестов без расширения).
 */

interface DomApiLike {
    openOrClosedShadowRoot?(element: Element): ShadowRoot | null;
}

interface ExtensionHostLike {
    dom?: DomApiLike;
}

function resolveDomApi(): DomApiLike | undefined {
    const host = globalThis as typeof globalThis & {
        browser?: ExtensionHostLike;
        chrome?: ExtensionHostLike;
    };

    return host.browser?.dom ?? host.chrome?.dom;
}

/**
 * Возвращает shadow root узла — открытый или закрытый — либо `null`, если корня нет.
 *
 * ★ У движков РАЗНЫЕ привилегированные пути, и оба нужны:
 * - Chromium: `chrome.dom.openOrClosedShadowRoot(element)` (Chrome 88+);
 * - Firefox: `browser.dom` этого метода НЕ имеет (MDN: «No support»), зато content script видит
 *   свойство `element.openOrClosedShadowRoot` — оно и отдаёт закрытый корень.
 * Пока поддерживался только первый путь, на Firefox закрытые корни оставались невидимыми.
 */
export function getShadowRootOfAnyMode(host: Element): ShadowRoot | null {
    const open = host.shadowRoot;
    if (open !== null) {
        return open;
    }

    const api = resolveDomApi();
    if (api?.openOrClosedShadowRoot !== undefined) {
        try {
            const viaApi = api.openOrClosedShadowRoot(host);
            if (viaApi) {
                return viaApi;
            }
        } catch {
            // Вызов бросает на узлах вне документа и на нестандартных хостах — пробуем дальше.
        }
    }

    const geckoHost = host as Element & { openOrClosedShadowRoot?: ShadowRoot | null };
    if (geckoHost.openOrClosedShadowRoot !== undefined) {
        return geckoHost.openOrClosedShadowRoot ?? null;
    }

    return null;
}

/**
 * Режим корня для отчёта драйверу: `'open'`, `'closed'` либо `'false'`, если корня нет.
 */
export function describeShadowRootMode(host: Element): 'open' | 'closed' | 'false' {
    if (host.shadowRoot !== null) {
        return 'open';
    }

    return getShadowRootOfAnyMode(host) !== null ? 'closed' : 'false';
}
