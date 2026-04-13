export interface BrowserHost {
    readonly runtime?: any;
    readonly proxy?: any;
    readonly storage?: {
        readonly managed?: any;
        readonly local?: any;
    };
    readonly tabs?: any;
    readonly windows?: any;
    readonly cookies?: any;
    readonly scripting?: any;
    readonly webNavigation?: any;
    readonly webRequest?: any;
}

export interface BrowserTab {
    readonly id?: number;
    readonly windowId?: number;
    readonly url?: string;
    readonly title?: string;
    readonly status?: string;
    readonly pendingUrl?: string;
}

export interface BrowserWindowInfo {
    readonly id?: number;
    readonly left?: number;
    readonly top?: number;
    readonly width?: number;
    readonly height?: number;
    readonly state?: string;
    readonly tabs?: readonly BrowserTab[];
}

export interface BrowserCookie {
    readonly name: string;
    readonly value?: string;
    readonly domain?: string;
    readonly path?: string;
    readonly secure?: boolean;
    readonly httpOnly?: boolean;
    readonly expirationDate?: number;
}

type BrowserGlobalState = typeof globalThis & {
    browser?: BrowserHost;
    chrome?: BrowserHost;
};

export function getBrowserHost(): BrowserHost {
    const runtimeState = globalThis as BrowserGlobalState;
    return runtimeState.browser ?? runtimeState.chrome ?? {};
}

export function getRuntimeApi(runtimeHost: BrowserHost): any {
    const runtime = runtimeHost.runtime;
    if (runtime === undefined) {
        throw new Error('Средства выполнения браузера недоступны');
    }

    return runtime;
}

export async function findBootstrapTab(runtime: any, browserHost: BrowserHost): Promise<BrowserTab | null> {
    if (browserHost.tabs === undefined) {
        return null;
    }

    const activeTabs = await queryTabs(runtime, browserHost.tabs, {
        active: true,
        lastFocusedWindow: true,
    });
    const activeTab = activeTabs.find((tab) => Number.isInteger(tab.id));
    if (activeTab !== undefined) {
        return activeTab;
    }

    if (browserHost.windows === undefined) {
        return null;
    }

    const windows = await getAllWindows(runtime, browserHost.windows, {
        populate: true,
        windowTypes: ['normal'],
    });

    for (const windowInfo of windows) {
        const candidate = windowInfo.tabs?.find((tab) => Number.isInteger(tab.id));
        if (candidate !== undefined) {
            return candidate;
        }
    }

    return null;
}

export async function queryTabs(runtime: any, tabsApi: any, queryInfo: unknown): Promise<BrowserTab[]> {
    if (tabsApi === undefined) {
        throw new Error('API вкладок недоступен');
    }

    return invokeBrowserCall<BrowserTab[]>(runtime, tabsApi.query, tabsApi, queryInfo);
}

export async function createTab(runtime: any, tabsApi: any, createProperties: unknown): Promise<BrowserTab> {
    if (tabsApi === undefined) {
        throw new Error('API вкладок недоступен');
    }

    return invokeBrowserCall<BrowserTab>(runtime, tabsApi.create, tabsApi, createProperties);
}

export async function getTab(runtime: any, tabsApi: any, tabId: number): Promise<BrowserTab> {
    if (tabsApi === undefined) {
        throw new Error('API вкладок недоступен');
    }

    return invokeBrowserCall<BrowserTab>(runtime, tabsApi.get, tabsApi, tabId);
}

export async function captureTabPngDataUrl(runtime: any, tabsApi: any, windowsApi: any, tabId: number, windowId?: number): Promise<string> {
    if (tabsApi === undefined) {
        throw new Error('API вкладок недоступен');
    }

    const options = { format: 'png' };
    let captureTabError: string | null = null;

    const waitForBrowserSettleAsync = async (): Promise<void> => {
        await new Promise((resolve) => setTimeout(resolve, 75));
    };

    const captureVisibleTabAsync = async (): Promise<string> => {
        if (typeof tabsApi.captureVisibleTab !== 'function') {
            throw new Error('API скриншотов вкладки недоступен');
        }

        const activeTab = windowId === undefined
            ? null
            : (await queryTabs(runtime, tabsApi, { windowId, active: true })).find((candidate) => Number.isInteger(candidate.id)) ?? null;
        const restoreTabId = activeTab?.id;

        if (windowId !== undefined && windowsApi !== undefined) {
            await updateWindow(runtime, windowsApi, windowId, { focused: true }).catch(() => undefined);
        }

        if (restoreTabId !== tabId) {
            await updateTab(runtime, tabsApi, tabId, { active: true });
        }

        await waitForBrowserSettleAsync();

        try {
            if (windowId === undefined) {
                return await invokeBrowserCall<string>(runtime, tabsApi.captureVisibleTab, tabsApi, options);
            }

            return await invokeBrowserCall<string>(runtime, tabsApi.captureVisibleTab, tabsApi, windowId, options);
        } finally {
            if (restoreTabId !== undefined && restoreTabId !== tabId) {
                await updateTab(runtime, tabsApi, restoreTabId, { active: true }).catch(() => undefined);
            }
        }
    };

    if (typeof tabsApi.captureTab === 'function') {
        try {
            return await invokeBrowserCall<string>(runtime, tabsApi.captureTab, tabsApi, tabId, options);
        } catch (error) {
            captureTabError = describeBrowserError(error);
            if (typeof tabsApi.captureVisibleTab !== 'function') {
                throw new Error(`Не удалось снять скриншот вкладки ${tabId.toString()}: captureTab=${captureTabError}`);
            }
        }
    }

    try {
        return await captureVisibleTabAsync();
    } catch (error) {
        const captureVisibleTabError = describeBrowserError(error);
        const errorDetails = captureTabError === null
            ? `captureVisibleTab=${captureVisibleTabError}`
            : `captureTab=${captureTabError}; captureVisibleTab=${captureVisibleTabError}`;
        throw new Error(`Не удалось снять скриншот вкладки ${tabId.toString()}: ${errorDetails}`);
    }
}

export async function updateTab(runtime: any, tabsApi: any, tabId: number, updateProperties: unknown): Promise<BrowserTab> {
    if (tabsApi === undefined) {
        throw new Error('API вкладок недоступен');
    }

    return invokeBrowserCall<BrowserTab>(runtime, tabsApi.update, tabsApi, tabId, updateProperties);
}

export async function reloadTab(runtime: any, tabsApi: any, tabId: number): Promise<void> {
    if (tabsApi === undefined) {
        throw new Error('API вкладок недоступен');
    }

    await invokeBrowserCall(runtime, tabsApi.reload, tabsApi, tabId);
}

export async function removeTab(runtime: any, tabsApi: any, tabId: number): Promise<void> {
    if (tabsApi === undefined) {
        throw new Error('API вкладок недоступен');
    }

    await invokeBrowserCall(runtime, tabsApi.remove, tabsApi, tabId);
}

export async function getAllWindows(runtime: any, windowsApi: any, getInfo: unknown): Promise<BrowserWindowInfo[]> {
    return invokeBrowserCall<BrowserWindowInfo[]>(runtime, windowsApi.getAll, windowsApi, getInfo);
}

export async function createWindow(runtime: any, windowsApi: any, createData: unknown): Promise<BrowserWindowInfo> {
    if (windowsApi === undefined) {
        throw new Error('API окон недоступен');
    }

    return invokeBrowserCall<BrowserWindowInfo>(runtime, windowsApi.create, windowsApi, createData);
}

export async function getWindow(runtime: any, windowsApi: any, windowId: number): Promise<BrowserWindowInfo> {
    if (windowsApi === undefined) {
        throw new Error('API окон недоступен');
    }

    if (typeof windowsApi.get === 'function') {
        return invokeBrowserCall<BrowserWindowInfo>(runtime, windowsApi.get, windowsApi, windowId, { populate: true });
    }

    const windows = await getAllWindows(runtime, windowsApi, { populate: true, windowTypes: ['normal'] });
    const windowInfo = windows.find((candidate) => candidate.id === windowId);
    if (windowInfo === undefined) {
        throw new Error('Окно не найдено');
    }

    return windowInfo;
}

export async function updateWindow(runtime: any, windowsApi: any, windowId: number, updateInfo: unknown): Promise<BrowserWindowInfo> {
    if (windowsApi === undefined) {
        throw new Error('API окон недоступен');
    }

    return invokeBrowserCall<BrowserWindowInfo>(runtime, windowsApi.update, windowsApi, windowId, updateInfo);
}

export async function removeWindow(runtime: any, windowsApi: any, windowId: number): Promise<void> {
    if (windowsApi === undefined) {
        throw new Error('API окон недоступен');
    }

    await invokeBrowserCall(runtime, windowsApi.remove, windowsApi, windowId);
}

export async function getCookies(runtime: any, cookiesApi: any, details: unknown): Promise<BrowserCookie[]> {
    if (cookiesApi === undefined) {
        throw new Error('API cookies недоступен');
    }

    return invokeBrowserCall<BrowserCookie[]>(runtime, cookiesApi.getAll, cookiesApi, details);
}

export async function setCookie(runtime: any, cookiesApi: any, details: unknown): Promise<unknown> {
    if (cookiesApi === undefined) {
        throw new Error('API cookies недоступен');
    }

    return invokeBrowserCall(runtime, cookiesApi.set, cookiesApi, details);
}

export async function removeCookie(runtime: any, cookiesApi: any, details: unknown): Promise<void> {
    if (cookiesApi === undefined) {
        throw new Error('API cookies недоступен');
    }

    await invokeBrowserCall(runtime, cookiesApi.remove, cookiesApi, details);
}

export function requireTabUrl(tab: BrowserTab): string {
    if (typeof tab.url !== 'string' || tab.url.trim().length === 0) {
        throw new Error('Для текущей вкладки недоступен адрес, необходимый для cookies');
    }

    return tab.url;
}

export function resolveWindowTab(windowInfo: BrowserWindowInfo): BrowserTab | null {
    const candidate = windowInfo.tabs?.find((tab) => Number.isInteger(tab.id));
    return candidate ?? null;
}

export async function findFirstWindowTab(runtime: any, tabsApi: any, windowId: number | undefined): Promise<BrowserTab | null> {
    if (windowId === undefined) {
        return null;
    }

    const tabs = await queryTabs(runtime, tabsApi, { windowId });
    return tabs.find((tab) => Number.isInteger(tab.id)) ?? null;
}

export function getWebRequestTabId(tabId: number | undefined): string | null {
    return typeof tabId === 'number' && Number.isInteger(tabId) && tabId > 0 ? tabId.toString() : null;
}

export function buildCookieUrl(cookie: BrowserCookie, fallbackUrl: string): string {
    if (cookie.domain === undefined || cookie.domain.trim().length === 0) {
        return fallbackUrl;
    }

    const protocol = cookie.secure === true ? 'https://' : getUrlProtocol(fallbackUrl);
    const normalizedDomain = cookie.domain.replace(/^\./, '');
    const path = cookie.path && cookie.path.length > 0 ? cookie.path : '/';
    return `${protocol}${normalizedDomain}${path}`;
}

export function getUrlProtocol(url: string): string {
    if (url.startsWith('https://')) {
        return 'https://';
    }
    if (url.startsWith('http://')) {
        return 'http://';
    }
    return 'http://';
}

export function invokeBrowserCall<TResult>(runtime: any, method: (...args: any[]) => unknown, context: any, ...args: any[]): Promise<TResult> {
    return new Promise<TResult>((resolve, reject) => {
        const callback = (result: TResult) => {
            const message = runtime.lastError?.message;
            if (typeof message === 'string' && message.length > 0) {
                reject(new Error(message));
                return;
            }

            resolve(result);
        };

        try {
            const returnValue = method.call(context, ...args, callback);
            if (returnValue !== undefined && returnValue !== null && typeof (returnValue as PromiseLike<TResult>).then === 'function') {
                Promise.resolve(returnValue as TResult | PromiseLike<TResult>).then(resolve, reject);
            }
        } catch (error) {
            reject(error);
        }
    });
}

function describeBrowserError(error: unknown): string {
    if (error instanceof Error && error.message.trim().length > 0) {
        return error.message;
    }

    if (typeof error === 'string' && error.trim().length > 0) {
        return error;
    }

    return 'без подробностей';
}