export interface HeaderLike {
    readonly name?: string;
    readonly value?: string;
}

export interface MutableHeaderLike {
    name?: string;
    value?: string;
}

export interface VirtualCookie {
    readonly name: string;
    readonly value: string;
    readonly domain: string;
    readonly path: string;
    readonly secure: boolean;
    readonly httpOnly: boolean;
    readonly hostOnly: boolean;
    readonly expires?: number;
}

export interface DirectVirtualCookieAttributes {
    readonly secure?: boolean;
    readonly httpOnly?: boolean;
    readonly expires?: number;
}

export function toJsonVirtualCookie(cookie: VirtualCookie): Record<string, unknown> {
    const payload: Record<string, unknown> = {
        name: cookie.name,
        value: cookie.value,
        domain: cookie.domain,
        path: cookie.path,
        secure: cookie.secure,
        httpOnly: cookie.httpOnly,
    };

    if (typeof cookie.expires === 'number' && Number.isFinite(cookie.expires)) {
        payload.expires = Math.trunc(cookie.expires);
    }

    return payload;
}

export function cloneHeaders(headers: readonly HeaderLike[] | undefined): MutableHeaderLike[] {
    return Array.isArray(headers) ? headers.map((header) => ({ ...header })) : [];
}

export function isHeaderNamed(header: HeaderLike, expectedName: string): boolean {
    return typeof header.name === 'string'
        && header.name.localeCompare(expectedName, undefined, { sensitivity: 'accent' }) === 0;
}

export function setHeaderValue(headers: MutableHeaderLike[], name: string, value: string | undefined): void {
    const index = headers.findIndex((header) => isHeaderNamed(header, name));
    if (value === undefined || value.length === 0) {
        if (index >= 0) {
            headers.splice(index, 1);
        }
        return;
    }

    if (index >= 0) {
        headers[index] = { ...headers[index], name, value };
        return;
    }

    headers.push({ name, value });
}

export function moveVirtualCookieStore(stores: Map<string, VirtualCookie[]>, sourceContextId: string, targetContextId: string): void {
    if (sourceContextId === targetContextId) {
        return;
    }

    const source = stores.get(sourceContextId);
    if (source === undefined) {
        return;
    }

    const target = stores.get(targetContextId);
    if (target === undefined) {
        stores.set(targetContextId, [...source]);
    } else {
        for (const cookie of source) {
            ensureVirtualCookieStoreEntry(target, cookie);
        }
    }

    stores.delete(sourceContextId);
}

export function ensureVirtualCookieStore(stores: Map<string, VirtualCookie[]>, contextId: string): VirtualCookie[] {
    let store = stores.get(contextId);
    if (store === undefined) {
        store = [];
        stores.set(contextId, store);
    }

    pruneExpiredVirtualCookies(store);
    return store;
}

export function upsertVirtualCookie(store: VirtualCookie[], cookie: VirtualCookie): void {
    pruneExpiredVirtualCookies(store);
    ensureVirtualCookieStoreEntry(store, cookie);
}

export function pruneExpiredVirtualCookies(store: VirtualCookie[]): void {
    for (let index = store.length - 1; index >= 0; index -= 1) {
        const cookie = store[index];
        if (cookie !== undefined && isExpiredVirtualCookie(cookie)) {
            store.splice(index, 1);
        }
    }
}

export function getVisibleVirtualCookies(store: VirtualCookie[], url: string): VirtualCookie[] {
    pruneExpiredVirtualCookies(store);
    return store.filter((cookie) => matchesVirtualCookie(cookie, url));
}

export function deleteVisibleVirtualCookies(store: VirtualCookie[], url: string): void {
    for (let index = store.length - 1; index >= 0; index -= 1) {
        const cookie = store[index];
        if (cookie !== undefined && matchesVirtualCookie(cookie, url)) {
            store.splice(index, 1);
        }
    }
}

export function buildVirtualCookieHeader(cookies: VirtualCookie[]): string | undefined {
    if (cookies.length === 0) {
        return undefined;
    }

    return cookies.map((cookie) => `${cookie.name}=${cookie.value}`).join('; ');
}

export function createDirectVirtualCookie(
    url: string,
    name: string,
    value: string,
    domain?: string,
    path?: string,
    attributes?: DirectVirtualCookieAttributes,
): VirtualCookie {
    const parsedUrl = new URL(url);
    const normalizedDomain = normalizeCookieDomain(domain ?? parsedUrl.hostname);
    const cookie: VirtualCookie = {
        name,
        value,
        domain: normalizedDomain,
        path: normalizeCookiePath(path ?? '/'),
        secure: attributes?.secure === true,
        httpOnly: attributes?.httpOnly === true,
        hostOnly: domain === undefined || domain.trim().length === 0,
    };

    if (typeof attributes?.expires === 'number' && Number.isFinite(attributes.expires)) {
        (cookie as { expires?: number }).expires = Math.trunc(attributes.expires);
    }

    return cookie;
}

export function parseSetCookieHeader(value: string, url: string): VirtualCookie | null {
    let parsedUrl: URL;
    try {
        parsedUrl = new URL(url);
    } catch {
        return null;
    }

    const parts = value.split(';').map((part) => part.trim()).filter((part) => part.length > 0);
    if (parts.length === 0) {
        return null;
    }

    const firstPart = parts[0];
    if (firstPart === undefined) {
        return null;
    }

    const nameValueSeparator = firstPart.indexOf('=');
    if (nameValueSeparator <= 0) {
        return null;
    }

    const cookie: VirtualCookie = {
        name: firstPart.slice(0, nameValueSeparator).trim(),
        value: firstPart.slice(nameValueSeparator + 1),
        domain: normalizeCookieDomain(parsedUrl.hostname),
        path: normalizeCookiePath(defaultCookiePath(parsedUrl.pathname)),
        secure: false,
        httpOnly: false,
        hostOnly: true,
    };

    for (const attribute of parts.slice(1)) {
        const separator = attribute.indexOf('=');
        const key = (separator >= 0 ? attribute.slice(0, separator) : attribute).trim().toLowerCase();
        const rawValue = separator >= 0 ? attribute.slice(separator + 1).trim() : '';

        switch (key) {
            case 'domain':
                if (rawValue.length > 0) {
                    (cookie as { domain: string }).domain = normalizeCookieDomain(rawValue);
                    (cookie as { hostOnly: boolean }).hostOnly = false;
                }
                break;
            case 'path':
                if (rawValue.length > 0) {
                    (cookie as { path: string }).path = normalizeCookiePath(rawValue);
                }
                break;
            case 'secure':
                (cookie as { secure: boolean }).secure = true;
                break;
            case 'httponly':
                (cookie as { httpOnly: boolean }).httpOnly = true;
                break;
            case 'max-age': {
                const maxAge = Number(rawValue);
                if (Number.isFinite(maxAge)) {
                    (cookie as { expires?: number }).expires = getUnixTimeSeconds() + Math.trunc(maxAge);
                }
                break;
            }
            case 'expires': {
                const expiresAt = Date.parse(rawValue);
                if (Number.isFinite(expiresAt)) {
                    (cookie as { expires?: number }).expires = Math.trunc(expiresAt / 1e3);
                }
                break;
            }
            default:
                break;
        }
    }

    return cookie;
}

function ensureVirtualCookieStoreEntry(store: VirtualCookie[], cookie: VirtualCookie): void {
    const existingIndex = store.findIndex((candidate) => areSameVirtualCookie(candidate, cookie));
    if (existingIndex >= 0) {
        store.splice(existingIndex, 1);
    }

    if (!isExpiredVirtualCookie(cookie)) {
        store.push(cookie);
    }
}

function isExpiredVirtualCookie(cookie: VirtualCookie): boolean {
    return typeof cookie.expires === 'number' && Number.isFinite(cookie.expires)
        ? cookie.expires <= getUnixTimeSeconds()
        : false;
}

function areSameVirtualCookie(left: VirtualCookie, right: VirtualCookie): boolean {
    return left.name === right.name
        && left.domain === right.domain
        && left.path === right.path
        && left.hostOnly === right.hostOnly;
}

function matchesVirtualCookie(cookie: VirtualCookie, url: string): boolean {
    let parsedUrl: URL;
    try {
        parsedUrl = new URL(url);
    } catch {
        return false;
    }

    if (cookie.secure && parsedUrl.protocol !== 'https:') {
        return false;
    }

    const host = parsedUrl.hostname.toLowerCase();
    const domain = cookie.domain.toLowerCase();
    if (cookie.hostOnly) {
        if (host !== domain) {
            return false;
        }
    } else if (host !== domain && !host.endsWith(`.${domain}`)) {
        return false;
    }

    const requestPath = parsedUrl.pathname.length > 0 ? parsedUrl.pathname : '/';
    if (cookie.path === '/') {
        return true;
    }

    return requestPath === cookie.path
        || requestPath.startsWith(cookie.path.endsWith('/') ? cookie.path : `${cookie.path}/`);
}

function normalizeCookieDomain(domain: string): string {
    return domain.trim().replace(/^\./, '').toLowerCase();
}

function normalizeCookiePath(path: string): string {
    if (path.trim().length === 0 || !path.startsWith('/')) {
        return '/';
    }

    return path;
}

function defaultCookiePath(pathname: string): string {
    if (pathname.length === 0 || pathname === '/') {
        return '/';
    }

    const lastSlashIndex = pathname.lastIndexOf('/');
    return lastSlashIndex <= 0 ? '/' : pathname.slice(0, lastSlashIndex);
}

function getUnixTimeSeconds(): number {
    return Math.trunc(Date.now() / 1e3);
}