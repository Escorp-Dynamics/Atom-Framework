import { type BrowserHost, invokeBrowserCall } from '../Browser/BrowserApi';
import type { TabContextEnvelope } from '../../Shared/Protocol';

/**
 * Есть ли в контексте хоть одно поле личности, ради которого стоит трогать подчинённые фреймы.
 *
 * Без профиля работа впустую: внедрение в каждый фрейм стоит обращения к браузеру, а на пустом
 * контексте не меняет ничего. Именно этот случай — штатный режим без подмены, и он не должен
 * платить ни за что.
 */
export function hasIdentityOverrides(context: TabContextEnvelope): boolean {
    return context.userAgent !== undefined
        || context.platform !== undefined
        || context.clientHints !== undefined
        || (context as { webGl?: unknown }).webGl !== undefined
        || (context as { webGlParameters?: unknown }).webGlParameters !== undefined
        || context.languages !== undefined
        || context.hardwareConcurrency !== undefined
        || context.deviceMemory !== undefined
        || context.maxTouchPoints !== undefined;
}

/**
 * Краткая подпись личности: две разные личности обязаны давать разные строки.
 *
 * Нужна как ключ «эта вкладка уже накрыта» — по одной только вкладке смена профиля без навигации
 * считалась бы уже применённой и не происходила бы вовсе. Состав тот же, что у сторожа повторной
 * установки: строка агента, платформа, пояс, языки, память, ядра, экран и видеокарта.
 */
export function describeIdentity(context: TabContextEnvelope): string {
    const screen = (context as { screen?: { width?: number; height?: number } }).screen;
    const webGl = (context as { webGl?: { renderer?: string } }).webGl;

    return [
        context.userAgent ?? '',
        context.platform ?? '',
        context.timezone ?? '',
        (context.languages ?? []).join('|'),
        String(context.deviceMemory ?? ''),
        String(context.hardwareConcurrency ?? ''),
        screen ? String(screen.width ?? '') + 'x' + String(screen.height ?? '') : '',
        webGl?.renderer ?? '',
    ].join('\u0001');
}

/**
 * Минимальная подмена личности для контекста Web Worker.
 *
 * Намеренно СИЛЬНО уже установщика для документа. В воркер нельзя тащить ни диагностические
 * обёртки, ни подмены canvas/WebGL/речи/экрана: там этих объектов либо нет вовсе, либо их
 * оборачивание делает горячие нативные методы не-нативными. Проверка Cloudflare исполняется
 * именно в воркере, и обёрнутые `Date.prototype.getTimezoneOffset`, `getBoundingClientRect`,
 * `URL.createObjectURL` ломали её работу: виджет переставал отдавать токен и висел до таймаута,
 * хотя отказа (`error-callback`) уже не было.
 *
 * Поэтому здесь только те поля, которые в воркере реально существуют и выдают операционную
 * систему: строка агента, платформа, языки, число ядер, объём памяти и client hints. Ничего
 * сверх этого — каждая лишняя обёртка здесь стоит дороже, чем даёт.
 *
 * Требование самодостаточности то же, что и у установщика для документа: до воркера доезжает
 * только собственный исходный текст функции.
 */
export function installIdentityInWorker(context: TabContextEnvelope): void {
    try {
        const scope: any = globalThis as any;
        const navigatorObject: any = scope.navigator;
        if (!navigatorObject) {
            return;
        }

        // Подменяем ТОЛЬКО на прототипе. У настоящего браузера эти свойства — аксессоры на
        // `WorkerNavigator.prototype`, а сам объект `navigator` собственных свойств не имеет.
        // Определив собственное свойство на объекте, мы бы создали отличие, наблюдаемое обычным
        // `Object.getOwnPropertyNames(navigator)` — то есть выдали бы себя ровно тем механизмом,
        // которым прячемся. На объект переходим лишь когда прототипа нет вовсе.
        // ★ Обёртка обязана выглядеть РОДНОЙ функцией. Замер показал, что она не выглядела:
        // 'getParameter.toString()' печатал наш исходник, у геттера 'platform' имя было пустым
        // вместо 'get platform', а у подменённых методов присутствовал собственный 'prototype',
        // которого у нативных функций не бывает. Каждый признак читается страницей одной строкой
        // и не требует ни таблиц, ни статистики.
        const nativeOrigins = new WeakMap<Function, Function>();

        // Подставная функция получает имя и арность оригинала и запоминает сам оригинал: его
        // исходник и отдаётся при чтении 'toString'. Собственного 'prototype' у неё нет по
        // построению — обёртки создаются сокращённой записью метода, у которой прототипа не
        // бывает, ровно как у нативных функций.
        const markNative = (patched: any, original: any, name: string, length?: number): any => {
            try {
                Object.defineProperty(patched, 'name', { configurable: true, value: name });

                if (typeof length === 'number') {
                    Object.defineProperty(patched, 'length', { configurable: true, value: length });
                }
            } catch {
                // Неподменяемые описатели маскировке не мешают.
            }

            if (typeof original === 'function') {
                try {
                    nativeOrigins.set(patched, original);
                } catch {
                    // Замороженный объект — остальное всё равно должно встать.
                }
            }

            return patched;
        };

        // Единственный путь прочитать исходник функции — 'Function.prototype.toString'. Подменяем
        // его ОДИН раз: нашим обёрткам он отдаёт исходник оригинала, всему остальному — обычный
        // результат. Сам он тоже зарегистрирован, поэтому чтение собственного текста его не выдаёт.
        {
            const originalFunctionToString = Function.prototype.toString;

            // ★ Формат исходника НАТИВНОЙ функции (V8 печатает одной строкой, JavaScriptCore —
            // тремя, с переносом и отступом) МЫ НЕ ПОДМЕНЯЕМ, хотя профиль и заявляет iOS.
            //
            // Замер на живой проверке Cloudflare, профиль iPhone: с подменой формата её скрипт
            // молча вставал — переставал отвечать сторожевому пингу виджета, и тот через ~40 секунд
            // объявлял отказ 300030 (0/3). Без подмены — токен за ~8 секунд (3/3). Якорная сверка
            // «это ровно нативный исходник» дела не меняет (тоже 0/3): мешает сам переписанный
            // текст, а не ложные срабатывания. Признак движка ценен, но не ценой той самой
            // проверки, ради которой профиль и существует.

            const toStringHolder = {
                toString(this: unknown) {
                    const origin = nativeOrigins.get(this as Function);
                    const target = origin === undefined ? this : origin;
                    return originalFunctionToString.call(target);
                },
            };

            markNative(toStringHolder.toString, originalFunctionToString, 'toString', 0);

            try {
                Object.defineProperty(Function.prototype, 'toString', {
                    configurable: true,
                    writable: true,
                    value: toStringHolder.toString,
                });
            } catch {
                // Неподменяемый прототип — остальные поверхности всё равно должны встать.
            }
        }

        // Метод без собственного 'prototype': сокращённая запись в литерале объекта даёт ровно
        // такую функцию, а обычное 'function ...' — нет.
        const asNativeMethod = (name: string, original: any, implementation: any): any => {
            const holder: any = {
                [name](this: unknown, ...args: unknown[]) {
                    return implementation.apply(this, args);
                },
            };

            return markNative(holder[name], original, name, typeof original === 'function' ? original.length : implementation.length);
        };

        // Геттер аксессора: имя 'get <свойство>' проставляет сам движок, а исходник берётся у
        // родного геттера того же свойства — совпадают и текст, и имя.
        // Получатель пробрасывается: без него подмена на прототипе элемента ломается об WebIDL.
        const asNativeGetter = (property: string, original: any, getter: () => unknown): any => {
            const holder: any = {
                get [property]() {
                    return getter.call(this);
                },
            };

            return markNative(Object.getOwnPropertyDescriptor(holder, property)?.get, original, 'get ' + property, 0);
        };

        const prototype = scope.WorkerNavigator?.prototype;
        const define = (property: string, getter: () => unknown): void => {
            const target = prototype ?? navigatorObject;
            try {
                // Часть профиля браузер применяет САМ: строку агента задаёт '--user-agent', пояс и
                // локаль — переменные окружения процесса. Обёртка поверх уже верного значения —
                // чистый проигрыш: поведение та же, а подменённое свойство находится.
                if (navigatorObject[property] === getter()) {
                    return;
                }
            } catch {
                // Недоступное чтение — просто ставим подмену.
            }

            try {
                const existing = Object.getOwnPropertyDescriptor(target, property);

                Object.defineProperty(target, property, {
                    configurable: true,
                    enumerable: existing ? existing.enumerable : true,
                    get: asNativeGetter(property, existing?.get, getter),
                });
            } catch {
                // Неподменяемое свойство пропускаем: остальные всё равно должны встать.
            }
        };

        if (typeof context.userAgent === 'string' && context.userAgent.length > 0) {
            const userAgent = context.userAgent;
            define('userAgent', () => userAgent);

            const separator = userAgent.indexOf('/');
            const appVersion = separator >= 0 ? userAgent.slice(separator + 1) : userAgent;
            define('appVersion', () => appVersion);
        }

        if (typeof context.platform === 'string' && context.platform.length > 0) {
            const platform = context.platform;
            define('platform', () => platform);
        }

        if (Array.isArray(context.languages) && context.languages.length > 0) {
            const languages = context.languages.slice();
            define('languages', () => Object.freeze(languages.slice()));
            define('language', () => languages[0]);
        }

        if (typeof context.hardwareConcurrency === 'number') {
            const hardwareConcurrency = context.hardwareConcurrency;
            define('hardwareConcurrency', () => hardwareConcurrency);
        }

        if (typeof context.deviceMemory === 'number') {
            const deviceMemory = context.deviceMemory;
            define('deviceMemory', () => deviceMemory);
        }

        const clientHints = context.clientHints;
        if (clientHints !== undefined) {
            const brands = Array.isArray(clientHints.brands)
                ? clientHints.brands.map((item) => ({ brand: item.brand, version: item.version }))
                : [];
            const mobile = clientHints.mobile === true;
            const platformName = typeof clientHints.platform === 'string' ? clientHints.platform : '';

            const highEntropy: Record<string, unknown> = {
                brands: brands.slice(),
                mobile,
                platform: platformName,
                platformVersion: clientHints.platformVersion ?? '',
                architecture: clientHints.architecture ?? '',
                bitness: clientHints.bitness ?? '',
                model: clientHints.model ?? '',
            };

            define('userAgentData', () => ({
                brands: brands.slice(),
                mobile,
                platform: platformName,
                getHighEntropyValues(hints: string[]) {
                    const result: Record<string, unknown> = { brands: brands.slice(), mobile, platform: platformName };
                    if (Array.isArray(hints)) {
                        for (const hint of hints) {
                            if (Object.prototype.hasOwnProperty.call(highEntropy, hint)) {
                                result[hint] = highEntropy[hint];
                            }
                        }
                    }

                    return Promise.resolve(result);
                },
                toJSON() {
                    return { brands: brands.slice(), mobile, platform: platformName };
                },
            }));
        }

        // WebGL внутри воркера (через OffscreenCanvas). Документ уже заявляет видеокарту
        // подменённой платформы, и если воркер отдаст НАСТОЯЩУЮ, получится противоречие внутри
        // одной вкладки — признак подделки более явный, чем любой отдельный признак. Это
        // расхождение мы создали сами, подменив WebGL только в документе.
        const webGl: any = (context as any).webGl;
        const webGlLimits: any = (context as any).webGlParameters;
        if (webGl || webGlLimits) {
            const overrides: Record<number, unknown> = {};

            if (webGl) {
                overrides[0x9245] = webGl.unmaskedVendor ?? webGl.vendor;
                overrides[0x9246] = webGl.unmaskedRenderer ?? webGl.renderer;

                // ★ МАСКИРОВАННЫЕ VENDOR/RENDERER — жёсткие константы Chromium, а не свойство машины.
                // Без WEBGL_debug_renderer_info браузер отдаёт ровно 'WebKit' и 'WebKit WebGL' на любой
                // ОС и любом железе. Сюда клали настоящее имя видеокарты — значение, которого в
                // немодифицированном браузере не бывает: одна проверка getParameter(VENDOR) !== 'WebKit'
                // ловит подмену без ложных срабатываний. Замер в одном прогоне: родной профиль дал
                // 'WebKit', подменённый — 'Google Inc. (Google)'. Имя карты отдаётся только через
                // расширение отладки, то есть через 0x9245/0x9246 выше.
                overrides[0x1f00] = 'WebKit';
                overrides[0x1f01] = 'WebKit WebGL';
            }

            // Числовые пределы: воркер обязан отдавать те же, что документ. Расхождение здесь
            // равнозначно расхождению имени видеокарты — оно внутри одной вкладки и заметно
            // простой сверкой.
            if (webGlLimits) {
                if (webGlLimits.maxTextureSize > 0) {
                    overrides[0x0d33] = webGlLimits.maxTextureSize;
                }

                if (webGlLimits.maxRenderbufferSize > 0) {
                    overrides[0x84e8] = webGlLimits.maxRenderbufferSize;
                }

                if (webGlLimits.maxVaryingVectors > 0) {
                    overrides[0x8dfc] = webGlLimits.maxVaryingVectors;
                }

                if (webGlLimits.maxVertexUniformVectors > 0) {
                    overrides[0x8dfb] = webGlLimits.maxVertexUniformVectors;
                }

                if (webGlLimits.maxFragmentUniformVectors > 0) {
                    overrides[0x8dfd] = webGlLimits.maxFragmentUniformVectors;
                }
            }

            // Размеры вьюпорта отдаются ТИПИЗИРОВАННЫМ массивом и КАЖДЫЙ раз новым: настоящий
            // WebGL здесь всегда возвращает свежий Int32Array, а один и тот же объект выдал бы
            // подмену сравнением двух подряд идущих чтений.
            const viewportDims: any = Array.isArray(webGlLimits?.maxViewportDims) && webGlLimits.maxViewportDims.length === 2
                ? webGlLimits.maxViewportDims
                : null;

            for (const contextName of ['WebGLRenderingContext', 'WebGL2RenderingContext']) {
                try {
                    const prototype = scope[contextName]?.prototype;
                    if (!prototype || typeof prototype.getParameter !== 'function') {
                        continue;
                    }

                    const originalGetParameter = prototype.getParameter;
                    prototype.getParameter = asNativeMethod('getParameter', originalGetParameter, function (this: unknown, parameter: number) {
                        if (viewportDims !== null && parameter === 0x0d3a) {
                            return new Int32Array(viewportDims);
                        }

                        const override = overrides[parameter];
                        return override === undefined ? originalGetParameter.call(this, parameter) : override;
                    });
                } catch {
                    // Контекста может не быть — это нормально.
                }
            }
        }

        // ★ ДВИЖОК В ВОРКЕРЕ. Документ хромовые поверхности при заявленном WebKit уже вычищает,
        // а воркер — нет. Замер CreepJS на профиле iPhone показал в воркере
        // 'userAgentData: Google Chrome 152 / Linux' и настоящую платформу машины: то самое
        // противоречие уровня движка, которое в документе уже устранено. Проверка Cloudflare
        // исполняется именно в воркере, поэтому здесь оно и читается.
        const declaredUserAgent = typeof context.userAgent === 'string' ? context.userAgent : '';

        // Признак движка выводится из ОС: на iOS других движков не бывает вовсе, включая Chrome
        // (CriOS) и Firefox (FxiOS). Правило то же, что у установщика для документа.
        const declaresWebKit = declaredUserAgent.indexOf('iPhone') >= 0
            || declaredUserAgent.indexOf('iPad') >= 0
            || declaredUserAgent.indexOf('iPod') >= 0
            || (declaredUserAgent.indexOf('Safari/') >= 0
                && declaredUserAgent.indexOf('Version/') >= 0
                && declaredUserAgent.indexOf('Chrome/') < 0
                && declaredUserAgent.indexOf('Chromium/') < 0);

        if (declaresWebKit) {
            // Вендор у Safari свой; 'Google Inc.' противоречил бы строке агента.
            define('vendor', () => 'Apple Computer, Inc.');

            // Свойства навигатора, которых у WebKit НЕТ вовсе. Убираем с ПРОТОТИПА: собственное
            // свойство на самом навигаторе наблюдаемо описателем, а у настоящего браузера
            // собственных свойств у 'navigator' не бывает.
            const navigatorPrototype = scope.WorkerNavigator?.prototype;
            const chromiumOnlyNavigator = [
                'userAgentData',
                'deviceMemory',
                'connection',
                'usb',
                'serial',
                'hid',
                'bluetooth',
                'ink',
                'gpu',
                'scheduling',
            ];

            for (const property of chromiumOnlyNavigator) {
                try {
                    if (navigatorPrototype && Object.getOwnPropertyDescriptor(navigatorPrototype, property) !== undefined) {
                        delete navigatorPrototype[property];
                    }

                    if (Object.getOwnPropertyDescriptor(navigatorObject, property) !== undefined) {
                        delete navigatorObject[property];
                    }
                } catch {
                    // Неудаляемое свойство пропускаем: остальные всё равно должны исчезнуть.
                }
            }

            // Глобальные объекты воркера, существующие только в Chromium.
            const chromiumOnlyGlobals = [
                'IdleDetector',
                'USB',
                'Serial',
                'HID',
                'Bluetooth',
                'NetworkInformation',
                'PressureObserver',
                'scheduler',
                'Scheduler',
                'TaskController',
                'TaskPriorityChangeEvent',
                'chrome',
            ];

            // По всей цепочке прототипов: атрибуты интерфейса лежат на 'WorkerGlobalScope.prototype',
            // а не на самом 'self'. Замер в блоб-воркере показывал 'scheduler' живым именно поэтому.
            for (const property of chromiumOnlyGlobals) {
                try {
                    for (let holder: any = scope; holder !== null && holder !== undefined; holder = Object.getPrototypeOf(holder)) {
                        if (Object.getOwnPropertyDescriptor(holder, property) !== undefined) {
                            delete holder[property];
                        }
                    }
                } catch {
                    // Неудаляемый глобальный объект пропускаем: остальные всё равно исчезнут.
                }
            }

            // Точки входа V8 в стек. 'stackTraceLimit' НЕ трогаем: без него V8 перестаёт собирать
            // стек вообще, и пустой 'new Error().stack' — признак заметнее скрываемого.
            try {
                delete (Error as any).captureStackTrace;
                delete (Error as any).prepareStackTrace;
            } catch {
                // Свойства V8 неудаляемыми не бывают, но падать из-за них нельзя.
            }

            try {
                delete (Intl as any).v8BreakIterator;
            } catch {
                // То же самое.
            }
        }

        // Часовой пояс. Документ отдаёт подменённый, а воркер без этой правки — настоящий пояс
        // машины: снова противоречие внутри одной вкладки, созданное нами же.
        const timezone = context.timezone;
        if (typeof timezone === 'string' && timezone.length > 0) {
            try {
                const OriginalDateTimeFormat: any = scope.Intl?.DateTimeFormat;
                const originalResolvedOptions = OriginalDateTimeFormat?.prototype?.resolvedOptions;

                // Пояс процессу задаёт переменная окружения TZ, и в штатном случае воркер уже
                // отдаёт заявленный пояс сам — нативно и во всех путях сразу. Обёртка нужна лишь
                // при реальном расхождении: пояс сменили на лету уже после запуска браузера.
                const nativeTimezone = typeof originalResolvedOptions === 'function'
                    ? new OriginalDateTimeFormat().resolvedOptions().timeZone
                    : timezone;

                if (typeof originalResolvedOptions === 'function' && nativeTimezone !== timezone) {
                    Object.defineProperty(OriginalDateTimeFormat.prototype, 'resolvedOptions', {
                        configurable: true,
                        writable: true,
                        value: asNativeMethod('resolvedOptions', originalResolvedOptions, function (this: unknown) {
                            const result = originalResolvedOptions.call(this);
                            return result !== null && typeof result === 'object'
                                ? { ...result, timeZone: timezone }
                                : result;
                        }),
                    });
                }
            } catch {
                // Неподменяемый прототип — остальное всё равно должно встать.
            }
        }
    } catch {
        // Пролог воркера обязан быть незаметным: его сбой не должен мешать телу воркера.
    }
}

/**
 * Ставит подмену личности в главный мир указанного фрейма (или всех фреймов вкладки).
 *
 * Внедряем ФУНКЦИЮ с аргументом, а не текст скрипта: документ проверки Cloudflare объявляет
 * Trusted Types, из-за чего выполнение строки как кода там запрещено. Функцию браузер компилирует
 * сам, поэтому политика страницы её не касается.
 *
 * `injectImmediately` обязателен: без него браузер вправе отложить внедрение до загрузки
 * документа, и фрейм успеет прочитать настоящее окружение машины раньше нас.
 */
export async function injectIdentityIntoFrames(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
    frameId: number | null,
    context: TabContextEnvelope,
): Promise<string[]> {
    if (browserHost.scripting?.executeScript === undefined) {
        throw new Error('API внедрения скриптов недоступен');
    }

    const target = typeof frameId === 'number'
        ? { tabId, frameIds: [frameId] }
        : { tabId, allFrames: true };

    const results = await invokeBrowserCall<any[]>(
        runtime,
        browserHost.scripting.executeScript,
        browserHost.scripting,
        {
            target,
            world: 'MAIN',
            injectImmediately: true,
            func: installIdentityInMainWorld,
            // Вторым аргументом идёт исходник МИНИМАЛЬНОГО установщика для воркера. Полный
            // установщик туда тащить нельзя: его диагностические обёртки делают горячие нативные
            // методы не-нативными и вешают проверку.
            args: [context, installIdentityInWorker.toString()],
        },
    );

    return (results ?? []).map((item) => String(item?.result ?? ''));
}

/**
 * Подмена личности для ПОДЧИНЁННЫХ фреймов вкладки.
 *
 * Почему это отдельная функция, а не генерируемый текст скрипта.
 * Документ проверки Cloudflare объявляет Trusted Types (`require-trusted-types-for 'script'`),
 * который запрещает выполнять СТРОКУ как код — падают и `eval`, и `new Function`. Наш обычный
 * исполнитель работает именно так, поэтому в этот фрейм он попасть не мог:
 * «Evaluating a string as JavaScript violates this document's Trusted Type assignment
 * requirements». Функция же, переданная в `chrome.scripting.executeScript({ func, args })`,
 * компилируется САМИМ браузером в момент внедрения, минуя политику страницы.
 *
 * Отсюда два жёстких требования к телу функции:
 *  1. Она обязана быть САМОДОСТАТОЧНОЙ — никаких ссылок на внешние переменные, импорты и хелперы
 *     модуля: до фрейма доезжает только её собственный исходный текст и аргументы.
 *  2. Внутри нельзя пользоваться `eval`/`new Function` — по той же причине, из-за которой мы сюда
 *     и пришли.
 *
 * Изоляции хранилищ здесь намеренно нет: у подчинённого фрейма своё происхождение и свой канал
 * синхронизации кук, а переносить сюда изоляцию верхнего фрейма неверно. Задача этой функции
 * ровно одна — чтобы фрейм не выдавал НАСТОЯЩУЮ машину там, где заголовки его запросов уже
 * заявили подменённый профиль.
 */
export function installIdentityInMainWorld(context: TabContextEnvelope, workerSource?: string): string {
    try {
        const globalObject: any = globalThis as any;

        // Сторож повторной установки. Сравниваем ИМЕННО состав профиля, а не факт установки:
        // ранний скрипт `document_start` уже мог поставить профиль запуска, а следом брокер вправе
        // сменить профиль на лету. При булевом стороже такая смена молча терялась бы — документ
        // остался бы с прежней личностью. Одинаковый профиль повторно не ставим: второй проход по
        // уже подменённым геттерам ничего не даёт и только множит риск.
        // ★ Экран и видеокарта входят в сравнение наравне с остальным: два профиля могут нести
        // одну строку агента и разные разрешение/GPU (так устроены дескрипторы устройств).
        // Без них такая смена считалась «уже установленной», и вкладка дорабатывала задачу с экраном и GPU
        // предыдущей личности.
        const desiredComponents = [
            context.userAgent ?? '',
            context.platform ?? '',
            context.timezone ?? '',
            (context.languages ?? []).join('|'),
            context.clientHints?.platform ?? '',
            String(context.deviceMemory ?? ''),
            String(context.hardwareConcurrency ?? ''),
            context.screen ? String(context.screen.width ?? '') + 'x' + String(context.screen.height ?? '') : '',
            context.webGl?.renderer ?? '',
        ];

        // Сторож НЕ хранится на window: любое имя там — след автоматики, который страница читает
        // тем же `Object.getOwnPropertyNames`, каким его находит наш собственный footprint-тест.
        // Вместо этого сравниваем ЖИВОЕ окружение с желаемым: после установки совпадает всё, что
        // профиль задаёт; при смене профиля брокером расходится, и подмена применяется заново.
        // Компоненты, которые профиль НЕ задаёт, в сравнении не участвуют: их живое значение —
        // законная машина, и требовать там пустоту было бы неверно.
        // ★ Считывание живой личности ВСТРОЕНО, а не вынесено в соседнюю функцию.
        //
        // Этот установщик уезжает в страницу через executeScript({ func }), то есть в виде ОДНОЙ
        // сериализованной функции: любое обращение к соседям модуля превращается в странице в
        // ReferenceError. Так и было: динамическая установка личности падала на первой же строке
        // с 'readLiveIdentityComponents is not defined', и весь динамический путь не работал —
        // ни профиль на вкладку, ни смена профиля брокером на лету. Замер по вкладкам показывал
        // это прямо: навигатор во вкладке подменял резидентный скрипт, а видеокарта оставалась
        // браузерной, потому что её подменяет только этот установщик.
        const liveComponents = ((): string[] => {
            try {
                const liveNavigator: any = globalObject.navigator;
                if (!liveNavigator) {
                    return ['', '', '', '', '', '', '', '', ''];
                }

                let liveTimezone = '';
                try {
                    liveTimezone = Intl.DateTimeFormat().resolvedOptions().timeZone ?? '';
                } catch {
                    liveTimezone = '';
                }

                const liveUserAgentDataPlatform = liveNavigator.userAgentData
                    && typeof liveNavigator.userAgentData.platform === 'string'
                    ? liveNavigator.userAgentData.platform
                    : '';

                let liveScreen = '';
                try {
                    const screenObject: any = globalObject.screen;
                    liveScreen = screenObject ? String(screenObject.width ?? '') + 'x' + String(screenObject.height ?? '') : '';
                } catch {
                    liveScreen = '';
                }

                // Строка видеокарты читается через одноразовый контекст: держать его негде, а создание
                // дешёвле пропущенной смены личности.
                let liveRenderer = '';
                try {
                    const probeCanvas = globalObject.document?.createElement?.('canvas');
                    const gl = probeCanvas?.getContext?.('webgl') ?? probeCanvas?.getContext?.('experimental-webgl');
                    if (gl) {
                        const debugInfo = gl.getExtension?.('WEBGL_debug_renderer_info');
                        if (debugInfo) liveRenderer = String(gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL) ?? '');
                    }
                } catch {
                    liveRenderer = '';
                }

                return [
                    typeof liveNavigator.userAgent === 'string' ? liveNavigator.userAgent : '',
                    typeof liveNavigator.platform === 'string' ? liveNavigator.platform : '',
                    liveTimezone,
                    Array.isArray(liveNavigator.languages) ? liveNavigator.languages.join('|') : '',
                    liveUserAgentDataPlatform,
                    typeof liveNavigator.deviceMemory === 'number' ? String(liveNavigator.deviceMemory) : '',
                    typeof liveNavigator.hardwareConcurrency === 'number' ? String(liveNavigator.hardwareConcurrency) : '',
                    liveScreen,
                    liveRenderer,
                ];
            } catch {
                return ['', '', '', '', '', '', '', '', ''];
            }
        })();
        const alreadyInstalled = desiredComponents.every(
            (desired, index) => desired === '' || desired === liveComponents[index],
        );

        if (alreadyInstalled) {
            return 'already';
        }

        const navigatorObject: any = globalObject.navigator;
        if (!navigatorObject) {
            return 'no-navigator';
        }

        // ★ Обёртка обязана выглядеть РОДНОЙ функцией. Замер показал, что она не выглядела:
        // 'getParameter.toString()' печатал наш исходник, у геттера 'platform' имя было пустым
        // вместо 'get platform', а у подменённых методов присутствовал собственный 'prototype',
        // которого у нативных функций не бывает. Каждый признак читается страницей одной строкой
        // и не требует ни таблиц, ни статистики.
        const nativeOrigins = new WeakMap<Function, Function>();

        // Подставная функция получает имя и арность оригинала и запоминает сам оригинал: его
        // исходник и отдаётся при чтении 'toString'. Собственного 'prototype' у неё нет по
        // построению — обёртки создаются сокращённой записью метода, у которой прототипа не
        // бывает, ровно как у нативных функций.
        const markNative = (patched: any, original: any, name: string, length?: number): any => {
            try {
                Object.defineProperty(patched, 'name', { configurable: true, value: name });

                if (typeof length === 'number') {
                    Object.defineProperty(patched, 'length', { configurable: true, value: length });
                }
            } catch {
                // Неподменяемые описатели маскировке не мешают.
            }

            if (typeof original === 'function') {
                try {
                    nativeOrigins.set(patched, original);
                } catch {
                    // Замороженный объект — остальное всё равно должно встать.
                }
            }

            return patched;
        };

        // Единственный путь прочитать исходник функции — 'Function.prototype.toString'. Подменяем
        // его ОДИН раз: нашим обёрткам он отдаёт исходник оригинала, всему остальному — обычный
        // результат. Сам он тоже зарегистрирован, поэтому чтение собственного текста его не выдаёт.
        {
            const originalFunctionToString = Function.prototype.toString;

            // ★ Формат исходника НАТИВНОЙ функции (V8 печатает одной строкой, JavaScriptCore —
            // тремя, с переносом и отступом) МЫ НЕ ПОДМЕНЯЕМ, хотя профиль и заявляет iOS.
            //
            // Замер на живой проверке Cloudflare, профиль iPhone: с подменой формата её скрипт
            // молча вставал — переставал отвечать сторожевому пингу виджета, и тот через ~40 секунд
            // объявлял отказ 300030 (0/3). Без подмены — токен за ~8 секунд (3/3). Якорная сверка
            // «это ровно нативный исходник» дела не меняет (тоже 0/3): мешает сам переписанный
            // текст, а не ложные срабатывания. Признак движка ценен, но не ценой той самой
            // проверки, ради которой профиль и существует.

            const toStringHolder = {
                toString(this: unknown) {
                    const origin = nativeOrigins.get(this as Function);
                    const target = origin === undefined ? this : origin;
                    return originalFunctionToString.call(target);
                },
            };

            markNative(toStringHolder.toString, originalFunctionToString, 'toString', 0);

            try {
                Object.defineProperty(Function.prototype, 'toString', {
                    configurable: true,
                    writable: true,
                    value: toStringHolder.toString,
                });
            } catch {
                // Неподменяемый прототип — остальные поверхности всё равно должны встать.
            }
        }

        // Метод без собственного 'prototype': сокращённая запись в литерале объекта даёт ровно
        // такую функцию, а обычное 'function ...' — нет.
        // ★ Кадры расширения вычёркиваются из стека брошенной ошибки.
        //
        // Родной геттер, вызванный на чужом получателе, бросает ИЗНУТРИ нашей обёртки, и её кадр
        // остаётся в '.stack': странице был виден 'at get width (chrome-extension://<id>/…)' —
        // адрес расширения и имя файла, то есть прямое доказательство подмены, читаемое одной
        // строкой. Замер: 4 такие поверхности при 0 у эталонного Chrome, и лишний кадр глубины
        // (7 против 5) сверх того.
        //
        // Снимается только собственный кадр: сообщение, тип и порядок остальных строк сохраняются,
        // поэтому ошибка неотличима от родной. Правка '.stack' безопасна — это обычное строковое
        // свойство экземпляра, в отличие от 'Error.prepareStackTrace', крючок которого исполнялся
        // бы при каждом чтении стека любым скриптом страницы (см. ниже, почему он не ставится).
        const withoutExtensionFrames = (error: unknown): unknown => {
            try {
                const stack = (error as { stack?: unknown })?.stack;

                if (typeof stack !== 'string') return error;

                const cleaned = stack
                    .split('\n')
                    .filter(line => line.indexOf('chrome-extension://') < 0 && line.indexOf('moz-extension://') < 0)
                    .join('\n');

                if (cleaned !== stack) (error as { stack?: unknown }).stack = cleaned;
            } catch {
                // Неизменяемый стек — ошибка всё равно должна дойти до страницы.
            }

            return error;
        };

        const asNativeMethod = (name: string, original: any, implementation: any): any => {
            const holder: any = {
                [name](this: unknown, ...args: unknown[]) {
                    try {
                        return implementation.apply(this, args);
                    } catch (error) {
                        throw withoutExtensionFrames(error);
                    }
                },
            };

            return markNative(holder[name], original, name, typeof original === 'function' ? original.length : implementation.length);
        };

        // ★ 'Error.prepareStackTrace': СВОЙ крючок не ставится, но ЧУЖОЙ оборачивается.
        //
        // Обработчик страницы получает кадры СТРУКТУРАМИ, до сборки строки, и видит
        // 'getFileName() === chrome-extension://…' мимо любой чистки текста — замер показывал
        // 'prepareStackTrace видит extension' = true при нуле утечек в обычном стеке, тогда как
        // у эталонного Chrome false. Это последний канал, по которому адрес расширения виден.
        //
        // Ставить СВОЙ обработчик нельзя: он исполнялся бы при каждом чтении '.stack' любым
        // скриптом, включая проверку Cloudflare, и ошибка внутри него рвала бы её целиком.
        // Поэтому перехватывается УСТАНОВКА: пока страница обработчика не ставила, не исполняется
        // ничего; как только поставила — её функция получает уже отфильтрованные кадры. Цена
        // платится ровно теми страницами, которые сами полезли в стек.
        // ★ ФОРМА СВОЙСТВА — сама по себе признак. У чистого V8 'prepareStackTrace' не существует
        // вовсе (getOwnPropertyDescriptor даёт undefined), а пара get/set находится одной строкой.
        // Рубильник нужен, чтобы измерить, не дороже ли сокрытие кадров самого их наличия.
        try {
            const surfacesOff = (context as unknown as { disabledOsSurfaces?: unknown }).disabledOsSurfaces;
            if (Array.isArray(surfacesOff) && surfacesOff.includes('prepareStack')) throw new Error('disabled');

            let pageHandler: unknown;
            const isExtensionFrame = (frame: any): boolean => {
                try {
                    const fileName = typeof frame?.getFileName === 'function' ? frame.getFileName() : undefined;

                    return typeof fileName === 'string'
                        && (fileName.indexOf('chrome-extension://') >= 0 || fileName.indexOf('moz-extension://') >= 0);
                } catch {
                    return false;
                }
            };

            Object.defineProperty(Error, 'prepareStackTrace', {
                configurable: true,
                enumerable: false,
                get: () => pageHandler,
                set: (handler: unknown) => {
                    if (typeof handler !== 'function') {
                        pageHandler = handler;
                        return;
                    }

                    const wrapped = function (this: unknown, error: unknown, frames: unknown) {
                        const visible = Array.isArray(frames)
                            ? frames.filter(frame => !isExtensionFrame(frame))
                            : frames;

                        return (handler as (error: unknown, frames: unknown) => unknown).call(this, error, visible);
                    };

                    // Обёртка обязана выглядеть функцией страницы: исходник и длина берутся у неё,
                    // иначе сам обработчик, прочитав свой 'toString', и выдаст подмену.
                    markNative(wrapped, handler, (handler as { name?: string }).name ?? '', (handler as { length?: number }).length ?? 2);
                    pageHandler = wrapped;
                },
            });
        } catch {
            // Неподменяемое свойство — остальные поверхности всё равно должны встать.
        }

        // ★ Получатель обязан доехать до подставленного геттера.
        //
        // Обёртка звала getter() без 'this'. Для свойств navigator/screen это безразлично, но
        // подмена на HTMLElement.prototype так ЛОМАЕТСЯ: внутри 'this' оказывался объект-держатель,
        // и родной WebIDL-геттер отвергал чужой получатель — 'offsetWidth' бросал TypeError
        // «Illegal invocation» на ЛЮБОМ элементе. Настоящий браузер такого не делает никогда, а
        // размеры виджета читаются именно им. Замер: ветка подмены шрифтов включается при
        // declaredOs Windows/macOS — ровно на этих профилях элемент и ломался, тогда как Linux и
        // Android (ветка выключена) работали. 'clientWidth' уцелел случайно: он объявлен на
        // Element.prototype, и родной геттер предка перекрывал битую копию.
        const asNativeGetter = (property: string, original: any, getter: () => unknown): any => {
            const holder: any = {
                get [property]() {
                    // ★ Чужой получатель обязан ронять геттер так же, как родной.
                    //
                    // Атрибуты WebIDL проверяют тип получателя: 'Screen.prototype.width.get.call({})'
                    // у настоящего браузера бросает TypeError «Illegal invocation». Наши обёртки
                    // отдавали значение кому угодно — замер на 2507 методах: эталонный Chrome даёт
                    // 0 таких протечек, у нас было 18. Одна строка проверки со стороны сайта, и
                    // расхождение с эталоном абсолютное, без опоры на значения.
                    if (typeof original === 'function') {
                        try {
                            original.call(this);
                        } catch (error) {
                            throw withoutExtensionFrames(error);
                        }
                    }

                    return getter.call(this);
                },
            };

            return markNative(Object.getOwnPropertyDescriptor(holder, property)?.get, original, 'get ' + property, 0);
        };

        const defineGetter = (target: any, property: string, getter: () => unknown): void => {
            if (!target) {
                return;
            }

            try {
                const existing = Object.getOwnPropertyDescriptor(target, property);

                Object.defineProperty(target, property, {
                    configurable: true,
                    // Атрибуты WebIDL перечислимы; неперечислимое свойство на месте перечислимого
                    // отличается от настоящего одним вызовом getOwnPropertyDescriptor.
                    enumerable: existing ? existing.enumerable : true,
                    get: asNativeGetter(property, existing?.get, getter),
                });
            } catch {
                // Неподменяемое свойство пропускаем: остальные всё равно должны встать.
            }
        };

        // Уже ли живое окружение отдаёт ровно то, что мы собирались подставить.
        //
        // Часть профиля браузер применяет САМ: строку агента задаёт '--user-agent', часовой пояс и
        // локаль — переменные окружения процесса. Ставить обёртку поверх уже верного значения —
        // чистый проигрыш: поведение то же, а подменённое свойство находится.
        const alreadyMatches = (target: any, property: string, getter: () => unknown): boolean => {
            try {
                const desired: any = getter();

                // Профиль ничего не заявляет — подменять нечего, а лишняя обёртка находится
                // обычным описателем свойства.
                if (desired === undefined || desired === null) {
                    return true;
                }

                const current: any = target[property];

                if (Array.isArray(desired)) {
                    return Array.isArray(current)
                        && current.length === desired.length
                        && desired.every((value: unknown, index: number) => value === current[index]);
                }

                return current === desired;
            } catch {
                return false;
            }
        };

        // Атрибуты навигатора в настоящем браузере объявлены НА ПРОТОТИПЕ, а не на самом объекте:
        // 'Object.getOwnPropertyNames(navigator)' у Chrome пуст. Замер показывал там пятнадцать
        // собственных свойств — след, видимый без единой проверки значения. Поэтому подменяем ровно
        // там, где свойство объявлено, и НИКОГДА не добавляем несуществующее.
        const defineNavigatorGetter = (property: string, getter: () => unknown): void => {
            if (alreadyMatches(navigatorObject, property, getter)) {
                return;
            }

            // В контексте воркера прототип навигатора называется WorkerNavigator.
            const prototype = globalObject.Navigator?.prototype ?? globalObject.WorkerNavigator?.prototype;

            if (prototype && Object.getOwnPropertyDescriptor(prototype, property) !== undefined) {
                defineGetter(prototype, property, getter);
                return;
            }

            if (Object.getOwnPropertyDescriptor(navigatorObject, property) !== undefined) {
                defineGetter(navigatorObject, property, getter);
            }
        };

        // Экран — та же история, что и навигатор: его атрибуты объявлены на 'Screen.prototype'.
        // Сверку «уже совпадает» экран не проходит: на виртуальном дисплее его размер РАВЕН
        // заявленному, а окно браузер может расширить до своей нижней границы уже после установки
        // подмены — и тогда пропущенный геттер навсегда оставит экран уже окна.
        const defineScreenGetter = (screenTarget: any, property: string, getter: () => unknown): void => {
            if (!screenTarget) {
                return;
            }

            const prototype = globalObject.Screen?.prototype;

            if (prototype && Object.getOwnPropertyDescriptor(prototype, property) !== undefined) {
                defineGetter(prototype, property, getter);
                return;
            }

            defineGetter(screenTarget, property, getter);
        };

        // Глобальный объект — исключение из правила о прототипе: по WebIDL у интерфейса с [Global]
        // атрибуты объявляются на самом объекте, и в настоящем браузере они там собственные.
        const defineWindowGetter = (property: string, getter: () => unknown): void => {
            if (alreadyMatches(globalObject, property, getter)) {
                return;
            }

            defineGetter(globalObject, property, getter);
        };

        // ★ WEB WORKER — здесь и была настоящая дыра.
        //
        // Замер счётчиками показал: проверка Cloudflare собирает blob и запускает из него Worker.
        // У воркера СВОЙ глобальный контекст и свой navigator, куда подмены главного мира не
        // попадают вовсе — там окружение остаётся НАСТОЯЩИМ. Отсюда прежняя картина: страница
        // полностью согласована под чужую ОС, а проверка всё равно видит правду. И отсюда же
        // объяснение, почему canvas, шрифты и WebGL ничего не решали — в главном мире фрейма их
        // никто не читал.
        //
        // Подменяем на уровне СОЗДАНИЯ АДРЕСА, а не конструктора Worker. Так лучше по трём
        // причинам сразу:
        //  1. Конструктор `Worker` остаётся нативным — его `toString` не выдаёт подмену.
        //  2. Сайт получает адрес УЖЕ пропатченного blob'а, поэтому `self.location.href` внутри
        //     воркера совпадает с тем, что сайт держит у себя: расхождения адресов нет.
        //  3. Не нужно обходить Trusted Types: сайт сам оформит адрес так, как требует политика.
        if (typeof workerSource === 'string' && workerSource.length > 0) {
            try {
                const urlObject = globalObject.URL;
                const originalCreateObjectURL = urlObject?.createObjectURL;

                if (typeof originalCreateObjectURL === 'function') {
                    // Пролог обязан быть незаметным для тела воркера: собственный try/catch,
                    // никаких объявлений в общей области видимости и перевод строки в конце,
                    // чтобы склейка не слипалась с первой строкой оригинала.
                    const prelude = ';(function(){try{(' + workerSource + ')(' + JSON.stringify(context) + ');'
                        + '}catch(e){}})();\n';

                    urlObject.createObjectURL = asNativeMethod('createObjectURL', originalCreateObjectURL, function (this: unknown, source: any) {
                        try {
                            if (source instanceof globalObject.Blob) {
                                const type = String(source.type ?? '').toLowerCase();

                                // Склеиваем ТОЛЬКО скриптовые blob'ы. Конструктор Blob принимает
                                // другие Blob'ы как части, поэтому склейка синхронна и сохраняет
                                // оригинальное тело байт в байт — читать его не требуется.
                                if (type.indexOf('javascript') >= 0 || type.indexOf('ecmascript') >= 0) {
                                    const patched = new globalObject.Blob([prelude, source], { type: source.type });
                                    return originalCreateObjectURL.call(this, patched);
                                }
                            }
                        } catch {
                            // Любой сбой склейки не должен ломать сайт: отдаём адрес как есть.
                        }

                        return originalCreateObjectURL.call(this, source);
                    });
                }
            } catch {
                // Неподменяемый метод — воркеры останутся без подмены, страница не пострадает.
            }
        }

        if (typeof context.userAgent === 'string' && context.userAgent.length > 0) {
            const userAgent = context.userAgent;
            defineNavigatorGetter('userAgent', () => userAgent);

            const appVersionIndex = userAgent.indexOf('/');
            const appVersion = appVersionIndex >= 0 ? userAgent.slice(appVersionIndex + 1) : userAgent;
            defineNavigatorGetter('appVersion', () => appVersion);
        }

        if (typeof context.platform === 'string' && context.platform.length > 0) {
            const platform = context.platform;
            defineNavigatorGetter('platform', () => platform);
        }

        if (Array.isArray(context.languages) && context.languages.length > 0) {
            const languages = context.languages.slice();
            defineNavigatorGetter('languages', () => Object.freeze(languages.slice()));
            defineNavigatorGetter('language', () => languages[0]);
        } else if (typeof context.locale === 'string' && context.locale.length > 0) {
            const locale = context.locale;
            defineNavigatorGetter('language', () => locale);
        }

        if (typeof context.hardwareConcurrency === 'number') {
            const hardwareConcurrency = context.hardwareConcurrency;
            defineNavigatorGetter('hardwareConcurrency', () => hardwareConcurrency);
        }

        if (typeof context.deviceMemory === 'number') {
            const deviceMemory = context.deviceMemory;
            defineNavigatorGetter('deviceMemory', () => deviceMemory);
        }

        if (typeof context.maxTouchPoints === 'number') {
            const maxTouchPoints = context.maxTouchPoints;
            defineNavigatorGetter('maxTouchPoints', () => maxTouchPoints);
        }

        // Client Hints. Заголовки `Sec-CH-UA*` переписываются по вкладке и накрывают в том числе
        // запрос этого фрейма, поэтому JS-сторона обязана говорить ТО ЖЕ САМОЕ: расхождение между
        // заголовком и `navigator.userAgentData` — самостоятельный признак подделки.
        const clientHints = context.clientHints;
        if (clientHints !== undefined) {
            const brands = Array.isArray(clientHints.brands)
                ? clientHints.brands.map((item) => ({ brand: item.brand, version: item.version }))
                : [];
            const fullVersionList = Array.isArray(clientHints.fullVersionList)
                ? clientHints.fullVersionList.map((item) => ({ brand: item.brand, version: item.version }))
                : brands;
            const mobile = clientHints.mobile === true;
            const platformName = typeof clientHints.platform === 'string' ? clientHints.platform : '';

            const highEntropyValues: Record<string, unknown> = {
                brands: brands.slice(),
                mobile,
                platform: platformName,
            };

            if (typeof clientHints.platformVersion === 'string') {
                highEntropyValues.platformVersion = clientHints.platformVersion;
            }

            if (typeof clientHints.architecture === 'string') {
                highEntropyValues.architecture = clientHints.architecture;
            }

            if (typeof clientHints.bitness === 'string') {
                highEntropyValues.bitness = clientHints.bitness;
            }

            if (typeof clientHints.model === 'string') {
                highEntropyValues.model = clientHints.model;
            }

            // ★ Полная версия берётся из СПИСКА версий, а не оставляется пустой.
            //
            // Пустая строка при заполненном 'fullVersionList' невозможна ни на одном настоящем
            // Chrome: обе подсказки движок выводит из одной версии. Замер при заявленном Windows
            // давал uaFullVersion='' и полный fullVersionList рядом — противоречие читается одним
            // вызовом getHighEntropyValues.
            const brandVersion = fullVersionList
                .find((entry) => typeof entry?.brand === 'string' && entry.brand.indexOf('Brand') < 0)?.version;

            if (typeof brandVersion === 'string' && brandVersion.length > 0) {
                highEntropyValues.uaFullVersion = brandVersion;
            }

            highEntropyValues.fullVersionList = fullVersionList.slice();

            // Форм-фактор и режим совместимости Chrome отдаёт ВСЕГДА начиная со 110-й версии.
            // Их отсутствие в ответе — не «мы не знаем», а признак, что отвечает не браузер.
            highEntropyValues.formFactors = mobile ? ['Mobile'] : ['Desktop'];
            highEntropyValues.wow64 = false;

            const userAgentData = {
                get brands() {
                    return brands.slice();
                },
                get mobile() {
                    return mobile;
                },
                get platform() {
                    return platformName;
                },
                getHighEntropyValues(hints: string[]) {
                    const result: Record<string, unknown> = {
                        brands: brands.slice(),
                        mobile,
                        platform: platformName,
                    };

                    if (Array.isArray(hints)) {
                        for (const hint of hints) {
                            if (Object.prototype.hasOwnProperty.call(highEntropyValues, hint)) {
                                result[hint] = highEntropyValues[hint];
                            }
                        }
                    }

                    return Promise.resolve(result);
                },
                toJSON() {
                    return { brands: brands.slice(), mobile, platform: platformName };
                },
            };

            // ★ Исходник подставного объекта маскируется под родной.
            //
            // Литерал отдавал 'Function.prototype.toString' НАШ текст: замер показывал
            // 'get brands() { return brands.slice(); }' вместо '[native code]'. Эталонный Chrome
            // не печатает пользовательский код ни у одного свойства платформы — расхождение видно
            // одной строкой и не зависит ни от значений, ни от заявленной ОС.
            try {
                const liveUserAgentData: any = (globalObject.navigator as { userAgentData?: unknown } | undefined)?.userAgentData;
                const livePrototype = liveUserAgentData ? Object.getPrototypeOf(liveUserAgentData) : undefined;

                for (const property of ['brands', 'mobile', 'platform'] as const) {
                    const patched = Object.getOwnPropertyDescriptor(userAgentData, property)?.get;
                    const origin = livePrototype
                        ? Object.getOwnPropertyDescriptor(livePrototype, property)?.get
                        : undefined;

                    if (patched) markNative(patched, origin, 'get ' + property, 0);
                }

                for (const method of ['getHighEntropyValues', 'toJSON'] as const) {
                    const origin = livePrototype?.[method];

                    markNative(
                        userAgentData[method],
                        origin,
                        method,
                        typeof origin === 'function' ? origin.length : userAgentData[method].length);
                }
            } catch {
                // Маскировка не удалась — подмена всё равно должна встать.
            }

            defineNavigatorGetter('userAgentData', () => userAgentData);
        }

        if (typeof context.doNotTrack === 'boolean') {
            const doNotTrack = context.doNotTrack ? '1' : '0';
            defineNavigatorGetter('doNotTrack', () => doNotTrack);
        }

        if (typeof context.globalPrivacyControl === 'boolean') {
            const globalPrivacyControl = context.globalPrivacyControl;
            defineNavigatorGetter('globalPrivacyControl', () => globalPrivacyControl);
        }

        // ★ Экран и область просмотра следуют ПРОФИЛЮ, а не размеру окна. Размером окна задача
        // нерешаема в принципе: вкладки одного окна несут разные профили, а Chromium вдобавок не сужает
        // окно ниже примерно 500 точек — телефонные 412 недостижимы физически. Здесь запечён профиль
        // БРАУЗЕРА; вкладка со своим профилем и раскладка доводятся установщиком во вкладке.
        const screenObject = globalObject.screen;
        const declaredScreen = context.screen;

        // ★ Экран отдаёт РОВНО заявленный профилем размер. Раньше он подгонялся под окно через
        // Math.max, и сайт видел экран 500 вместо телефонных 412 — то есть наше ограничение
        // протекало в личность. Размер окна не должен определять метаданные экрана: у настоящего
        // телефона они заданы аппаратно и от окна не зависят вовсе.
        const declaredWidth = declaredScreen?.width ?? context.viewport?.width;
        const declaredHeight = declaredScreen?.height ?? context.viewport?.height;

        // ★ Метрики окна и области просмотра следуют заявленной области просмотра. Браузер держит
        // нижнюю границу ширины окна около 500 px и до телефонных 412 его не сужает, а поверхность
        // вдобавок шире окна на поля тени — оба числа выдают среду, отличную от заявленной.
        // Раскладку под эти числа подгоняет установщик вкладки: здесь профиль ещё общебраузерный.
        const declaredViewportWidth = context.viewport?.width ?? declaredWidth;
        const declaredViewportHeight = context.viewport?.height ?? declaredHeight;

        // Область просмотра не больше доступной области: профиль рабочего стола заявляет вьюпорт
        // 1080 при доступных 1040, а окно вместе с рамкой браузера за экран не выходит.
        const clampToAvailable = (value: number, available: number | undefined): number => typeof available === 'number' && available > 0
            ? Math.min(value, available)
            : value;

        // У встроенного фрейма своя область просмотра — размер самого фрейма, а не устройства.
        const isTopLevelDocument = (() => {
            try {
                return globalObject.top === globalObject.self;
            } catch {
                return false;
            }
        })();

        // Размер раскладки объявлен на Element.prototype и читается ВСЕМИ элементами сразу,
        // поэтому подменяется ровно корневой своего документа: у прочих размер обязан остаться настоящим.
        const defineRootClientSizeGetter = (property: string, value: number): void => {
            const elementPrototype = globalObject.Element?.prototype;
            const descriptor = Object.getOwnPropertyDescriptor(elementPrototype, property);
            const original = descriptor?.get;

            if (!original) {
                return;
            }

            // Обёртка ставится НАПРЯМУЮ, минуя 'defineGetter': тот зовёт подменяющий геттер без
            // приёмника, и для свойства с Element.prototype это даёт «Illegal invocation» —
            // родной геттер обязан вызываться на самом элементе.
            try {
                const holder: any = {
                    get [property]() {
                        if (this === globalObject.document?.documentElement) return value;

                        // Чужой получатель роняет родной геттер ИЗНУТРИ обёртки, и её кадр с
                        // адресом расширения остаётся в стеке — см. withoutExtensionFrames.
                        try {
                            return original.call(this);
                        } catch (error) {
                            throw withoutExtensionFrames(error);
                        }
                    },
                };

                Object.defineProperty(elementPrototype, property, {
                    configurable: true,
                    enumerable: descriptor.enumerable,
                    get: markNative(Object.getOwnPropertyDescriptor(holder, property)?.get, original, 'get ' + property, 0),
                });
            } catch {
                // Неподменяемое свойство пропускаем: остальные всё равно должны встать.
            }
        };

        // Рубильник всего дисплейного слоя разом: вьюпорт, экран, devicePixelRatio и производные.
        // Ни один путь чтения геометрии не подменяется, страница видит настоящее окно.
        const displaySurfacesOff = (() => {
            const names = (context as unknown as { disabledOsSurfaces?: unknown }).disabledOsSurfaces;
            return Array.isArray(names) && names.includes('display');
        })();

        if (!displaySurfacesOff && isTopLevelDocument && typeof declaredViewportWidth === 'number' && declaredViewportWidth > 0) {
            const viewportWidth = clampToAvailable(declaredViewportWidth, declaredScreen?.availWidth);

            defineWindowGetter('innerWidth', () => viewportWidth);
            defineWindowGetter('outerWidth', () => viewportWidth);

            // ★ Раскладка корня идёт ВМЕСТЕ с 'innerWidth'. Пока её подменял только установщик
            // вкладки, между ними зияла дыра: он приходит позже, и до его прихода страница видела
            // 'innerWidth' заявленным, а 'clientWidth' — настоящим. Замер матрицы признаков дал
            // разницу 32px там, где у чистого браузера стоит полоса прокрутки (15px).
            defineRootClientSizeGetter('clientWidth', viewportWidth);
        }

        if (!displaySurfacesOff && isTopLevelDocument && typeof declaredViewportHeight === 'number' && declaredViewportHeight > 0) {
            const viewportHeight = clampToAvailable(declaredViewportHeight, declaredScreen?.availHeight);

            // ★ Рамка браузера (вкладки и адресная строка) съедает область просмотра ВНУТРИ
            // окна, а не выталкивает само окно за экран. Равенство outer и inner невозможно у
            // настольного браузера, но и окно выше экрана невозможно тоже — замер ловил out=1920x1127
            // при экране 1080. Поэтому окно равно доступной области, а вьюпорт — окно минус рамка.
            // Рамка берётся ЖИВАЯ, когда окно её уже знает: константа 87 расходилась с фактической
            // раскладкой, и '(height: <innerHeight>px)' отвечал false — движок считает по настоящему
            // окну. Константа остаётся запасным значением, пока размеры окна ещё не известны.
            const liveChromeHeight = globalObject.outerHeight > globalObject.innerHeight
                ? globalObject.outerHeight - globalObject.innerHeight
                : 0;
            const chromeHeight = context.isMobile === true ? 0 : (liveChromeHeight > 0 ? liveChromeHeight : 87);
            const outerHeightValue = viewportHeight;
            const innerHeightValue = Math.max(1, viewportHeight - chromeHeight);

            defineWindowGetter('innerHeight', () => innerHeightValue);
            defineWindowGetter('outerHeight', () => outerHeightValue);
            defineRootClientSizeGetter('clientHeight', innerHeightValue);
        }

        // ★ Область просмотра обязана совпадать по ВСЕМ трём путям чтения.
        //
        // Ранний скрипт подменял 'innerWidth/innerHeight', а 'visualViewport' и медиазапросы
        // '(width:)'/'(height:)' оставлял родными: замер давал innerWidth=1920 при '100vw'=1888 и
        // 'matchMedia("(width: 1920px)")'=false — три независимых пути об одном вьюпорте расходились
        // на 32 и 42 пикселя. У настоящего браузера они сходятся всегда, и расхождение читается
        // одной строкой. Подмена вкладки эти пути накрывает, но приходит позже — до её прихода
        // страница видит рассогласование.
        if (!displaySurfacesOff
            && isTopLevelDocument
            && typeof declaredViewportWidth === 'number'
            && typeof declaredViewportHeight === 'number'
            && declaredViewportWidth > 0
            && declaredViewportHeight > 0) {
            const viewportWidth = clampToAvailable(declaredViewportWidth, declaredScreen?.availWidth);
            const viewportHeight = clampToAvailable(declaredViewportHeight, declaredScreen?.availHeight);
            const innerHeightValue = Math.max(1, viewportHeight - (context.isMobile === true ? 0 : 87));

            try {
                const visualViewportPrototype = globalObject.VisualViewport?.prototype;

                if (visualViewportPrototype) {
                    // Зазор между вьюпортом и областью просмотра сохраняется: у настоящего браузера
                    // 'visualViewport.width' меньше 'innerWidth' ровно на полосу прокрутки, и строгое
                    // равенство было бы таким же расхождением с эталоном, как прежний разнобой.
                    const liveViewport: any = (globalObject as { visualViewport?: unknown }).visualViewport;
                    const widthGap = Math.max(0, globalObject.innerWidth - (liveViewport?.width ?? globalObject.innerWidth));
                    const heightGap = Math.max(0, globalObject.innerHeight - (liveViewport?.height ?? globalObject.innerHeight));

                    const overrides = [
                        ['width', Math.max(1, viewportWidth - widthGap)],
                        ['height', Math.max(1, innerHeightValue - heightGap)],
                    ] as const;

                    for (const [property, value] of overrides) {
                        if (Object.getOwnPropertyDescriptor(visualViewportPrototype, property)?.get) {
                            defineGetter(visualViewportPrototype, property, () => value);
                        }
                    }
                }
            } catch {
                // Недоступный VisualViewport — числовые подмены всё равно встали.
            }

            // ★ 'matchMedia' НЕ подменяется, хотя '(width: <innerWidth>px)' и отвечает false.
            //
            // Переписывание запроса ломает адаптивную вёрстку: '(min-width: 100px)' превращался в
            // '(min-width: 1920px)' и давал false там, где браузер даёт true, а '(max-width: 500px)' —
            // наоборот true. Плюс 'mql.media' возвращал НЕ ТО, что спросили, — признак подмены
            // сильнее скрываемого. Согласовать этот путь текстом нельзя: движок раскладки считает по
            // настоящему окну, и CSS '@media' в таблице стилей всё равно ответит по нему же.
        }

        const resolveScreenWidth = (): number => declaredWidth ?? 0;
        const resolveScreenHeight = (): number => declaredHeight ?? 0;
        const screenWidth = declaredWidth;
        const screenHeight = declaredHeight;

        if (!displaySurfacesOff && screenObject && typeof screenWidth === 'number' && typeof screenHeight === 'number') {
            defineScreenGetter(screenObject, 'width', resolveScreenWidth);
            defineScreenGetter(screenObject, 'height', resolveScreenHeight);

            if (typeof declaredScreen?.colorDepth === 'number') {
                const colorDepth = declaredScreen.colorDepth;
                defineScreenGetter(screenObject, 'colorDepth', () => colorDepth);
            }

            if (typeof declaredScreen?.pixelDepth === 'number') {
                const pixelDepth = declaredScreen.pixelDepth;
                defineScreenGetter(screenObject, 'pixelDepth', () => pixelDepth);
            }
        }

        if (!displaySurfacesOff && typeof context.deviceScaleFactor === 'number') {
            const deviceScaleFactor = context.deviceScaleFactor;

            // ★ Подмена ставится БЕЗУСЛОВНО, минуя сверку «уже совпадает».
            //
            // Масштаб вкладки, которым сужается область просмотра, ДЕЛИТ devicePixelRatio: замер
            // давал 0.9833 при заявленном 1 — значение, невозможное ни у одного устройства.
            // Сверка же видела совпадение ДО масштабирования (масштаб ставится по onCommitted,
            // то есть позже) и подмену пропускала, оставляя странице расхождение.
            defineGetter(globalObject, 'devicePixelRatio', () => deviceScaleFactor);
        }

        // Соединение. Настоящий канал машины противоречит и заявленному устройству, и прокси:
        // замер видел 3g с задержкой 400 мс при заявленном 4g. Подменяем ТОЛЬКО существующие
        // свойства: у настольного Chrome, например, нет 'type', и добавленное поле выдало бы
        // подмену вернее любого расхождения значений.
        const network = context.network;
        const connectionPrototype = globalObject.NetworkInformation?.prototype;

        if (network !== undefined && connectionPrototype) {
            const connectionValues: Record<string, unknown> = {
                effectiveType: network.effectiveType,
                type: network.type,
                downlink: network.downlink,
                rtt: network.rtt,
            };

            for (const property of Object.keys(connectionValues)) {
                const value = connectionValues[property];

                if (value === undefined || Object.getOwnPropertyDescriptor(connectionPrototype, property) === undefined) {
                    continue;
                }

                defineGetter(connectionPrototype, property, () => value);
            }
        }

        // ★ Медиа-запросы — САМОСТОЯТЕЛЬНЫЙ путь чтения устройства, и замер показал, что по нему
        // просачивалась настоящая машина: при заявленном настольном профиле '(pointer: fine)' и
        // '(hover: hover)' были ЛОЖНЫ (виртуальный дисплей не объявляет ни одного указательного
        // устройства: Xvfb отдаёт только XTEST-указатель, а Chromium его мышью не считает),
        // '(prefers-color-scheme: dark)' — истинно при заявленной светлой теме, а '(resolution:
        // 2dppx)' — ложно при заявленном devicePixelRatio 2. Последнее прямо противоречит само
        // себе и читается двумя строками подряд.
        //
        // Ключом запуска это не лечится: значения приходят из браузерного процесса и перетирают
        // '--blink-settings' — проверено замером. Поэтому подменяем ОДИН аксессор
        // 'MediaQueryList.prototype.matches', разбирая текст самого запроса: родными остаются и
        // 'matchMedia', и объект результата, и его свойство 'media'.
        const mediaQueryListPrototype = globalObject.MediaQueryList?.prototype;
        const originalMatchMedia = typeof globalObject.matchMedia === 'function'
            ? globalObject.matchMedia.bind(globalObject)
            : null;

        if (mediaQueryListPrototype && originalMatchMedia !== null) {
            const coarsePointer = context.isMobile === true || context.hasTouch === true;
            const declaredColorScheme = typeof context.colorScheme === 'string' ? context.colorScheme.toLowerCase() : null;
            const reducedMotion = context.reducedMotion === true;
            const declaredRatio = typeof context.deviceScaleFactor === 'number' && context.deviceScaleFactor > 0
                ? context.deviceScaleFactor
                : null;

            // Плотность запроса приводится к dppx: одна и та же величина пишется четырьмя
            // единицами, и сравнивать их между собой можно только после приведения.
            const toDppx = (value: number, unit: string): number | null => {
                switch (unit) {
                    case 'dppx':
                    case 'x':
                        return value;
                    case 'dpi':
                        return value / 96;
                    case 'dpcm':
                        return value / 37.795275590551185;
                    default:
                        return null;
                }
            };

            const resolveFeatureOverride = (media: string): boolean | undefined => {
                const parsed = /^\(\s*([a-z-]+)\s*:\s*([^)]+?)\s*\)$/.exec(media.trim().toLowerCase());
                if (parsed === null) {
                    return undefined;
                }

                const feature = parsed[1] ?? '';
                const value = parsed[2] ?? '';

                switch (feature) {
                    case 'hover':
                    case 'any-hover':
                        return value === (coarsePointer ? 'none' : 'hover');
                    case 'pointer':
                    case 'any-pointer':
                        return value === (coarsePointer ? 'coarse' : 'fine');
                    case 'prefers-color-scheme':
                        return declaredColorScheme === null ? undefined : value === declaredColorScheme;
                    case 'prefers-reduced-motion':
                        return value === (reducedMotion ? 'reduce' : 'no-preference');
                    case 'resolution':
                    case 'min-resolution':
                    case 'max-resolution': {
                        if (declaredRatio === null) {
                            return undefined;
                        }

                        const amount = /^([\d.]+)(dppx|x|dpi|dpcm)$/.exec(value);
                        if (amount === null) {
                            return undefined;
                        }

                        const requested = toDppx(Number.parseFloat(amount[1] ?? ''), amount[2] ?? '');
                        if (requested === null || Number.isNaN(requested)) {
                            return undefined;
                        }

                        return feature === 'min-resolution'
                            ? declaredRatio >= requested
                            : (feature === 'max-resolution' ? declaredRatio <= requested : declaredRatio === requested);
                    }
                    case 'device-pixel-ratio':
                    case '-webkit-device-pixel-ratio':
                    case 'min-device-pixel-ratio':
                    case '-webkit-min-device-pixel-ratio':
                    case 'max-device-pixel-ratio':
                    case '-webkit-max-device-pixel-ratio': {
                        if (declaredRatio === null) {
                            return undefined;
                        }

                        const requested = Number.parseFloat(value);
                        if (Number.isNaN(requested)) {
                            return undefined;
                        }

                        return feature.indexOf('min-') >= 0
                            ? declaredRatio >= requested
                            : (feature.indexOf('max-') >= 0 ? declaredRatio <= requested : declaredRatio === requested);
                    }
                    default:
                        return undefined;
                }
            };

            // Составной запрос вида '(hover: hover) and (pointer: fine)' разбирается по частям:
            // известные признаки берём подменённые, неизвестные — у самого браузера. Иначе
            // одиночный и составной запрос об одном и том же отвечали бы по-разному, а это
            // расхождение заметнее любого отдельного признака. Списки через запятую и отрицания
            // не трогаем: их семантика сложнее, а встречаются они у сборщиков отпечатков редко.
            const resolveMediaOverride = (media: unknown): boolean | undefined => {
                if (typeof media !== 'string') {
                    return undefined;
                }

                const query = media.trim().toLowerCase();

                if (query.length === 0 || query.indexOf(',') >= 0 || query.indexOf('not ') === 0 || query.indexOf('only ') === 0) {
                    return undefined;
                }

                const parts = query.split(' and ');
                let overridden = false;
                let result = true;

                for (const part of parts) {
                    const override = resolveFeatureOverride(part);

                    if (override !== undefined) {
                        overridden = true;
                        result = result && override;
                        continue;
                    }

                    try {
                        result = result && originalMatchMedia(part).matches;
                    } catch {
                        return undefined;
                    }
                }

                return overridden ? result : undefined;
            };

            // Ставим подмену ТОЛЬКО если окружение и правда отвечает не так, как заявлено: на
            // настоящем рабочем столе с верной темой подменять нечего, а лишняя обёртка — лишняя
            // поверхность.
            const disagrees = [
                '(pointer: fine)',
                '(hover: hover)',
                '(prefers-color-scheme: dark)',
                '(prefers-color-scheme: light)',
                '(resolution: ' + String(declaredRatio ?? 1) + 'dppx)',
            ].some((query) => {
                const desired = resolveMediaOverride(query);

                try {
                    return desired !== undefined && originalMatchMedia(query).matches !== desired;
                } catch {
                    return false;
                }
            });

            if (disagrees) {
                try {
                    const descriptor = Object.getOwnPropertyDescriptor(mediaQueryListPrototype, 'matches');
                    const originalMatches = descriptor?.get;

                    if (typeof originalMatches === 'function') {
                        Object.defineProperty(mediaQueryListPrototype, 'matches', {
                            configurable: true,
                            enumerable: descriptor?.enumerable ?? true,
                            get: markNative(
                                Object.getOwnPropertyDescriptor(
                                    {
                                        get matches() {
                                            const self: any = this;
                                            const override = resolveMediaOverride(self?.media);
                                            return override === undefined ? originalMatches.call(self) : override;
                                        },
                                    },
                                    'matches')?.get,
                                originalMatches,
                                'get matches',
                                0),
                        });
                    }
                } catch {
                    // Неподменяемый прототип — остальные поверхности всё равно должны встать.
                }
            }
        }

        // Часовой пояс. Подменяем и `resolvedOptions().timeZone`, и конструктор форматтера: сайту
        // достаточно построить формат без явного timeZone, чтобы получить НАСТОЯЩИЙ пояс машины.
        //
        // ★ Пояс процессу задаёт переменная окружения TZ при запуске браузера, и в штатном случае
        // Intl отдаёт заявленный пояс САМ — нативно и согласованно во всех путях сразу, включая
        // Date, форматирование и воркеры. Тогда обёртка не даёт ничего, а стоит двух заметных
        // поверхностей: подменённого 'resolvedOptions' и подменённого конструктора форматтера.
        // Поэтому ставим её ТОЛЬКО при расхождении — то есть когда пояс сменили уже после запуска.
        const timezone = context.timezone;
        const nativeTimezone = ((): string | null => {
            try {
                return Intl.DateTimeFormat().resolvedOptions().timeZone;
            } catch {
                return null;
            }
        })();

        if (typeof timezone === 'string' && timezone.length > 0 && timezone !== nativeTimezone) {
            const OriginalDateTimeFormat: any = Intl.DateTimeFormat;
            const originalResolvedOptions = OriginalDateTimeFormat.prototype.resolvedOptions;

            try {
                Object.defineProperty(OriginalDateTimeFormat.prototype, 'resolvedOptions', {
                    configurable: true,
                    writable: true,
                    value: asNativeMethod('resolvedOptions', originalResolvedOptions, function (this: unknown) {
                        const result = originalResolvedOptions.call(this);
                        if (result === null || typeof result !== 'object') {
                            return result;
                        }

                        // Локаль форматтера идёт оттуда же, откуда navigator.language: процесс
                        // запущен со своей, и resolvedOptions().locale выдавал именно её —
                        // прямое противоречие с заявленным языком страницы.
                        const preferredLocale = (context.languages ?? [])[0];

                        return preferredLocale
                            ? { ...result, timeZone: timezone, locale: preferredLocale }
                            : { ...result, timeZone: timezone };
                    }),
                });
            } catch {
                // Неподменяемый прототип — остальные поверхности всё равно должны встать.
            }

            const PatchedDateTimeFormat: any = function (this: unknown, locales?: unknown, options?: any) {
                const nextOptions = options === null || options === undefined ? {} : { ...options };
                if (nextOptions.timeZone === undefined) {
                    nextOptions.timeZone = timezone;
                }

                // Локаль по умолчанию — заявленная, иначе форматтер печатал бы японский формат,
                // сам же рапортуя resolvedOptions().locale = 'en-US'.
                const nextLocales = locales ?? (context.languages ?? [])[0];

                return new.target !== undefined
                    ? Reflect.construct(OriginalDateTimeFormat, [nextLocales, nextOptions], new.target)
                    : new OriginalDateTimeFormat(nextLocales, nextOptions);
            };

            try {
                Object.setPrototypeOf(PatchedDateTimeFormat, OriginalDateTimeFormat);
                PatchedDateTimeFormat.prototype = OriginalDateTimeFormat.prototype;
                Object.defineProperty(Intl, 'DateTimeFormat', {
                    configurable: true,
                    writable: true,
                    value: markNative(PatchedDateTimeFormat, OriginalDateTimeFormat, 'DateTimeFormat', OriginalDateTimeFormat.length),
                });
            } catch {
                // То же самое: частичная подмена лучше, чем срыв всей установки.
            }

            // ★ Date.prototype.getTimezoneOffset ЖИВЁТ ОТДЕЛЬНО ОТ Intl и подмены выше не видит.
            // Пока он отдавал смещение сервера, страница получала прямое противоречие: Intl
            // называет один пояс, Date показывает смещение другого. Проверяется одной строкой и
            // потому обесценивает всю подмену пояса.
            //
            // Смещение вычисляется через сам Intl в заявленном поясе, поэтому остаётся верным и
            // при переходе на летнее время, а не берётся постоянной.
            try {
                const OriginalGetTimezoneOffset = Date.prototype.getTimezoneOffset;
                const offsetFormatter = new Intl.DateTimeFormat('en-US', {
                    timeZone: timezone,
                    hour12: false,
                    year: 'numeric',
                    month: '2-digit',
                    day: '2-digit',
                    hour: '2-digit',
                    minute: '2-digit',
                    second: '2-digit',
                });

                Object.defineProperty(Date.prototype, 'getTimezoneOffset', {
                    configurable: true,
                    writable: true,
                    value: asNativeMethod('getTimezoneOffset', OriginalGetTimezoneOffset, function (this: Date) {
                        try {
                            const parts: any = {};
                            for (const part of offsetFormatter.formatToParts(this)) {
                                if (part.type !== 'literal') parts[part.type] = part.value;
                            }

                            const asUtc = Date.UTC(
                                Number(parts.year),
                                Number(parts.month) - 1,
                                Number(parts.day),
                                Number(parts.hour) % 24,
                                Number(parts.minute),
                                Number(parts.second),
                            );

                            // Знак как в спецификации: западнее UTC смещение положительно.
                            return Math.round((this.getTime() - asUtc) / 60000);
                        } catch {
                            return OriginalGetTimezoneOffset.call(this);
                        }
                    }),
                });
            } catch {
                // Прототип Date неподменяем — остальные поверхности всё равно должны встать.
            }

            // ★ Текстовые представления даты живут ОТДЕЛЬНО от смещения: toString/toTimeString
            // печатают пояс, заданный процессу переменной TZ при запуске. Замер: при заявленном
            // America/Los_Angeles и уже верном getTimezoneOffset()=420 строка давала
            // «GMT+0900 (日本標準時)» — то есть пояс запуска, прямо противоречащий числу.
            try {
                const OriginalToString = Date.prototype.toString;
                const OriginalToTimeString = Date.prototype.toTimeString;
                const OriginalToDateString = Date.prototype.toDateString;

                // Строка вида «GMT-0700 (Pacific Daylight Time)» собирается из подменённого
                // смещения и длинного имени пояса, которое выдаёт сам Intl в этом поясе.
                const formatZoneSuffix = (date: Date): string => {
                    const offsetMinutes = date.getTimezoneOffset();
                    const sign = offsetMinutes > 0 ? '-' : '+';
                    const absolute = Math.abs(offsetMinutes);
                    const hours = String(Math.floor(absolute / 60)).padStart(2, '0');
                    const minutes = String(absolute % 60).padStart(2, '0');

                    let longName = '';
                    try {
                        const named = new Intl.DateTimeFormat('en-US', { timeZone: timezone, timeZoneName: 'long' })
                            .formatToParts(date)
                            .find((part) => part.type === 'timeZoneName');
                        longName = named ? named.value : '';
                    } catch {
                        longName = '';
                    }

                    return 'GMT' + sign + hours + minutes + (longName ? ' (' + longName + ')' : '');
                };

                // Дата и время печатаются в заявленном поясе через en-US-форматтер, иначе сами
                // цифры остались бы от пояса процесса.
                const formatLocalParts = (date: Date): { dateText: string; timeText: string } | null => {
                    try {
                        const parts: any = {};
                        for (const part of new Intl.DateTimeFormat('en-US', {
                            timeZone: timezone,
                            hour12: false,
                            weekday: 'short',
                            month: 'short',
                            day: '2-digit',
                            year: 'numeric',
                            hour: '2-digit',
                            minute: '2-digit',
                            second: '2-digit',
                        }).formatToParts(date)) {
                            if (part.type !== 'literal') parts[part.type] = part.value;
                        }

                        return {
                            dateText: parts.weekday + ' ' + parts.month + ' ' + parts.day + ' ' + parts.year,
                            timeText: (parts.hour === '24' ? '00' : parts.hour) + ':' + parts.minute + ':' + parts.second,
                        };
                    } catch {
                        return null;
                    }
                };

                Object.defineProperty(Date.prototype, 'toString', {
                    configurable: true,
                    writable: true,
                    value: asNativeMethod('toString', OriginalToString, function (this: Date) {
                        const local = formatLocalParts(this);
                        return local === null
                            ? OriginalToString.call(this)
                            : local.dateText + ' ' + local.timeText + ' ' + formatZoneSuffix(this);
                    }),
                });

                Object.defineProperty(Date.prototype, 'toTimeString', {
                    configurable: true,
                    writable: true,
                    value: asNativeMethod('toTimeString', OriginalToTimeString, function (this: Date) {
                        const local = formatLocalParts(this);
                        return local === null
                            ? OriginalToTimeString.call(this)
                            : local.timeText + ' ' + formatZoneSuffix(this);
                    }),
                });

                Object.defineProperty(Date.prototype, 'toDateString', {
                    configurable: true,
                    writable: true,
                    value: asNativeMethod('toDateString', OriginalToDateString, function (this: Date) {
                        const local = formatLocalParts(this);
                        return local === null ? OriginalToDateString.call(this) : local.dateText;
                    }),
                });
                // ★ toLocale* — ОТДЕЛЬНАЯ поверхность, и без неё подмена сама себе противоречит:
                // замер показал toString() «05:05 GMT-0700» и toLocaleString() «21:05» на ОДНОМ
                // объекте Date — разрыв в 16 часов, видимый одной строкой.
                const preferredLocale = (context.languages ?? [])[0];
                const localeDefaults = { timeZone: timezone };

                const patchLocaleMethod = (name: 'toLocaleString' | 'toLocaleDateString' | 'toLocaleTimeString') => {
                    const original = (Date.prototype as any)[name];

                    Object.defineProperty(Date.prototype, name, {
                        configurable: true,
                        writable: true,
                        value: asNativeMethod(name, original, function (this: Date, locales?: unknown, options?: any) {
                            const nextOptions = options === null || options === undefined
                                ? { ...localeDefaults }
                                : { ...localeDefaults, ...options };

                            // Явно переданный пояс уважаем: вызывающий код знает, чего просит.
                            if (options && options.timeZone !== undefined) nextOptions.timeZone = options.timeZone;

                            return original.call(this, locales ?? preferredLocale, nextOptions);
                        }),
                    });
                };

                patchLocaleMethod('toLocaleString');
                patchLocaleMethod('toLocaleDateString');
                patchLocaleMethod('toLocaleTimeString');
            } catch {
                // Те же соображения: частичная подмена лучше срыва всей установки.
            }

            // Локаль по умолчанию у соседних конструкторов Intl берётся из процесса, а не из
            // navigator.language: NumberFormat и Collator рапортовали «ja» при заявленном en-US.
            try {
                const preferredLocale = (context.languages ?? [])[0];
                if (preferredLocale) {
                    for (const name of ['NumberFormat', 'Collator'] as const) {
                        const Original: any = (Intl as any)[name];
                        if (typeof Original !== 'function') continue;

                        const Patched: any = function (this: unknown, locales?: unknown, options?: unknown) {
                            return new.target !== undefined
                                ? Reflect.construct(Original, [locales ?? preferredLocale, options], new.target)
                                : Original(locales ?? preferredLocale, options);
                        };

                        Object.setPrototypeOf(Patched, Original);
                        Patched.prototype = Original.prototype;
                        Object.defineProperty(Intl, name, {
                            configurable: true,
                            writable: true,
                            value: markNative(Patched, Original, name, Original.length),
                        });
                    }
                }
            } catch {
                // Как и выше: неподменяемый Intl не должен рвать остальную установку.
            }
        }

        // Геолокация. Настоящие координаты машины противоречат и заявленному поясу, и прокси.
        const geolocation = context.geolocation;
        if (geolocation !== undefined) {
            const coords = {
                latitude: geolocation.latitude,
                longitude: geolocation.longitude,
                accuracy: geolocation.accuracy ?? 100,
                altitude: null,
                altitudeAccuracy: null,
                heading: null,
                speed: null,
            };

            const position = {
                coords,
                timestamp: Date.now(),
                toJSON() {
                    return { coords, timestamp: Date.now() };
                },
            };

            const fakeGeolocation = {
                getCurrentPosition(onSuccess: (value: unknown) => void) {
                    if (typeof onSuccess === 'function') {
                        onSuccess(position);
                    }
                },
                watchPosition(onSuccess: (value: unknown) => void) {
                    if (typeof onSuccess === 'function') {
                        onSuccess(position);
                    }

                    return 1;
                },
                clearWatch() {
                    // Наблюдение одноразовое: снимать нечего.
                },
            };

            defineNavigatorGetter('geolocation', () => fakeGeolocation);
        }

        // ОС-ЗАВИСИМЫЕ ПОВЕРХНОСТИ, выводимые из заявленной платформы.
        //
        // Это НЕ косметика: замер показал, что при заявленном Windows у нас было ноль голосов
        // синтеза речи и доступная область экрана, равная полной. Настоящая Windows-машина всегда
        // отдаёт непустой список голосов (Microsoft David/Zira ставятся с системой), а панель задач
        // всегда уменьшает `availHeight`. Каждого из этих признаков по отдельности достаточно,
        // чтобы заявление чужой ОС не сошлось с наблюдаемым окружением.
        // Рубильник сравнительного замера: пустая строка отключает ОС-зависимые подмены целиком,
        // оставляя строку агента, платформу и клиентские подсказки нетронутыми.
        const declaredOs = (context as { suppressOsSurfaces?: boolean }).suppressOsSurfaces === true
            ? ''
            : (clientHints?.platform ?? '');

        // ★ ЗАЯВЛЕННЫЙ ДВИЖОК. Профиль iPhone/iPad объявляет Safari, а исполняет всё Chromium — и
        // замер это показывал прямо: 'navigator.vendor' отдавал 'Google Inc.', на месте были
        // 'navigator.userAgentData' и 'navigator.deviceMemory', существовал объект 'window.chrome'.
        // Ни одного из этих свойств у настоящего Safari НЕТ вовсе, а строка агента заявляла именно
        // его. Cloudflare отвечал на такой профиль error-callback 600010 в 6 случаях из 6.
        //
        // Поэтому при заявленном Safari поверхности, которых у него не существует, УБИРАЮТСЯ.
        // Отсутствие здесь — не подделка, а правда о заявленном браузере.
        // Объявляет свойство навигатора, которого в текущем браузере не существует: на прототипе,
        // как и все остальные, чтобы список собственных свойств объекта остался пустым.
        const defineMissingNavigatorProperty = (property: string, value: string): void => {
            try {
                const target = globalObject.Navigator?.prototype ?? navigatorObject;
                if (Object.getOwnPropertyDescriptor(target, property) !== undefined) {
                    defineNavigatorGetter(property, () => value);
                    return;
                }

                Object.defineProperty(target, property, {
                    configurable: true,
                    enumerable: true,
                    get: asNativeGetter(property, undefined, () => value),
                });
            } catch {
                // Неподменяемый навигатор — остальные поверхности всё равно должны встать.
            }
        };

        const declaredUserAgent = typeof context.userAgent === 'string' ? context.userAgent : '';

        // Любой iOS/iPadOS — это WebKit: других движков там не бывает, включая Chrome (CriOS) и
        // Firefox (FxiOS). Поэтому признак движка выводится из ОС, а не только из имени браузера.
        const declaresWebKit = declaredUserAgent.indexOf('iPhone') >= 0
            || declaredUserAgent.indexOf('iPad') >= 0
            || declaredUserAgent.indexOf('iPod') >= 0
            || (declaredUserAgent.indexOf('Safari/') >= 0
                && declaredUserAgent.indexOf('Version/') >= 0
                && declaredUserAgent.indexOf('Chrome/') < 0
                && declaredUserAgent.indexOf('Chromium/') < 0);
        const declaresSafari = declaresWebKit;

        // ★ Заявленный Gecko. Firefox — единственный движок, кроме WebKit, у которого нет
        // клиентских подсказок и хромовых поверхностей: профиль «Firefox» на браузере Chromium
        // обязан выглядеть так же, как настоящий Firefox, иначе строка агента противоречит всему
        // остальному. Проверка «Firefox/ без Chrome/» отделяет его от хромовых сборок, которые
        // держат в строке 'like Gecko'.
        const declaresGecko = declaredUserAgent.indexOf('Firefox/') >= 0
            && declaredUserAgent.indexOf('Chrome/') < 0
            && declaredUserAgent.indexOf('Chromium/') < 0;

        // ★ МОБИЛЬНЫЕ ПОВЕРХНОСТИ. У браузера на телефоне НЕТ плагинов вовсе: и Chrome на Android,
        // и Safari на iOS отдают пустой 'navigator.plugins'. Замер показывал пять настольных
        // записей PDF-просмотрщика на заявленном телефоне — признак, читаемый одним свойством.
        if (context.isMobile === true) {
            const emptyList = (name: string): unknown => {
                const listConstructor = globalObject[name];
                const list = listConstructor?.prototype ? Object.create(listConstructor.prototype) : [];

                try {
                    Object.defineProperty(list, 'length', { configurable: true, get: () => 0 });
                    list.item = asNativeMethod('item', listConstructor?.prototype?.item, () => null);
                    list.namedItem = asNativeMethod('namedItem', listConstructor?.prototype?.namedItem, () => null);
                    list.refresh = asNativeMethod('refresh', listConstructor?.prototype?.refresh, () => undefined);
                } catch {
                    // Неподменяемый список — пустой массив тоже лучше настольного набора.
                }

                return list;
            };

            const emptyPlugins = emptyList('PluginArray');
            const emptyMimeTypes = emptyList('MimeTypeArray');

            defineNavigatorGetter('plugins', () => emptyPlugins);
            defineNavigatorGetter('mimeTypes', () => emptyMimeTypes);
        }

        if (declaresSafari || declaresGecko) {
            // Safari на iOS объявляет 'navigator.standalone' (признак запуска с домашнего экрана);
            // у Chromium такого свойства нет вовсе, и его отсутствие противоречит строке агента.
            if (declaresSafari) {
                try {
                        // На ПРОТОТИПЕ, как и все атрибуты навигатора: собственное свойство прямо на
                        // объекте видно обычным getOwnPropertyNames, а у настоящего браузера этот
                        // список пуст.
                        const standaloneTarget = globalObject.Navigator?.prototype ?? navigatorObject;

                        if (Object.getOwnPropertyDescriptor(standaloneTarget, 'standalone') === undefined) {
                            Object.defineProperty(standaloneTarget, 'standalone', {
                                configurable: true,
                                enumerable: true,
                                get: asNativeGetter('standalone', undefined, () => false),
                            });
                        }
                    } catch {
                        // Неподменяемый навигатор — остальные поверхности всё равно должны встать.
                    }

                    // Вендор у Safari свой; у Chromium — 'Google Inc.'.
                    defineNavigatorGetter('vendor', () => 'Apple Computer, Inc.');
                }

                if (declaresGecko) {
                    // Firefox отдаёт пустой вендор и собственный productSub; 'oscpu' и 'buildID' —
                    // свойства, которых у Chromium нет вовсе, и их отсутствие противоречит строке
                    // агента так же, как лишний хромовый объект.
                    defineNavigatorGetter('vendor', () => '');
                    defineNavigatorGetter('productSub', () => '20100101');

                    // 'buildID' и 'oscpu' у Chromium отсутствуют вовсе, поэтому их нужно ОБЪЯВИТЬ,
                    // а не переопределить: помощник выше правит только существующие свойства.
                    defineMissingNavigatorProperty('buildID', '20181001000000');

                    // 'oscpu' обязан совпадать с заявленной ОС: замер на живом Firefox показывал
                    // 'Linux x86_64' при заявленном Win32.
                    const declaredOsCpu = declaredUserAgent.indexOf('Windows NT 10.0; Win64; x64') >= 0
                        ? 'Windows NT 10.0; Win64; x64'
                        : declaredUserAgent.indexOf('Macintosh') >= 0
                            ? 'Intel Mac OS X 10.15'
                            : declaredUserAgent.indexOf('Linux') >= 0
                                ? 'Linux x86_64'
                                : undefined;

                    if (declaredOsCpu !== undefined)
                        defineMissingNavigatorProperty('oscpu', declaredOsCpu);

                    // Координаты области содержимого на экране: у Firefox это обычные числа, и их
                    // отсутствие при заявленной строке агента Firefox противоречит ей само по себе.
                    // Значения берутся от положения окна, чтобы не разойтись со 'screenX'.
                    for (const [property, source] of [['mozInnerScreenX', 'screenX'], ['mozInnerScreenY', 'screenY']] as const) {
                        try {
                            if (!(property in globalObject)) {
                                Object.defineProperty(globalObject, property, {
                                    configurable: true,
                                    enumerable: true,
                                    get: asNativeGetter(property, undefined, () => Number(globalObject[source]) || 0),
                                });
                            }
                        } catch {
                            // Неподменяемая поверхность — остальные всё равно должны встать.
                        }
                    }
                }

                // Свойства, которых у Safari не бывает. Убираем их С ПРОТОТИПА: собственное свойство на
                // самом навигаторе (в том числе удалённое) наблюдаемо описателем.
                // Свойства навигатора, которых у WebKit НЕТ вовсе. Часть — интерфейсы к железу,
                // которые Apple намеренно не реализует (usb/serial/hid/bluetooth), часть — чисто
                // хромовые (userAgentData, deviceMemory, connection).
                //
                // ★ Список выведен ЗАМЕРОМ: перечислением 'Navigator.prototype' на профиле iPhone.
                // Крупнейший кластер — Protected Audience (аукцион рекламы): тринадцать имён, которых
                // нет ни у одного браузера, кроме Chromium. Рядом — WebXR, FedCM, Web MIDI, батарея,
                // корзины хранилища и оба 'webkit*Storage' (префикс обманчив: это чисто хромовые API).
                const chromiumOnly = [
                    'userAgentData',
                    'deviceMemory',
                    'connection',
                    'globalPrivacyControl',
                    'usb',
                    'serial',
                    'hid',
                    'bluetooth',
                    'presentation',
                    'keyboard',
                    'ink',
                    'windowControlsOverlay',
                    'virtualKeyboard',
                    'managed',
                    'scheduling',
                    'adAuctionComponents',
                    'canLoadAdAuctionFencedFrame',
                    'clearOriginJoinedAdInterestGroups',
                    'createAuctionNonce',
                    'deprecatedReplaceInURN',
                    'deprecatedRunAdAuctionEnforcesKAnonymity',
                    'deprecatedURNToURL',
                    'getInterestGroupAdAuctionData',
                    'joinAdInterestGroup',
                    'leaveAdInterestGroup',
                    'protectedAudience',
                    'runAdAuction',
                    'updateAdInterestGroups',
                    'getBattery',
                    'getInstalledRelatedApps',
                    'devicePosture',
                    'cpuPerformance',
                    'login',
                    'storageBuckets',
                    'userActivation',
                    'webkitPersistentStorage',
                    'webkitTemporaryStorage',
                    'xr',
                ];

                // У Firefox эти поверхности ЕСТЬ, поэтому удаляются только при заявленном Safari:
                // глобальный контроль приватности, Web MIDI, вибрация и обработчики протоколов.
                if (declaresSafari) {
                    chromiumOnly.push(
                        'globalPrivacyControl',
                        'requestMIDIAccess',
                        'gpu',
                        'vibrate',
                        'registerProtocolHandler',
                        'unregisterProtocolHandler');
                }
                const navigatorPrototype = globalObject.Navigator?.prototype;

                for (const property of chromiumOnly) {
                    try {
                        if (navigatorPrototype && Object.getOwnPropertyDescriptor(navigatorPrototype, property) !== undefined) {
                            delete navigatorPrototype[property];
                        }

                        if (Object.getOwnPropertyDescriptor(navigatorObject, property) !== undefined) {
                            delete navigatorObject[property];
                        }
                    } catch {
                        // Неудаляемое свойство пропускаем: остальные всё равно должны исчезнуть.
                    }
                }

                // Объект 'chrome' — самый заметный признак движка: у Safari его нет.
                //
                // ★ Вместе с ним убираются и остальные хромовые глобальные объекты, и — главное —
                // СЛЕДЫ ДВИЖКА V8. Проверка на движок стоит одну строку: 'Error.stackTraceLimit' и
                // 'Error.captureStackTrace' существуют только в V8, 'Intl.v8BreakIterator' — только
                // в нём же, а диалоги выбора файлов ('showOpenFilePicker') Apple не реализует.
                // Заявлять iOS и держать их на месте — противоречие уровня движка, а не свойства.
                const chromiumOnlyGlobals = [
                    'chrome',
                    'showOpenFilePicker',
                    'showSaveFilePicker',
                    'showDirectoryPicker',
                    'EyeDropper',
                    'IdleDetector',
                    'BeforeInstallPromptEvent',
                    'LaunchQueue',
                    'USB',
                    'Serial',
                    'HID',
                    'Bluetooth',
                    // Перечисление 'window' на профиле iPhone: всё нижеследующее существует только в
                    // Chromium. Навигационный API, хранилище cookie, Trusted Types, планировщик задач,
                    // датчик нагрузки, выбор шрифтов, картинка-в-картинке для документа, изолированные
                    // фреймы и обработчики событий, которых у WebKit нет вовсе.
                    'navigation',
                    'cookieStore',
                    'sharedStorage',
                    // ★ 'trustedTypes' НЕ удаляем, хотя у WebKit его нет.
                    //
                    // Политику Trusted Types документу задаёт CSP, и Chromium ТРЕБУЕТ её независимо от
                    // того, объявлен ли API. Убрав API, мы оставляли требование без исполнителя: во
                    // фрейме проверки Cloudflare это давало EvalError и отказ присвоения innerHTML —
                    // её скрипт умирал, переставал отвечать на сторожевой пинг, и виджет объявлял
                    // 300030. Сокрытие одного признака ломало исполнение страницы целиком.
                    'scheduler',
                    'PressureObserver',
                    'queryLocalFonts',
                    'documentPictureInPicture',
                    'launchQueue',
                    'fence',
                    'fetchLater',
                    'getScreenDetails',
                    'credentialless',
                    'crashReport',
                    'offscreenBuffering',
                    'onappinstalled',
                    'onbeforeinstallprompt',
                    'onbeforexrselect',
                    'oncontentvisibilityautostatechange',
                    'oncommand',
                    'onpagereveal',
                    'onpageswap',
                    'onpointerrawupdate',
                    'NavigateEvent',
                    'Navigation',
                    'CookieStore',
                    'CookieChangeEvent',
                    'Scheduler',
                    'TaskController',
                    'TaskSignal',
                    'TaskPriorityChangeEvent',
                    'SharedStorage',
                    'FencedFrameConfig',
                    'DocumentPictureInPicture',
                    'FileSystemHandle',
                    'FileSystemFileHandle',
                    'FileSystemDirectoryHandle',
                    'FileSystemWritableFileStream',
                    'NavigatorUAData',
                    'BatteryManager',
                    'XRSystem',
                    'XRSession',
                    'MIDIAccess',
                    'MIDIInput',
                    'MIDIOutput',
                    'MIDIMessageEvent',
                    'DevicePosture',
                    'StorageBucketManager',
                    'VirtualKeyboard',
                    'WindowControlsOverlay',
                    'Ink',
                    'Presentation',
                    'PresentationRequest',
                ];

                // Конструкторы интерфейсов лежат прямо на глобальном объекте, а вот атрибуты и методы
                // самого интерфейса Window — на 'Window.prototype'. Проверка одного лишь собственного
                // свойства пропускала вторую половину: замер показывал 'scheduler', 'queryLocalFonts'
                // и 'documentPictureInPicture' живыми на профиле iPhone. Поэтому идём по всей цепочке.
                for (const property of chromiumOnlyGlobals) {
                    try {
                        for (let holder: any = globalObject; holder !== null && holder !== undefined; holder = Object.getPrototypeOf(holder)) {
                            if (Object.getOwnPropertyDescriptor(holder, property) !== undefined) {
                                delete holder[property];
                            }
                        }
                    } catch {
                        // Неудаляемый глобальный объект пропускаем: остальные всё равно исчезнут.
                    }
                }

                // ★ 'stackTraceLimit' НЕ удаляем, хотя у WebKit его нет.
                //
                // Замер показал цену: без него V8 перестаёт собирать стек вообще, и 'new Error().stack'
                // становится ПУСТЫМ. Пустой стек не бывает ни у одного настоящего браузера, то есть
                // сокрытие одного признака создавало признак заметнее исходного. Остальные точки входа
                // V8 в стек убираются: они на сбор не влияют.
                try {
                    delete (Error as any).captureStackTrace;
                } catch {
                    // Свойства V8 неудаляемыми не бывают, но падать из-за них нельзя.
                }

                try {
                    delete (Intl as any).v8BreakIterator;
                } catch {
                    // То же самое.
                }

                try {
                    delete (Error as any).prepareStackTrace;
                } catch {
                    // Свойства V8 неудаляемыми не бывают, но падать из-за них нельзя.
                }

            // Для заявленного Gecko этих поверхностей быть НЕ должно: у Firefox их нет,
            // поэтому добавляются они только при заявленном Safari.
            if (declaresSafari) {
                // ★ И наоборот: поверхности, которые есть ТОЛЬКО у WebKit. Их отсутствие при заявленном
                // iOS — такой же приговор, как лишний хромовый объект: 'GestureEvent' и события жестов
                // существуют исключительно в WebKit, а Apple Pay доступен любому сайту на iOS.
                // Проверка Cloudflare ветвится по строке агента, и на iOS-ветке она вправе на них
                // рассчитывать — код 600010 означает именно сорванное исполнение проверки.
                try {
                    if (globalObject.GestureEvent === undefined) {
                        const gestureEventHolder = {
                            GestureEvent: class GestureEvent extends globalObject.UIEvent {
                                constructor(type: string, init?: any) {
                                    super(type, init);
                                    Object.defineProperties(this, {
                                        scale: { configurable: true, enumerable: true, value: init?.scale ?? 1 },
                                        rotation: { configurable: true, enumerable: true, value: init?.rotation ?? 0 },
                                    });
                                }
                            },
                        };

                        Object.defineProperty(globalObject, 'GestureEvent', {
                            configurable: true,
                            writable: true,
                            value: gestureEventHolder.GestureEvent,
                        });
                    }

                    for (const handler of ['ongesturestart', 'ongesturechange', 'ongestureend']) {
                        if (!(handler in globalObject)) {
                            Object.defineProperty(globalObject, handler, {
                                configurable: true,
                                enumerable: true,
                                get: asNativeGetter(handler, undefined, () => null),
                                set: () => undefined,
                            });
                        }
                    }

                    // Apple Pay доступен ЛЮБОМУ сайту на устройстве Apple, и его отсутствие при
                    // заявленном iOS — такой же однозначный признак, как лишний хромовый объект.
                    // Достаточно самого интерфейса: проверки читают его наличие, а не проводят оплату.
                    if (globalObject.ApplePaySession === undefined) {
                        const applePayHolder = {
                            ApplePaySession: class ApplePaySession extends globalObject.EventTarget {
                                static canMakePayments(): boolean {
                                    return true;
                                }

                                static canMakePaymentsWithActiveCard(): Promise<boolean> {
                                    return Promise.resolve(false);
                                }

                                static supportsVersion(version: number): boolean {
                                    return version <= 14;
                                }
                            },
                        };

                        for (const [name, value] of [
                            ['STATUS_SUCCESS', 0],
                            ['STATUS_FAILURE', 1],
                            ['STATUS_INVALID_BILLING_POSTAL_ADDRESS', 2],
                            ['STATUS_INVALID_SHIPPING_POSTAL_ADDRESS', 3],
                            ['STATUS_INVALID_SHIPPING_CONTACT', 4],
                            ['STATUS_PIN_REQUIRED', 5],
                            ['STATUS_PIN_INCORRECT', 6],
                            ['STATUS_PIN_LOCKOUT', 7],
                        ] as [string, number][]) {
                            Object.defineProperty(applePayHolder.ApplePaySession, name, {
                                configurable: false,
                                enumerable: true,
                                value,
                            });
                        }

                        Object.defineProperty(globalObject, 'ApplePaySession', {
                            configurable: true,
                            writable: true,
                            value: applePayHolder.ApplePaySession,
                        });
                    }

                    // Методы преобразования координат существуют только в WebKit и живут прямо на окне.
                    for (const method of ['webkitConvertPointFromNodeToPage', 'webkitConvertPointFromPageToNode']) {
                        if (typeof globalObject[method] !== 'function') {
                            Object.defineProperty(globalObject, method, {
                                configurable: true,
                                writable: true,
                                value: asNativeMethod(method, undefined, function (this: unknown, node: any, point: any) {
                                    return point;
                                }),
                            });
                        }
                    }

                    // Ориентация экрана на телефоне: устаревшее, но на iOS живое свойство. Обратное
                    // тоже верно — на настольном Safari его нет, поэтому ставим только мобильным.
                    if (context.isMobile === true && globalObject.orientation === undefined) {
                        Object.defineProperty(globalObject, 'orientation', {
                            configurable: true,
                            enumerable: true,
                            get: asNativeGetter('orientation', undefined, () => 0),
                        });

                        Object.defineProperty(globalObject, 'onorientationchange', {
                            configurable: true,
                            enumerable: true,
                            get: asNativeGetter('onorientationchange', undefined, () => null),
                            set: () => undefined,
                        });
                    }
                } catch {
                    // Частичная установка лучше сорванной: остальные поверхности всё равно нужны.
                }
            }
        }

        // Рубильник отдельных ветвей ОС-подмен для бинарного поиска виновника отказа: список имён
        // приходит в контексте вкладки, гасится только названное. Общий `suppressOsSurfaces`
        // выключает слой целиком и разделить ветви не позволяет.
        const disabledSurfacesValue = (context as unknown as { disabledOsSurfaces?: unknown }).disabledOsSurfaces;
        const disabledSurfaces: string[] = Array.isArray(disabledSurfacesValue) ? disabledSurfacesValue : [];
        const surfaceEnabled = (name: string): boolean => !disabledSurfaces.includes(name);

        if ((declaredOs === 'Windows' || declaredOs === 'macOS') && surfaceEnabled('voices')) {
            const voiceNames = declaredOs === 'Windows'
                ? [
                    ['Microsoft David - English (United States)', 'en-US'],
                    ['Microsoft Zira - English (United States)', 'en-US'],
                    ['Microsoft Mark - English (United States)', 'en-US'],
                ]
                : [
                    ['Alex', 'en-US'],
                    ['Samantha', 'en-US'],
                    ['Daniel', 'en-GB'],
                ];

            // Голоса выдаются объектами с настоящим прототипом: обычный литерал не проходит
            // 'instanceof SpeechSynthesisVoice' и печатается как '[object Object]'.
            const voicePrototype = globalObject.SpeechSynthesisVoice?.prototype;
            const voices = voiceNames.map(([name, lang], index) => {
                const voice = voicePrototype ? Object.create(voicePrototype) : {};

                Object.defineProperties(voice, {
                    voiceURI: { configurable: true, enumerable: true, get: asNativeGetter('voiceURI', undefined, () => name) },
                    name: { configurable: true, enumerable: true, get: asNativeGetter('name', undefined, () => name) },
                    lang: { configurable: true, enumerable: true, get: asNativeGetter('lang', undefined, () => lang) },
                    localService: { configurable: true, enumerable: true, get: asNativeGetter('localService', undefined, () => true) },
                    default: { configurable: true, enumerable: true, get: asNativeGetter('default', undefined, () => index === 0) },
                });

                return voice;
            });

            const speech = globalObject.speechSynthesis;
            if (speech) {
                try {
                    const speechPrototype = globalObject.SpeechSynthesis?.prototype;
                    const originalGetVoices = speech.getVoices?.bind(speech);
                    // ★ Системные голоса ПОДМЕНЯЮТСЯ, а не уступают. Прежде подмена срабатывала
                    // только на пустом списке, но у Linux-машины он не пуст: замер при заявленном
                    // Windows показывал 'Google Deutsch|Google US English|…' — набор, которого на
                    // Windows не бывает, тогда как там всегда есть 'Microsoft David/Zira'.
                    // Чужие голоса — прямое указание на настоящую ОС, поэтому берём заявленные.
                    const patchedGetVoices = asNativeMethod('getVoices', speech.getVoices, function () {
                        return voices.slice();
                    });

                    // Метод объявлен на 'SpeechSynthesis.prototype'; собственное свойство прямо на
                    // объекте 'speechSynthesis' — тот же наблюдаемый след, что и у навигатора.
                    if (speechPrototype && typeof speechPrototype.getVoices === 'function') {
                        Object.defineProperty(speechPrototype, 'getVoices', {
                            configurable: true,
                            writable: true,
                            enumerable: Object.getOwnPropertyDescriptor(speechPrototype, 'getVoices')?.enumerable ?? true,
                            value: patchedGetVoices,
                        });
                    } else {
                        speech.getVoices = patchedGetVoices;
                    }
                } catch {
                    // Неподменяемый метод — остальные поверхности всё равно должны встать.
                }
            }
        }

        // ★ ШРИФТЫ ОС. Набор семейств жёстко привязан к системе, и сайт читает его измерением:
        // ширина строки в проверяемом шрифте сравнивается с шириной в базовом, и совпадение
        // означает, что шрифта нет и сработала подстановка. Замер при заявленном Windows давал
        // 0 из 5 windows-семейств при трёх linux-овых — расхождение уровня ОС, которое не
        // закрывается ни строкой агента, ни клиентскими подсказками.
        //
        // Подменяется САМО ИЗМЕРЕНИЕ: подсунуть в систему шрифты нельзя, а оба пути измерения —
        // 'measureText' и размер элемента — ведут к одному числу. Заявленным семействам даётся
        // устойчивое смещение от базовой ширины, поэтому они «существуют»; чужим системным,
        // которых на заявленной ОС быть не должно, — ровно базовая ширина, то есть «отсутствуют».
        if ((declaredOs === 'Windows' || declaredOs === 'macOS') && surfaceEnabled('fonts')) {
            // ★ Список — ПОЛНЫЙ штатный комплект заявленной ОС, а не десяток заметных имён.
            // Перечисление шрифтов проверяет 40–100 семейств разом, и отсутствие обязательных
            // выдаёт подмену вернее, чем присутствие подставных: Times New Roman и Georgia есть
            // даже на голой Windows, а Marlett — системный шрифт элементов окна, без него живой
            // Windows не бывает. Замер при 10 подменяемых семействах: 18 обязательных отсутствовали.
            const ownFamilies = declaredOs === 'Windows'
                ? [
                    'segoe ui', 'segoe ui semibold', 'segoe ui light', 'segoe ui black',
                    'segoe ui emoji', 'segoe ui symbol', 'segoe ui historic', 'segoe print',
                    'segoe script', 'segoe mdl2 assets', 'segoe fluent icons',
                    'calibri', 'cambria', 'cambria math', 'candara', 'consolas', 'constantia',
                    'corbel', 'sylfaen', 'tahoma', 'verdana', 'georgia', 'impact',
                    // Алиасы, которые Windows разрешает всегда: без них браузер сам подставлял
                    // системный шрифт, и ширина уезжала мимо подмены при отрицательном fonts.check.
                    'arial', 'helvetica', 'courier', 'times',
                    'times new roman', 'courier new', 'arial black', 'arial narrow',
                    'trebuchet ms', 'comic sans ms', 'palatino linotype', 'book antiqua',
                    'lucida console', 'lucida sans unicode', 'lucida sans', 'microsoft sans serif',
                    'ms sans serif', 'ms serif', 'ms shell dlg 2', 'ms gothic', 'ms pgothic',
                    'ms ui gothic', 'ms mincho', 'ms pmincho', 'simsun', 'nsimsun', 'simhei',
                    'microsoft yahei', 'microsoft jhenghei', 'malgun gothic', 'gulim', 'batang',
                    'meiryo', 'yu gothic', 'yu mincho', 'mingliu', 'pmingliu',
                    'marlett', 'webdings', 'wingdings', 'wingdings 2', 'wingdings 3', 'symbol',
                    'franklin gothic medium', 'gabriola', 'gadugi', 'ebrima', 'nirmala ui',
                    'leelawadee ui', 'mv boli', 'myanmar text', 'javanese text', 'mongolian baiti',
                    'sitka small', 'sitka text', 'sitka subheading', 'sitka heading',
                    'sitka display', 'sitka banner', 'bahnschrift', 'ink free', 'hololens mdl2 assets',
                ]
                : [
                    'helvetica neue', 'helvetica', 'lucida grande', 'geneva', 'menlo', 'monaco',
                    'optima', 'papyrus', 'sf pro text', 'sf pro display', 'sf mono', 'sf compact',
                    'american typewriter', 'andale mono', 'apple chancery', 'apple color emoji',
                    'apple sd gothic neo', 'apple symbols', 'avenir', 'avenir next',
                    'avenir next condensed', 'baskerville', 'big caslon', 'bodoni 72',
                    'bradley hand', 'brush script mt', 'chalkboard', 'chalkboard se', 'chalkduster',
                    'cochin', 'copperplate', 'courier', 'didot', 'futura', 'gill sans',
                    'herculanum', 'hoefler text', 'impact', 'marker felt', 'noteworthy',
                    'palatino', 'phosphate', 'rockwell', 'savoye let', 'signpainter',
                    'skia', 'snell roundhand', 'times', 'trattatello', 'zapfino',
                    'georgia', 'times new roman', 'courier new', 'verdana', 'trebuchet ms',
                    'comic sans ms', 'arial black', 'arial narrow', 'arial',
                ];

            // Семейства, которых на заявленной ОС не бывает: они обязаны выглядеть отсутствующими.
            // Список тоже полный: перечисление ищет линуксовые имена ровно так же, как виндовые,
            // и одна найденная 'DejaVu Sans' при заявленном Windows стоит всех подставленных.
            const linuxFamilies = [
                'dejavu sans', 'dejavu serif', 'dejavu sans mono', 'dejavu math tex gyre',
                'liberation sans', 'liberation serif', 'liberation mono', 'liberation sans narrow',
                'ubuntu', 'ubuntu mono', 'ubuntu condensed', 'cantarell', 'oxygen', 'oxygen-sans',
                'noto sans', 'noto serif', 'noto mono', 'noto color emoji', 'noto sans cjk jp',
                'noto sans cjk sc', 'noto sans symbols', 'fira sans', 'fira mono', 'fira code',
                'droid sans', 'droid serif', 'droid sans mono', 'freesans', 'freeserif', 'freemono',
                'nimbus sans', 'nimbus roman', 'nimbus mono ps', 'urw bookman', 'urw gothic',
                'century schoolbook l', 'bitstream vera sans', 'bitstream vera serif',
                'bitstream charter', 'c059', 'd050000l', 'p052', 'standard symbols ps', 'z003',
                'source code pro', 'jetbrains mono', 'cascadia code', 'hack', 'inconsolata',
                'carlito', 'caladea', 'croscore', 'arimo', 'tinos', 'cousine',
                'lohit devanagari', 'kacst art', 'mry_kacstqurn', 'padauk', 'abyssinica sil',
            ];

            const foreignFamilies = declaredOs === 'Windows'
                ? [...linuxFamilies, 'helvetica neue', 'lucida grande', 'menlo', 'monaco', 'geneva',
                    'sf pro text', 'sf pro display', 'sf mono', 'apple color emoji', 'apple sd gothic neo',
                    'avenir', 'avenir next', 'chalkboard', 'chalkduster', 'cochin', 'didot', 'futura',
                    'gill sans', 'hoefler text', 'marker felt', 'noteworthy', 'optima', 'papyrus',
                    'phosphate', 'skia', 'snell roundhand', 'zapfino']
                : [...linuxFamilies, 'segoe ui', 'segoe ui emoji', 'segoe ui symbol', 'segoe print',
                    'segoe script', 'calibri', 'cambria', 'candara', 'consolas', 'constantia', 'corbel',
                    'sylfaen', 'tahoma', 'marlett', 'webdings', 'wingdings', 'ms shell dlg 2',
                    'microsoft sans serif', 'ms gothic', 'simsun', 'microsoft yahei', 'malgun gothic',
                    'franklin gothic medium', 'gabriola', 'gadugi', 'ebrima', 'nirmala ui', 'bahnschrift'];

            // Обобщённые имена CSS: они есть на любой системе и подмене не подлежат.
            const genericFamilies = [
                'serif', 'sans-serif', 'monospace', 'cursive', 'fantasy', 'system-ui',
                'ui-serif', 'ui-sans-serif', 'ui-monospace', 'ui-rounded',
                'math', 'emoji', 'fangsong', 'caption', 'icon', 'menu', 'message-box',
                'small-caption', 'status-bar', '-webkit-body', 'inherit', 'initial', 'unset',
            ];

            // Первое семейство из списка CSS-шрифта: измеряют именно по нему, базовое идёт следом.
            //
            // ★ Имя берётся ЦЕЛИКОМ, а не последним словом. Прежний разбор резал по последнему
            // пробелу, и многословные семейства не совпадали со списком НИКОГДА: '48px "Segoe UI"'
            // давало 'ui', '"Times New Roman"' — 'roman', '"Liberation Sans"' — 'sans'. Подмена на
            // них не срабатывала вовсе, а Segoe UI отдавал ширину донора, совпадая с Arial до байта —
            // на живой Windows это разные шрифты. Кавычки снимаются до разбора: без них у
            // '48px "Segoe UI"' нет иного признака, где кончается размер и начинается имя.
            const firstFamily = (font: unknown): string => {
                const head = (String(font ?? '').split(',')[0] ?? '').trim();

                // Имя в кавычках — берём его содержимое как есть.
                const quoted = /["']([^"']+)["']\s*$/.exec(head);
                if (quoted?.[1] !== undefined) {
                    return quoted[1].trim().toLowerCase();
                }

                // Без кавычек имя идёт после размера — берём остаток за ПОСЛЕДНИМ размером: до него
                // могут стоять и числовая насыщенность ('400'), и другие дескрипторы ('small-caps 700').
                const sized = /^.*(?:^|\s)\d*\.?\d+(?:px|pt|em|rem|%|ex|ch|vh|vw|q|cm|mm|in|pc)(?:\s*\/\s*\S+)?\s+(.+)$/.exec(head);

                return (sized?.[1] ?? head).trim().toLowerCase();
            };

            // Смещение выводится из имени: у каждого семейства своя ширина, и одинаковая для всех
            // выдала бы подмену не хуже отсутствия шрифтов.
            const familyShift = (family: string): number => {
                let hash = 0;
                for (let index = 0; index < family.length; index++) {
                    hash = (hash * 31 + family.charCodeAt(index)) >>> 0;
                }

                return 1.03 + ((hash % 180) / 1000);
            };

            // ★ ИЗМЕРЕНИЕ НЕ ПОДМЕНЯЕТСЯ ВОВСЕ — ни в canvas, ни в раскладке.
            //
            // Источник истины о шрифтах ровно один: fontconfig профиля (PlatformFontConfiguration),
            // который отдаёт браузеру доноров под заявленными именами. Он подменяет ФАЙЛ, поэтому
            // все пути измерения — canvas, размер элемента, перечисление — меряют один и тот же
            // глиф и сходятся сами собой, как у настоящей машины.
            //
            // Подмена ширины в canvas этому прямо противоречила. Замер: `Calibri` и `Verdana`
            // раскладка рисует ОДНИМ файлом (оба падают на донора DejaVu), а canvas давал им
            // разную ширину по хешу имени. Расхождение двух путей на одном шрифте доходило до
            // 24 px, тогда как у родного профиля и у профиля своей ОС оно равно 0.008 px на всех
            // 18 проверенных семействах — то есть инвариант, ломать который дороже, чем прятать
            // ширину. Проверяется одним циклом: `measureText(f).width` против
            // `getBoundingClientRect().width` для того же CSS-шрифта.
            //
            // Если какого-то заявленного семейства недостаёт — лечится донором в fontconfig,
            // а не умножением на множитель поверх чужого глифа.

            // ★ 'document.fonts.check()' тоже НЕ подменяется — по той же причине.
            //
            // Подмена отвечала по списку имён, а раскладка рисовала по fontconfig, и они спорили
            // внутри одного документа: замер при заявленном Windows дал 'check("DejaVu Sans")'
            // = false, тогда как движок этим самым файлом строку и рисовал (ширина совпадала
            // с донорской), а 'check("ZzNoSuchFamily7331")' = true. Родной ответ согласован
            // с fontconfig по построению — он его и спрашивает.
            //
            // Семантику метода стоит помнить: настоящий Chrome отвечает 'true' почти на всё,
            // включая заведомо несуществующие имена, — проверяется готовность шрифта к отрисовке,
            // а не его наличие. Отрицательный ответ по умолчанию инвертировал бы API.

            // ★ Четвёртый путь — 'queryLocalFonts()'. Он отдаёт СПИСОК семейств машины целиком,
            // мимо любой подмены измерения: замер вернул 701 хостовое имя при заявленном Windows.
            // Подменять список нечем — postscriptName и style каждого шрифта нам неоткуда взять,
            // а правдоподобно выдумать их для сотни семейств нельзя. Поэтому API снимается: он
            // требует разрешения пользователя, в непривилегированном контексте недоступен и его
            // отсутствие — обычное состояние, а не признак.
            try {
                if ('queryLocalFonts' in globalObject) {
                    delete (globalObject as { queryLocalFonts?: unknown }).queryLocalFonts;
                }
            } catch {
                // Неудаляемое свойство — измерение всё равно подменено.
            }

            // ★ РАСКЛАДКА КЛАВИАТУРЫ. 'navigator.keyboard.getLayoutMap()' отдаёт соответствие
            // физических клавиш символам, и оно задано ОС, а не браузером. Замер при заявленном
            // Windows: 'IntlBackslash' отдавал '<' — так его печатают раскладки X11, тогда как на
            // US-Windows это '\'. Одна строка у сайта, и результат однозначно указывает на Linux.
            try {
                const keyboard: any = (globalObject.navigator as { keyboard?: unknown } | undefined)?.keyboard;
                const originalGetLayoutMap = keyboard?.getLayoutMap;

                if (typeof originalGetLayoutMap === 'function' && surfaceEnabled('keyboard')) {
                    const layoutOverrides: Record<string, string> = declaredOs === 'Windows'
                        ? { IntlBackslash: '\\', IntlRo: '\\', IntlYen: '\\' }
                        : { IntlBackslash: '§' };

                    const patchedGetLayoutMap = asNativeMethod('getLayoutMap', originalGetLayoutMap, async function (this: any) {
                        const layout: any = await originalGetLayoutMap.call(this);

                        try {
                            for (const [code, value] of Object.entries(layoutOverrides)) {
                                if (layout.has(code)) layout.set(code, value);
                            }
                        } catch {
                            // Неизменяемая карта — отдаём как есть.
                        }

                        return layout;
                    });

                    Object.defineProperty(Object.getPrototypeOf(keyboard) ?? keyboard, 'getLayoutMap', {
                        configurable: true,
                        writable: true,
                        enumerable: Object.getOwnPropertyDescriptor(Object.getPrototypeOf(keyboard) ?? keyboard, 'getLayoutMap')?.enumerable ?? true,
                        value: patchedGetLayoutMap,
                    });
                }
            } catch {
                // Недоступный Keyboard API — остальные поверхности всё равно подменены.
            }

            // ★ СИСТЕМНЫЕ ЦВЕТА CSS. Акцент рабочего стола виден странице через 'SelectedItem' и
            // родственные ключевые слова: замер при заявленном Windows давал '#3DAEE9' — акцент темы
            // KDE Breeze, тогда как на Windows по умолчанию '#0078D4'. Читается одним вызовом
            // getComputedStyle, сверяется с короткой таблицей и потому проверяется тривиально.
            try {
                const systemColors: Record<string, string> = !surfaceEnabled('colors')
                    ? {}
                    : declaredOs === 'Windows'
                    ? {
                        selecteditem: 'rgb(0, 120, 212)',
                        selecteditemtext: 'rgb(255, 255, 255)',
                        highlight: 'rgb(0, 120, 212)',
                        highlighttext: 'rgb(255, 255, 255)',
                        accentcolor: 'rgb(0, 120, 212)',
                        accentcolortext: 'rgb(255, 255, 255)',
                        buttonface: 'rgb(240, 240, 240)',
                        buttonborder: 'rgb(173, 173, 173)',
                    }
                    : {
                        selecteditem: 'rgb(0, 103, 244)',
                        selecteditemtext: 'rgb(255, 255, 255)',
                        highlight: 'rgb(0, 103, 244)',
                        highlighttext: 'rgb(255, 255, 255)',
                        accentcolor: 'rgb(0, 103, 244)',
                        accentcolortext: 'rgb(255, 255, 255)',
                    };

                const originalGetComputedStyle = globalObject.getComputedStyle;

                if (typeof originalGetComputedStyle === 'function') {
                    const patchedGetComputedStyle = asNativeMethod(
                        'getComputedStyle',
                        originalGetComputedStyle,
                        function (this: any, element: any, pseudoElement?: string) {
                            const style = originalGetComputedStyle.call(this ?? globalObject, element, pseudoElement);

                            try {
                                // Ключевое слово доезжает до вычисленного стиля как есть, поэтому
                                // подменяется РЕЗУЛЬТАТ чтения цвета, а не сам стиль целиком.
                                const declared = String(element?.style?.color ?? '').toLowerCase();
                                const replacement = systemColors[declared];

                                if (replacement !== undefined) {
                                    return new Proxy(style, {
                                        get: (target, property, receiver) => property === 'color'
                                            ? replacement
                                            : Reflect.get(target, property, receiver),
                                    });
                                }
                            } catch {
                                // Стиль недоступен — отдаём настоящий.
                            }

                            return style;
                        });

                    Object.defineProperty(globalObject, 'getComputedStyle', {
                        configurable: true,
                        writable: true,
                        enumerable: Object.getOwnPropertyDescriptor(globalObject, 'getComputedStyle')?.enumerable ?? true,
                        value: patchedGetComputedStyle,
                    });
                }
            } catch {
                // Неподменяемый getComputedStyle — остальные поверхности всё равно встали.
            }
        }

        // Доступная область экрана. Полное равенство avail и физического размера — почерк среды без
        // оболочки рабочего стола; на Windows край экрана занимает панель задач, на macOS — строка меню.
        //
        // Заявленные значения профиля важнее вычисленных: устройство знает свою доступную область
        // точнее, чем предположение о высоте панели.
        if (!displaySurfacesOff && screenObject && typeof screenWidth === 'number' && typeof screenHeight === 'number') {
            // Размер окна здесь не участвует: у настоящего устройства доступная область задана
            // оболочкой рабочего стола, а не тем, насколько широким вышло окно браузера.
            const reservedHeight = declaredOs === 'Windows' ? 40 : (declaredOs === 'macOS' ? 25 : 0);
            const resolveAvailWidth = (): number => Math.min(
                resolveScreenWidth(),
                declaredScreen?.availWidth ?? resolveScreenWidth());
            const resolveAvailHeight = (): number => Math.min(
                resolveScreenHeight(),
                declaredScreen?.availHeight ?? (resolveScreenHeight() - reservedHeight));

            // Доступная область объявляется ВСЕГДА, когда заявлен экран: без неё окно, которое
            // браузер расширил до своей нижней границы, оказывается шире доступной области —
            // невозможное для настоящего рабочего стола сочетание.
            {
                defineScreenGetter(screenObject, 'availWidth', resolveAvailWidth);
                defineScreenGetter(screenObject, 'availHeight', resolveAvailHeight);

                // Строка меню macOS занимает ВЕРХ экрана, поэтому доступная область там начинается
                // ниже нуля по вертикали; панель задач Windows по умолчанию снизу, и начало
                // остаётся нулевым. Нулевой availTop при уменьшенной availHeight внутренне
                // противоречив: место занято, но неизвестно где.
                //
                // Верх ограничен положением САМОГО окна: оконный менеджер виртуального дисплея
                // ставит окно в левый верхний угол и аргумент положения при запуске может не
                // соблюсти. Окно выше начала доступной области — противоречие, поэтому при
                // окне у нулевой границы недостающая высота считается занятой снизу (на macOS
                // там док), а не сверху.
                const resolveAvailTop = (): number => {
                    if (declaredOs !== 'macOS') {
                        return 0;
                    }

                    const windowTop = typeof globalObject.screenY === 'number' ? Math.max(0, globalObject.screenY) : 0;
                    return Math.min(Math.max(0, resolveScreenHeight() - resolveAvailHeight()), windowTop);
                };

                defineScreenGetter(screenObject, 'availTop', resolveAvailTop);
                defineScreenGetter(screenObject, 'availLeft', () => 0);
            }
        }

        // WebGL. Подменять надо И маскированные строки, И немаскированные (через расширение
        // WEBGL_debug_renderer_info): расхождение между ними само по себе выдаёт подделку.
        //
        // ★ Числовые пределы подменяются ТЕМ ЖЕ перехватом и обязательно ВМЕСТЕ со строками. Пара
        // «имя видеокарты — её пределы» известна и сверяется таблицей: заявленный Apple M1 Pro с
        // максимальным размером текстуры 8192 — карта, которой не бывает. Замер до этой правки
        // показывал ровно такую машину: строки подменялись здесь, а числа страница читала
        // настоящие, потому что ранний установщик знал только строковые параметры.
        // ★ Значения WebGL держатся в ЖИВОМ хранилище, общем для всех установок в этом документе.
        //
        // Замер по вкладкам показал так: браузер поднят с профилем Windows, вкладка объявляет
        // macOS — навигатор во вкладке правильный (MacIntel, 10 ядер), а видеокарта осталась
        // браузерной, 'NVIDIA GeForce RTX 3070' вместо 'Apple M1 Pro'. Причина: перехват
        // 'getParameter' ставит РАННИЙ скрипт с запечённым профилем браузера, а повторная
        // установка для вкладки застаёт его уже стоящим и значения не обновляла. Одной вкладке
        // такое расхождение стоит доверия целиком: заявленная платформа и видеокарта не сходятся.
        //
        // Хранилище живёт на символе: собственных ИМЁН у окна оно не добавляет, а перехват при
        // повторной установке лишь обновляет значения, не наслаивая ещё одну обёртку.
        const identityStateKey = Symbol.for('atom.identity.state');
        const identityState: any = globalObject[identityStateKey] ?? (globalObject[identityStateKey] = {});

        // Чей профиль главнее. Ранний скрипт несёт профиль БРАУЗЕРА, запечённый при запуске, и
        // приходит в документ ПОЗЖЕ динамической установки для вкладки — а значит, затирал бы её
        // значения последним. Отличить их просто: у вкладочного конверта есть идентификатор
        // вкладки, у запечённого профиля его нет. Вкладочный имеет приоритет и больше не
        // перетирается запечённым.
        const isTabScopedContext = typeof (context as any).tabId === 'string' && (context as any).tabId.length > 0;

        if (isTabScopedContext || identityState.webGlFromTab !== true) {
            identityState.webGl = (context as any).webGl ?? identityState.webGl;
            identityState.webGlParameters = (context as any).webGlParameters ?? identityState.webGlParameters;

            if (isTabScopedContext) {
                identityState.webGlFromTab = true;
            }
        }

        const webGl: any = identityState.webGl;
        const webGlLimits: any = identityState.webGlParameters;

        if (identityState.webGlInstalled === true) {
            // Перехват уже стоит и читает это же хранилище — обновлённых значений ему достаточно.
        }
        else if (webGl || webGlLimits) {
            identityState.webGlInstalled = true;
            const UNMASKED_VENDOR = 0x9245;
            const UNMASKED_RENDERER = 0x9246;
            const VENDOR = 0x1f00;
            const RENDERER = 0x1f01;
            const MAX_TEXTURE_SIZE = 0x0d33;
            const MAX_RENDERBUFFER_SIZE = 0x84e8;
            const MAX_VIEWPORT_DIMS = 0x0d3a;
            const MAX_VARYING_VECTORS = 0x8dfc;
            const MAX_VERTEX_UNIFORM_VECTORS = 0x8dfb;
            const MAX_FRAGMENT_UNIFORM_VECTORS = 0x8dfd;

            const resolveOverride = (parameter: number): unknown => {
                // Значения читаются из хранилища В МОМЕНТ обращения: профиль вкладки может быть
                // применён уже после установки перехвата.
                const liveWebGl: any = identityState.webGl;
                const liveLimits: any = identityState.webGlParameters;

                if (liveWebGl) {
                    if (parameter === UNMASKED_VENDOR) {
                        return liveWebGl.unmaskedVendor ?? liveWebGl.vendor;
                    }

                    if (parameter === UNMASKED_RENDERER) {
                        return liveWebGl.unmaskedRenderer ?? liveWebGl.renderer;
                    }

                    // Маскированные слоты — константы движка, одинаковые на любой машине. Имя видеокарты
                    // здесь выдавало подмену одной сверкой с 'WebKit' — оно отдаётся через UNMASKED_* выше.
                    if (parameter === VENDOR) {
                        return 'WebKit';
                    }

                    if (parameter === RENDERER) {
                        return 'WebKit WebGL';
                    }
                }

                const webGlLimits: any = liveLimits;

                if (!webGlLimits) {
                    return undefined;
                }

                if (parameter === MAX_TEXTURE_SIZE) {
                    return webGlLimits.maxTextureSize;
                }

                if (parameter === MAX_RENDERBUFFER_SIZE) {
                    return webGlLimits.maxRenderbufferSize;
                }

                if (parameter === MAX_VARYING_VECTORS) {
                    return webGlLimits.maxVaryingVectors;
                }

                if (parameter === MAX_VERTEX_UNIFORM_VECTORS) {
                    return webGlLimits.maxVertexUniformVectors;
                }

                if (parameter === MAX_FRAGMENT_UNIFORM_VECTORS) {
                    return webGlLimits.maxFragmentUniformVectors;
                }

                if (parameter === MAX_VIEWPORT_DIMS) {
                    // Настоящий WebGL отдаёт здесь типизированный массив; обычный выдал бы себя
                    // и типом, и поведением при чтении по индексу.
                    const dims = webGlLimits.maxViewportDims;
                    return Array.isArray(dims) && dims.length === 2 ? new Int32Array(dims) : undefined;
                }

                return undefined;
            };

            for (const contextName of ['WebGLRenderingContext', 'WebGL2RenderingContext']) {
                const constructor = globalObject[contextName];
                const prototype = constructor?.prototype;
                if (!prototype || typeof prototype.getParameter !== 'function') {
                    continue;
                }

                const originalGetParameter = prototype.getParameter;
                prototype.getParameter = asNativeMethod('getParameter', originalGetParameter, function (this: unknown, parameter: number) {
                    const override = resolveOverride(parameter);
                    if (override !== undefined) {
                        return override;
                    }

                    return originalGetParameter.call(this, parameter);
                });
            }
        }

        // Отчёт для диагностики: подтверждает, что подмена ДЕЙСТВУЕТ в этом фрейме, а не просто
        // «скрипт внедрён» — без него эти два состояния неразличимы. Отдаём подменённую платформу
        // и происхождение фрейма: этого хватает, чтобы увидеть кросс-доменный фрейм проверки.
        try {
            return `${navigatorObject.platform}@${globalObject.location?.origin ?? '?'}`;
        } catch {
            return 'ok';
        }
    } catch (error) {
        return `err:${error instanceof Error ? error.message : String(error)}`;
    }
}

/**
 * Считывает текущий состав личности прямо из живого навигатора документа.
 *
 * До подмены это НАСТОЯЩАЯ машина, после — подменённый профиль; почленная сверка с желаемым
 * составом даёт тот же ответ, что хранимый сторож, но без единого собственного свойства на
 * window. Порядок компонент соответствует желаемому составу в установщике.
 */
function readLiveIdentityComponents(globalObject: any): string[] {
    try {
        const navigatorObject = globalObject.navigator;
        if (!navigatorObject) {
            return ['', '', '', '', '', '', ''];
        }

        let timezone = '';
        try {
            timezone = Intl.DateTimeFormat().resolvedOptions().timeZone ?? '';
        } catch {
            timezone = '';
        }

        const userAgentDataPlatform = navigatorObject.userAgentData
            && typeof navigatorObject.userAgentData.platform === 'string'
            ? navigatorObject.userAgentData.platform
            : '';

        return [
            typeof navigatorObject.userAgent === 'string' ? navigatorObject.userAgent : '',
            typeof navigatorObject.platform === 'string' ? navigatorObject.platform : '',
            timezone,
            Array.isArray(navigatorObject.languages) ? navigatorObject.languages.join('|') : '',
            userAgentDataPlatform,
            typeof navigatorObject.deviceMemory === 'number' ? String(navigatorObject.deviceMemory) : '',
            typeof navigatorObject.hardwareConcurrency === 'number' ? String(navigatorObject.hardwareConcurrency) : '',
        ];
    } catch {
        return ['', '', '', '', '', '', ''];
    }
}
