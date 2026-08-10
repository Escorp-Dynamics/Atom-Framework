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

/**
 * Сообщает о вынужденном понижении возможностей слушателя webRequest.
 * `lostBlocking` означает, что слушатель зарегистрирован только как наблюдатель:
 * перехват (правка заголовков, отмена, редирект) в этом режиме не работает.
 */
export type WebRequestListenerDegradation = {
    readonly requestedExtraInfoSpec: readonly string[];
    readonly appliedExtraInfoSpec: readonly string[];
    readonly lostBlocking: boolean;
    readonly error: string;
};

export function addWebRequestListener(
    event: any,
    listener: (details: WebRequestDetails) => WebRequestListenerResult | undefined,
    extraInfoSpec: string[],
    onDegraded?: (degradation: WebRequestListenerDegradation) => void,
): void {
    const filter = { urls: ['<all_urls>'] };
    // Ниже — попытки понизить требования, но каждая из них меняет наблюдаемое поведение,
    // поэтому о ней обязательно сообщается наружу: молчаливое падение до наблюдателя
    // выглядело как «перехват включён, но ничего не перехватывает».
    let lastError: unknown;

    try {
        event.addListener(listener, filter, extraInfoSpec);
        return;
    } catch (error) {
        lastError = error;
    }

    const withoutExtraHeaders = extraInfoSpec.filter((item) => item !== 'extraHeaders');
    if (withoutExtraHeaders.length !== extraInfoSpec.length) {
        try {
            event.addListener(listener, filter, withoutExtraHeaders);
            onDegraded?.({
                requestedExtraInfoSpec: extraInfoSpec,
                appliedExtraInfoSpec: withoutExtraHeaders,
                lostBlocking: false,
                error: toDegradationErrorText(lastError),
            });
            return;
        } catch (error) {
            lastError = error;
        }
    }

    event.addListener(listener, filter);
    onDegraded?.({
        requestedExtraInfoSpec: extraInfoSpec,
        appliedExtraInfoSpec: [],
        lostBlocking: extraInfoSpec.includes('blocking'),
        error: toDegradationErrorText(lastError),
    });
}

function toDegradationErrorText(error: unknown): string {
    if (error instanceof Error && error.message.trim().length > 0) {
        return error.message;
    }

    const text = String(error).trim();
    return text.length > 0 ? text : 'причина не сообщена браузером';
}