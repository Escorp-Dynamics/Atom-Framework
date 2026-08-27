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
        || context.languages !== undefined
        || context.hardwareConcurrency !== undefined
        || context.deviceMemory !== undefined
        || context.maxTouchPoints !== undefined;
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
        const prototype = scope.WorkerNavigator?.prototype;
        const define = (property: string, getter: () => unknown): void => {
            const target = prototype ?? navigatorObject;
            try {
                Object.defineProperty(target, property, { configurable: true, enumerable: true, get: getter });
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
        if (webGl) {
            const overrides: Record<number, unknown> = {
                0x9245: webGl.unmaskedVendor ?? webGl.vendor,
                0x9246: webGl.unmaskedRenderer ?? webGl.renderer,
                0x1f00: webGl.vendor,
                0x1f01: webGl.renderer,
            };

            for (const contextName of ['WebGLRenderingContext', 'WebGL2RenderingContext']) {
                try {
                    const prototype = scope[contextName]?.prototype;
                    if (!prototype || typeof prototype.getParameter !== 'function') {
                        continue;
                    }

                    const originalGetParameter = prototype.getParameter;
                    prototype.getParameter = function patchedGetParameter(parameter: number) {
                        const override = overrides[parameter];
                        return override === undefined ? originalGetParameter.call(this, parameter) : override;
                    };
                } catch {
                    // Контекста может не быть — это нормально.
                }
            }
        }

        // Часовой пояс. Документ отдаёт подменённый, а воркер без этой правки — настоящий пояс
        // машины: снова противоречие внутри одной вкладки, созданное нами же.
        const timezone = context.timezone;
        if (typeof timezone === 'string' && timezone.length > 0) {
            try {
                const OriginalDateTimeFormat: any = scope.Intl?.DateTimeFormat;
                const originalResolvedOptions = OriginalDateTimeFormat?.prototype?.resolvedOptions;

                if (typeof originalResolvedOptions === 'function') {
                    Object.defineProperty(OriginalDateTimeFormat.prototype, 'resolvedOptions', {
                        configurable: true,
                        writable: true,
                        value: function resolvedOptions(this: unknown) {
                            const result = originalResolvedOptions.call(this);
                            return result !== null && typeof result === 'object'
                                ? { ...result, timeZone: timezone }
                                : result;
                        },
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
 * Возвращает счётчики обращений к ОС-зависимым API, накопленные во фрейме.
 *
 * ВРЕМЕННАЯ ДИАГНОСТИКА. Показывает, что проверка читает НА САМОМ ДЕЛЕ, вместо перебора гипотез
 * о том, какая поверхность нас выдаёт. Функция обязана быть самодостаточной по тем же причинам,
 * что и установщик подмены.
 */
export function readProbeLogInMainWorld(): string {
    try {
        const log = (globalThis as any).__atomProbeLog;
        if (!log) {
            return 'нет-данных';
        }

        const origin = (globalThis as any).location?.origin ?? '?';
        const entries = Object.keys(log)
            .map((key) => [key, log[key]] as [string, number])
            .sort((left, right) => right[1] - left[1])
            .map(([key, count]) => `${key}:${count}`);

        return entries.length === 0 ? `${origin} ничего-не-читали` : `${origin} ${entries.join(' ')}`;
    } catch (error) {
        return `err:${error instanceof Error ? error.message : String(error)}`;
    }
}

/**
 * Вычитывает счётчики обращений из всех фреймов вкладки.
 */
export async function readProbeLogFromFrames(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
): Promise<string[]> {
    if (browserHost.scripting?.executeScript === undefined) {
        return [];
    }

    const results = await invokeBrowserCall<any[]>(
        runtime,
        browserHost.scripting.executeScript,
        browserHost.scripting,
        {
            target: { tabId, allFrames: true },
            world: 'MAIN',
            func: readProbeLogInMainWorld,
        },
    );

    return (results ?? []).map((item) => String(item?.result ?? '')).filter((value) => value.length > 0);
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
        const signature = [
            context.userAgent ?? '',
            context.platform ?? '',
            context.timezone ?? '',
            (context.languages ?? []).join('|'),
            context.clientHints?.platform ?? '',
            String(context.deviceMemory ?? ''),
            String(context.hardwareConcurrency ?? ''),
        ].join('');

        if (globalObject.__atomIdentitySignature === signature) {
            return 'already';
        }

        const navigatorObject: any = globalObject.navigator;
        if (!navigatorObject) {
            return 'no-navigator';
        }

        const defineGetter = (target: any, property: string, getter: () => unknown): void => {
            if (!target) {
                return;
            }

            try {
                Object.defineProperty(target, property, { configurable: true, enumerable: true, get: getter });
            } catch {
                // Неподменяемое свойство пропускаем: остальные всё равно должны встать.
            }
        };

        const defineNavigatorGetter = (property: string, getter: () => unknown): void => {
            defineGetter(navigatorObject, property, getter);
            defineGetter(globalObject.Navigator?.prototype, property, getter);
            // В контексте воркера прототип навигатора называется WorkerNavigator.
            defineGetter(globalObject.WorkerNavigator?.prototype, property, getter);
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
                    // ВРЕМЕННАЯ ДИАГНОСТИКА в прологе: воркер сообщает, что он РЕАЛЬНО видит после
                    // установки. Без этого «подмена в воркере применена» — предположение, а не
                    // факт. Канал отдельный (BroadcastChannel), чтобы не вмешиваться в обмен
                    // сообщениями самой проверки.
                    // ВРЕМЕННАЯ ДИАГНОСТИКА: слушаем, что читает сам воркер. Тот же приём, которым
                    // был найден воркер, — только теперь внутри него. Обёрток МИНИМУМ и только на
                    // холодных методах: полный набор обёрток проверку вешает.
                    const report = 'try{'
                        + 'var L={};'
                        + 'var W=function(o,m,n){try{if(!o||typeof o[m]!=="function")return;var f=o[m];'
                        + 'o[m]=function(){L[n]=(L[n]||0)+1;return f.apply(this,arguments);};}catch(e){}};'
                        + 'W(self.OffscreenCanvas&&self.OffscreenCanvas.prototype,"getContext","offscreen.getContext");'
                        + 'W(self.OffscreenCanvas&&self.OffscreenCanvas.prototype,"convertToBlob","offscreen.convertToBlob");'
                        + 'W(self.OffscreenCanvasRenderingContext2D&&self.OffscreenCanvasRenderingContext2D.prototype,"measureText","offscreen.measureText");'
                        + 'W(self.OffscreenCanvasRenderingContext2D&&self.OffscreenCanvasRenderingContext2D.prototype,"fillText","offscreen.fillText");'
                        + 'W(self.OffscreenCanvasRenderingContext2D&&self.OffscreenCanvasRenderingContext2D.prototype,"getImageData","offscreen.getImageData");'
                        + 'W(self.WebGLRenderingContext&&self.WebGLRenderingContext.prototype,"getSupportedExtensions","webgl.getSupportedExtensions");'
                        + 'W(self.WebGL2RenderingContext&&self.WebGL2RenderingContext.prototype,"getSupportedExtensions","webgl2.getSupportedExtensions");'
                        + 'W(self.OfflineAudioContext&&self.OfflineAudioContext.prototype,"startRendering","audio.startRendering");'
                        + 'W(self.crypto&&self.crypto.subtle,"digest","crypto.digest");'
                        + 'W(self.Date.prototype,"getTimezoneOffset","date.getTimezoneOffset");'
                        + 'setTimeout(function(){try{var k=Object.keys(L).map(function(x){return x+":"+L[x];});'
                        + 'new BroadcastChannel("atom-identity-probe").postMessage("ЧИТАЛИ "+(k.length?k.join(" "):"НИЧЕГО")'
                        + '+" || plat="+navigator.platform);}catch(e){}},900);'
                        + '}catch(e){}';

                    const prelude = ';(function(){try{(' + workerSource + ')(' + JSON.stringify(context) + ');'
                        + report + '}catch(e){}})();\n';

                    try {
                        const channel = new globalObject.BroadcastChannel('atom-identity-probe');
                        channel.onmessage = (event: any) => {
                            try {
                                const log = globalObject.__atomProbeLog ?? (globalObject.__atomProbeLog = {});
                                log['worker-sees:' + String(event?.data ?? '?')] = 1;
                            } catch {
                                // Диагностика не важнее работы страницы.
                            }
                        };
                    } catch {
                        // Канал недоступен — просто не будет отчёта.
                    }

                    urlObject.createObjectURL = function createObjectURL(this: unknown, source: any) {
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
                    };
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

            if (typeof context.userAgent === 'string') {
                highEntropyValues.uaFullVersion = '';
            }

            highEntropyValues.fullVersionList = fullVersionList.slice();

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

        // Метрики окна и экрана. Во фрейме они читаются у ТОГО ЖЕ окна, что и в верхнем документе,
        // поэтому расхождение между ними — такой же признак подделки, как разная платформа.
        const viewport = context.viewport;
        if (viewport !== undefined) {
            const width = viewport.width;
            const height = viewport.height;

            defineGetter(globalObject, 'innerWidth', () => width);
            defineGetter(globalObject, 'innerHeight', () => height);
            defineGetter(globalObject, 'outerWidth', () => width);
            defineGetter(globalObject, 'outerHeight', () => height);

            const screenObject = globalObject.screen;
            if (screenObject) {
                defineGetter(screenObject, 'width', () => width);
                defineGetter(screenObject, 'height', () => height);
                // Доступная область меньше полной ровно там, где ОС занимает край экрана панелью.
                // Равенство avail и полного размера — само по себе признак среды без оболочки.
                defineGetter(screenObject, 'availWidth', () => width);
                defineGetter(screenObject, 'availHeight', () => height);
            }
        }

        if (typeof context.deviceScaleFactor === 'number') {
            const deviceScaleFactor = context.deviceScaleFactor;
            defineGetter(globalObject, 'devicePixelRatio', () => deviceScaleFactor);
        }

        // Часовой пояс. Подменяем и `resolvedOptions().timeZone`, и конструктор форматтера: сайту
        // достаточно построить формат без явного timeZone, чтобы получить НАСТОЯЩИЙ пояс машины.
        const timezone = context.timezone;
        if (typeof timezone === 'string' && timezone.length > 0) {
            const OriginalDateTimeFormat: any = Intl.DateTimeFormat;
            const originalResolvedOptions = OriginalDateTimeFormat.prototype.resolvedOptions;

            try {
                Object.defineProperty(OriginalDateTimeFormat.prototype, 'resolvedOptions', {
                    configurable: true,
                    value: function resolvedOptions(this: unknown) {
                        const result = originalResolvedOptions.call(this);
                        if (result === null || typeof result !== 'object') {
                            return result;
                        }

                        return { ...result, timeZone: timezone };
                    },
                });
            } catch {
                // Неподменяемый прототип — остальные поверхности всё равно должны встать.
            }

            const PatchedDateTimeFormat: any = function (this: unknown, locales?: unknown, options?: any) {
                const nextOptions = options === null || options === undefined ? {} : { ...options };
                if (nextOptions.timeZone === undefined) {
                    nextOptions.timeZone = timezone;
                }

                return new.target !== undefined
                    ? Reflect.construct(OriginalDateTimeFormat, [locales, nextOptions], new.target)
                    : new OriginalDateTimeFormat(locales, nextOptions);
            };

            try {
                Object.setPrototypeOf(PatchedDateTimeFormat, OriginalDateTimeFormat);
                PatchedDateTimeFormat.prototype = OriginalDateTimeFormat.prototype;
                Object.defineProperty(Intl, 'DateTimeFormat', {
                    configurable: true,
                    writable: true,
                    value: PatchedDateTimeFormat,
                });
            } catch {
                // То же самое: частичная подмена лучше, чем срыв всей установки.
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
        const declaredOs = clientHints?.platform ?? '';

        if (declaredOs === 'Windows' || declaredOs === 'macOS') {
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

            const voices = voiceNames.map(([name, lang], index) => ({
                voiceURI: name,
                name,
                lang,
                localService: true,
                default: index === 0,
            }));

            const speech = globalObject.speechSynthesis;
            if (speech) {
                try {
                    const originalGetVoices = speech.getVoices?.bind(speech);
                    speech.getVoices = function getVoices() {
                        const native = originalGetVoices ? originalGetVoices() : [];
                        // Если система голоса всё же отдаёт — не подменяем: правдивое окружение
                        // всегда лучше выдуманного.
                        return native !== null && native !== undefined && native.length > 0 ? native : voices.slice();
                    };
                } catch {
                    // Неподменяемый метод — остальные поверхности всё равно должны встать.
                }
            }
        }

        // Доступная область экрана. Полное равенство avail и физического размера — почерк среды без
        // оболочки рабочего стола; на Windows край экрана занимает панель задач, на macOS — строка меню.
        const screenObject = globalObject.screen;
        if (screenObject && (declaredOs === 'Windows' || declaredOs === 'macOS')) {
            const reservedHeight = declaredOs === 'Windows' ? 40 : 25;
            const fullHeight = context.viewport?.height ?? screenObject.height;
            const fullWidth = context.viewport?.width ?? screenObject.width;

            defineGetter(screenObject, 'availWidth', () => fullWidth);
            defineGetter(screenObject, 'availHeight', () => fullHeight - reservedHeight);
        }

        // WebGL. Подменять надо И маскированные строки, И немаскированные (через расширение
        // WEBGL_debug_renderer_info): расхождение между ними само по себе выдаёт подделку.
        const webGl: any = (context as any).webGl;
        if (webGl) {
            const UNMASKED_VENDOR = 0x9245;
            const UNMASKED_RENDERER = 0x9246;
            const VENDOR = 0x1f00;
            const RENDERER = 0x1f01;

            const resolveOverride = (parameter: number): string | undefined => {
                if (parameter === UNMASKED_VENDOR) {
                    return webGl.unmaskedVendor ?? webGl.vendor;
                }

                if (parameter === UNMASKED_RENDERER) {
                    return webGl.unmaskedRenderer ?? webGl.renderer;
                }

                if (parameter === VENDOR) {
                    return webGl.vendor;
                }

                if (parameter === RENDERER) {
                    return webGl.renderer;
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
                prototype.getParameter = function patchedGetParameter(parameter: number) {
                    const override = resolveOverride(parameter);
                    if (override !== undefined) {
                        return override;
                    }

                    return originalGetParameter.call(this, parameter);
                };
            }
        }

        globalObject.__atomIdentitySignature = signature;

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
