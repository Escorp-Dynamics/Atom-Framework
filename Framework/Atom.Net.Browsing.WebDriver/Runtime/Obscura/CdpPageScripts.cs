namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// JS-функции, которые CDP-транспорт исполняет в странице вместо обработчиков расширения.
/// </summary>
/// <remarks>
/// Семантика повторяет content-скрипт расширения (<c lang="text">ExtensionRuntime/content.runtime.ts</c>):
/// нормализация тела скрипта, приведение результата к строке, стратегии поиска элементов и
/// признаки видимости. Иначе один и тот же код драйвера вёл бы себя по-разному на разных транспортах.
/// </remarks>
internal static class CdpPageScripts
{
    /// <summary>Сообщение, с которым реестр отвечает на неизвестный идентификатор элемента.</summary>
    public const string ElementNotFoundMessage = "Element not found.";

    /// <summary>
    /// Прелюдия реестра элементов: идентификатор ↔ элемент в текущем документе.
    /// </summary>
    /// <remarks>
    /// Реестр живёт на документе, поэтому навигация сбрасывает его так же, как реестр content-скрипта
    /// расширения. Ссылки на объекты CDP здесь непригодны: у Obscura псевдо-идентификаторы элементов
    /// массива не адресуют узел, а объектные аргументы <c lang="text">callFunctionOn</c> не доходят.
    /// Префикс документа в идентификаторе не даёт старому элементу совпасть с новым после навигации.
    /// </remarks>
    public static string CreateRegistryPrelude(string registryKey)
        => $$"""
            const __atomRegistry = (() => {
                const key = Symbol.for({{ToJsLiteral(registryKey)}});
                let registry = document[key];
                if (!registry) {
                    registry = { prefix: Math.random().toString(36).slice(2, 10), next: 0, byId: new Map(), byElement: new WeakMap() };
                    try { Object.defineProperty(document, key, { value: registry }); } catch { }
                }
                return registry;
            })();
            const __atomPut = (element) => {
                if (!element) return null;
                const known = __atomRegistry.byElement.get(element);
                if (known) return known;
                const id = 'cdp_' + __atomRegistry.prefix + '_' + (++__atomRegistry.next);
                __atomRegistry.byId.set(id, element);
                __atomRegistry.byElement.set(element, id);
                return id;
            };
            const __atomGet = (id, missing) => {
                const element = id == null ? null : __atomRegistry.byId.get(id);
                if (element) return element;
                if (missing === null) return null;
                throw new Error(missing ?? {{ToJsLiteral(ElementNotFoundMessage)}});
            };
            """;

    /// <summary>Строковый литерал JavaScript (JSON-экранирование) либо <c lang="text">null</c>.</summary>
    public static string ToJsLiteral(string? value)
        => value is null ? "null" : System.Text.Json.Nodes.JsonValue.Create(value).ToJsonString();

    /// <summary>
    /// Выражение для <c lang="text">Runtime.evaluate</c>: вызывает <paramref name="function"/> с
    /// <c lang="text">this</c> = элементом реестра и возвращает <paramref name="resultMapping"/> над <c lang="text">__result</c>.
    /// </summary>
    /// <param name="registryKey">Ключ реестра элементов документа.</param>
    /// <param name="function">Текст JS-функции.</param>
    /// <param name="targetId">Идентификатор элемента для <c lang="text">this</c> либо <see langword="null"/>.</param>
    /// <param name="missingTargetMessage">
    /// Сообщение ошибки, если элемента нет в реестре; <see langword="null"/> — передать в функцию <c lang="text">null</c>.
    /// </param>
    /// <param name="resultMapping">JS-выражение над <c lang="text">__result</c>.</param>
    /// <param name="arguments">Аргументы функции (сериализуются как JSON).</param>
    public static string BuildInvocation(
        string registryKey,
        string function,
        string? targetId,
        string? missingTargetMessage,
        string resultMapping,
        System.Text.Json.Nodes.JsonArray arguments)
    {
        var target = targetId is null
            ? "null"
            : $"__atomGet({ToJsLiteral(targetId)}, {ToJsLiteral(missingTargetMessage)})";

        return $$"""
            (async () => {
            {{CreateRegistryPrelude(registryKey)}}
            const __target = {{target}};
            const __result = await ({{function}}).apply(__target, {{arguments.ToJsonString()}});
            return {{resultMapping}};
            })()
            """;
    }

    /// <summary>Отображение найденных элементов в идентификаторы реестра.</summary>
    public const string MapSingleElement = "__atomPut(__result)";

    /// <inheritdoc cref="MapSingleElement"/>
    public const string MapElementList = "Array.from(__result || []).map(__atomPut)";

    /// <summary>
    /// Отображение для ожидания: идентификатор найденного элемента с его рамкой и видимостью.
    /// </summary>
    public const string MapWaitCandidate = "__result ? Object.assign({ id: __atomPut(__result) }, JSON.parse((" + ElementBox + ").call(__result))) : null";

    /// <summary>
    /// Общие помощники: компиляция тела скрипта так же, как это делает расширение.
    /// </summary>
    private const string ScriptHelpers = """
        const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
        const stripTerminator = (s) => s.endsWith(';') ? s.slice(0, -1).trimEnd() : s;
        const normalize = (script) => {
            const source = String(script ?? '').trim();
            if (source.length === 0) return 'return undefined;';
            if (/^return\b/.test(source)) return source;
            const expression = stripTerminator(source);
            if (expression.startsWith('(') || expression.startsWith('[') || expression.startsWith('{')) return 'return (' + expression + ');';
            if (/^(?:const|let|var|if|for|while|switch|try|catch|finally|throw|class|function|import|export|do|break|continue|debugger)\b/.test(expression)) return source;
            try {
                new AsyncFunction('return (' + expression + ');');
                return 'return (' + expression + ');';
            } catch {
                return source;
            }
        };
        const asText = (value) => value == null ? 'null' : String(value);
        const errorText = (error) => String(error && error.message !== undefined ? error.message : error);
        """;

    /// <summary>
    /// Исполняет скрипт; <c lang="text">this</c> — элемент, хост теневого корня или хост фрейма.
    /// Возвращает <c lang="text">{ ok, value | error }</c>.
    /// </summary>
    public const string Execute = "async function (source, kind) {\n" + ScriptHelpers + """

        try {
            const body = normalize(source);
            if (kind === 'frame') {
                const host = this;
                const isFrame = host instanceof HTMLIFrameElement || (typeof HTMLFrameElement !== 'undefined' && host instanceof HTMLFrameElement);
                if (!isFrame || !host.contentWindow) return { ok: true, value: 'null' };
                let frameDocument = null;
                try { frameDocument = host.contentDocument; } catch { frameDocument = null; }
                if (!frameDocument) return { ok: true, value: 'null' };
                return { ok: true, value: asText(await host.contentWindow.eval('(async function(){' + body + '})()')) };
            }

            if (kind === 'element') {
                const target = this;
                return { ok: true, value: asText(await new AsyncFunction('element', 'self', body).call(globalThis, target, target)) };
            }

            if (kind === 'shadow') {
                const shadowRoot = this && this.shadowRoot;
                if (!shadowRoot) return { ok: false, error: 'Shadow root not found.' };
                return { ok: true, value: asText(await new AsyncFunction('shadowRoot', body).call(globalThis, shadowRoot)) };
            }

            return { ok: true, value: asText(await new AsyncFunction(body).call(globalThis)) };
        } catch (error) {
            return { ok: false, error: errorText(error) };
        }
        }
        """;

    /// <summary>
    /// Исполняет скрипт во всех доступных (same-origin) фреймах документа; возвращает JSON-строку
    /// в формате ответа ExecuteScriptInFrames расширения.
    /// </summary>
    public const string ExecuteInFrames = "async function (source, includeMetadata) {\n" + ScriptHelpers + """

        const body = normalize(source);
        const frames = [];
        const visit = (win, parentIndex) => {
            const index = frames.length;
            frames.push({ win, parentIndex });
            let hosts = [];
            try { hosts = Array.from(win.document.querySelectorAll('iframe,frame')); } catch { hosts = []; }
            for (const host of hosts) {
                let child = null;
                try { child = host.contentWindow; void child.document.documentElement; } catch { child = null; }
                if (child) visit(child, index);
            }
        };
        visit(window, -1);

        const results = [];
        for (let index = 0; index < frames.length; index++) {
            const frame = frames[index];
            let status = 'ok';
            let value = null;
            let error = null;
            try {
                value = asText(await frame.win.eval('(async function(){' + body + '})()'));
            } catch (failure) {
                status = 'err';
                error = errorText(failure);
            }

            if (includeMetadata) {
                let url = '';
                try { url = String(frame.win.location.href); } catch { url = ''; }
                results.push({ ordinal: index, frameId: index, parentFrameId: frame.parentIndex, url, errorOccurred: false, status, value: status === 'ok' ? value : null, error });
            } else {
                results.push(status === 'ok' ? value : { __error: error });
            }
        }

        return JSON.stringify(results);
        }
        """;

    /// <summary>
    /// Ищет элементы; <c lang="text">this</c> — хост корня поиска (если он задан).
    /// Бросает <c lang="text">Root element not found.</c>, если корень нельзя получить.
    /// </summary>
    public const string Find = """
        function (strategy, value, rootKind, multiple, shadowDiscovery) {
            if (rootKind !== 'document' && !this) throw new Error('Root element not found.');
            let root;
            if (rootKind === 'shadow') {
                root = this && this.shadowRoot;
                if (!root) throw new Error('Root element not found.');
            } else if (rootKind === 'frame') {
                const host = this;
                const isFrame = host instanceof HTMLIFrameElement || (typeof HTMLFrameElement !== 'undefined' && host instanceof HTMLFrameElement);
                if (!isFrame) throw new Error('Root element not found.');
                try { root = host.contentDocument; } catch { root = null; }
                if (!root) return multiple ? [] : null;
            } else if (rootKind === 'parent') {
                root = this;
            } else {
                root = document;
            }

            const evaluationDocument = root.nodeType === 9 ? root : (root.ownerDocument || document);
            const escape = (text) => typeof CSS !== 'undefined' && typeof CSS.escape === 'function'
                ? CSS.escape(text)
                : String(text).replace(/["\\]/g, '\\$&');
            const textNodes = (text, all) => {
                const walkRoot = root === document ? document.body : root;
                if (!walkRoot) return [];
                const found = [];
                const walker = evaluationDocument.createTreeWalker(walkRoot, 4);
                while (walker.nextNode()) {
                    if (walker.currentNode.textContent && walker.currentNode.textContent.trim() === text && walker.currentNode.parentElement) {
                        found.push(walker.currentNode.parentElement);
                        if (!all) break;
                    }
                }
                return found;
            };
            const byId = (id) => typeof root.getElementById === 'function' ? root.getElementById(id) : root.querySelector('#' + escape(id));

            if (!multiple) {
                switch (strategy) {
                    case 'Css':
                    case 'TagName':
                        return root.querySelector(value);
                    case 'XPath': {
                        const node = evaluationDocument.evaluate(value, root, null, 9, null).singleNodeValue;
                        return node && node.nodeType === 1 ? node : null;
                    }
                    case 'Id':
                        return byId(value);
                    case 'Text':
                        return textNodes(value, false)[0] || null;
                    case 'Name':
                        return root.querySelector('[name="' + escape(value) + '"]');
                    default:
                        return null;
                }
            }

            if (shadowDiscovery && strategy === 'Css') {
                const results = [];
                const seen = new Set();
                const visit = (current) => {
                    for (const match of current.querySelectorAll(value)) {
                        if (seen.has(match)) continue;
                        seen.add(match);
                        if (match.isConnected === false) continue;
                        results.push(match);
                    }
                    if (current.nodeType === 1 && current.shadowRoot) visit(current.shadowRoot);
                    for (const candidate of current.querySelectorAll('*')) {
                        if (candidate.shadowRoot) visit(candidate.shadowRoot);
                    }
                };
                visit(root);
                return results;
            }

            switch (strategy) {
                case 'Css':
                case 'TagName':
                    return Array.from(root.querySelectorAll(value));
                case 'XPath': {
                    const snapshot = evaluationDocument.evaluate(value, root, null, 7, null);
                    const elements = [];
                    for (let index = 0; index < snapshot.snapshotLength; index++) {
                        const node = snapshot.snapshotItem(index);
                        if (node && node.nodeType === 1) elements.push(node);
                    }
                    return elements;
                }
                case 'Id': {
                    const element = byId(value);
                    return element ? [element] : [];
                }
                case 'Text':
                    return textNodes(value, true);
                case 'Name':
                    return Array.from(root.querySelectorAll('[name="' + escape(value) + '"]'));
                default:
                    return [];
            }
        }
        """;

    /// <summary>
    /// Рамка и видимость элемента (как <c lang="text">isElementVisible</c> расширения), JSON-строкой.
    /// </summary>
    public const string ElementBox = """
        function () {
            const rect = this && typeof this.getBoundingClientRect === 'function' ? this.getBoundingClientRect() : null;
            if (!rect) return JSON.stringify({ visible: false, x: 0, y: 0, width: 0, height: 0 });
            let visible = rect.width > 0 && rect.height > 0;
            if (visible) {
                const view = this.ownerDocument && this.ownerDocument.defaultView;
                if (view && typeof view.getComputedStyle === 'function') {
                    const style = view.getComputedStyle(this);
                    if (style.visibility === 'hidden' || style.visibility === 'collapse' || style.display === 'none') visible = false;
                    else visible = Number.parseFloat(style.opacity || '1') > 0;
                }
            }
            return JSON.stringify({ visible, x: rect.x, y: rect.y, width: rect.width, height: rect.height });
        }
        """;

    /// <summary>
    /// Описание элемента в форме ответа DescribeElement расширения, JSON-строкой.
    /// </summary>
    public const string Describe = """
        function () {
            const element = this;
            if (!element) return null;
            const view = element.ownerDocument && element.ownerDocument.defaultView;
            const rect = typeof element.getBoundingClientRect === 'function'
                ? element.getBoundingClientRect()
                : { left: 0, top: 0, width: 0, height: 0 };
            let style = null;
            try { style = view && typeof view.getComputedStyle === 'function' ? view.getComputedStyle(element) : null; } catch { style = null; }
            const computedStyle = {};
            if (style) {
                try {
                    for (let index = 0; index < style.length; index++) {
                        const name = style[index];
                        computedStyle[name] = String(style.getPropertyValue(name));
                    }
                } catch {
                }
            }

            const isVisible = rect.width > 0 && rect.height > 0
                && (!style || (style.display !== 'none' && style.visibility !== 'hidden' && style.visibility !== 'collapse' && style.opacity !== '0'));
            const options = typeof HTMLSelectElement !== 'undefined' && element instanceof HTMLSelectElement
                ? Array.from(element.options).map((option) => ({ value: String(option.value), text: String(option.text) }))
                : [];
            let associatedControlId = '';
            try { associatedControlId = element.control && element.control.id ? String(element.control.id) : ''; } catch { associatedControlId = ''; }

            return JSON.stringify({
                tagName: String(element.tagName || ''),
                checked: element.checked === true,
                selectedIndex: typeof element.selectedIndex === 'number' ? element.selectedIndex : -1,
                isActive: !!element.ownerDocument && element.ownerDocument.activeElement === element,
                isConnected: element.isConnected !== false,
                isVisible,
                associatedControlId,
                boundingBox: { left: rect.left, top: rect.top, width: rect.width, height: rect.height },
                computedStyle,
                options,
            });
        }
        """;

    /// <summary>
    /// Значение свойства (а при его отсутствии — атрибута) элемента строкой либо <c lang="text">null</c>.
    /// </summary>
    public const string GetProperty = """
        function (name) {
            if (!this) return null;
            const direct = this[name];
            const value = direct === undefined ? this.getAttribute(name) : direct;
            return value == null ? null : String(value);
        }
        """;

    /// <summary>
    /// Центр элемента в области просмотра верхнего документа, JSON-строкой; по запросу прокручивает к нему.
    /// </summary>
    /// <remarks>
    /// Ввод протокола отладки адресуется координатами верхнего документа, поэтому для элемента во
    /// фрейме прибавляются смещения всех объемлющих фреймов.
    /// </remarks>
    public const string ViewportPoint = """
        function (scroll) {
            if (scroll && typeof this.scrollIntoView === 'function') {
                try { this.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' }); } catch { this.scrollIntoView(); }
            }
            const rect = this.getBoundingClientRect();
            let x = rect.width > 0 ? rect.left + rect.width / 2 : rect.left;
            let y = rect.height > 0 ? rect.top + rect.height / 2 : rect.top;
            let view = this.ownerDocument && this.ownerDocument.defaultView;
            while (view && view.frameElement) {
                const host = view.frameElement;
                const hostRect = host.getBoundingClientRect();
                x += hostRect.left + (host.clientLeft || 0);
                y += hostRect.top + (host.clientTop || 0);
                view = view.parent;
            }
            return JSON.stringify({ viewportX: x, viewportY: y });
        }
        """;

    /// <summary>Есть ли у элемента открытый теневой корень.</summary>
    public const string HasShadowRoot = "function () { return !!(this && this.shadowRoot); }";

    /// <summary>Разметка документа целиком.</summary>
    public const string DocumentContent = """
        (() => {
            const root = document.documentElement;
            if (!root) return '';
            const doctype = document.doctype ? '<!DOCTYPE ' + document.doctype.name + '>' : '';
            return doctype + root.outerHTML;
        })()
        """;

    /// <summary>Фокусирует элемент, по запросу прокрутив к нему.</summary>
    public const string Focus = """
        function (scroll) {
            if (scroll && typeof this.scrollIntoView === 'function') {
                try { this.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' }); } catch { this.scrollIntoView(); }
            }
            if (typeof this.focus === 'function') {
                try { this.focus({ preventScroll: true }); } catch { this.focus(); }
            }
            return true;
        }
        """;

    /// <summary>Прокручивает к элементу.</summary>
    public const string ScrollIntoView = """
        function () {
            try { this.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' }); } catch { this.scrollIntoView(); }
            return true;
        }
        """;

    /// <summary>Состояние документа для событий жизненного цикла.</summary>
    public const string DocumentSnapshot = "JSON.stringify({ href: String(location.href), title: String(document.title ?? ''), readyState: String(document.readyState ?? '') })";

    /// <summary>
    /// Мост callback'ов: забирает узлы-запросы, которые пишет скрипт подписки драйвера, и отправляет
    /// их в binding. Ответ на запрос не пишется — страница продолжает вызов с исходными аргументами.
    /// </summary>
    /// <remarks>
    /// <c lang="text">Runtime.addBinding</c> только уведомляет и не умеет синхронно вернуть решение,
    /// поэтому подмена аргументов и отмена вызова на этом транспорте недоступны.
    /// </remarks>
    /// <remarks>
    /// Имя binding случайно для каждого транспорта, а сама функция забирается в замыкание и удаляется
    /// с <c lang="text">globalThis</c>: странице не по чему узнать драйвер и нечем слать ему конверты.
    /// </remarks>
    public static string CreateCallbackBridge(string bindingName)
        => """
        (() => {
            const binding = __ATOM_BINDING__;
            const marker = Symbol.for(binding);
            if (typeof document === 'undefined' || document[marker]) return;
            try { Object.defineProperty(document, marker, { value: true }); } catch { return; }
            const send = globalThis[binding];
            try { delete globalThis[binding]; } catch { }
            const forward = (selector, kind) => {
                for (const node of Array.from(document.querySelectorAll(selector))) {
                    try {
                        const payload = JSON.parse(node.textContent || 'null');
                        if (payload && typeof payload.name === 'string' && payload.name.length > 0 && typeof send === 'function') {
                            send(JSON.stringify({ kind, payload }));
                        }
                    } catch {
                    } finally {
                        node.remove();
                    }
                }
            };
            document.addEventListener('atom-webdriver-callback-request', () => forward('script[data-atom-callback-request="1"]', 'request'));
            document.addEventListener('atom-webdriver-callback', () => forward('script[data-atom-callback-payload="1"]', 'event'));
            document.addEventListener('atom-webdriver-callback-finalized', () => forward('script[data-atom-callback-finalized="1"]', 'finalized'));
        })();
        """.Replace("__ATOM_BINDING__", ToJsLiteral(bindingName), StringComparison.Ordinal);
}
