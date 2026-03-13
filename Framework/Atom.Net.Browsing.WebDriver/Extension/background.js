/**
 * Atom WebDriver Connector — Background Script.
 *
 * Управляет WebSocket-соединениями с .NET-драйвером.
 * Каждая вкладка получает собственный изолированный канал,
 * что обеспечивает полную независимость контекстов.
 *
 * Универсальный скрипт: работает как в Chrome (MV3), так и в Firefox (MV2).
 *
 * Протокол:
 *   .NET → WebSocket → background.js → browser.runtime → content.js → DOM
 *   DOM  → content.js → browser.runtime → background.js → WebSocket → .NET
 */

const browser = globalThis.browser ?? globalThis.chrome;

// ── MV2 Polyfill: промисификация callback-based Chrome API ──────
// В Chrome MV2 API не возвращают Promise — только callback.
// Firefox (globalThis.browser) уже поддерживает Promise нативно.
if (!globalThis.browser) {
    function _promisify(fn) {
        return function (...args) {
            if (typeof args[args.length - 1] === "function") return fn.apply(this, args);
            return new Promise((resolve, reject) => {
                fn.call(this, ...args, (result) => {
                    if (browser.runtime.lastError) reject(new Error(browser.runtime.lastError.message));
                    else resolve(result);
                });
            });
        };
    }
    const _patch = (obj, methods) => { if (obj) for (const m of methods) if (obj[m]) obj[m] = _promisify(obj[m]); };
    _patch(browser.tabs, ["get", "query", "create", "update", "remove", "reload", "executeScript", "captureVisibleTab"]);
    _patch(browser.windows, ["create", "remove", "update"]);
    _patch(browser.cookies, ["get", "set", "getAll", "remove"]);
}

/** @type {Map<number, WebSocket>} Карта: tabId → WebSocket-соединение */
const tabSockets = new Map();

/** @type {{ host: string, port: number, secret: string } | null} */
let bridgeConfig = null;

/** @type {boolean} */
let autoConnectEnabled = false;

/** @type {Map<number, Array<{id: string, command: string, payload: object}>>} Очередь команд для content.js (fallback, если порт ещё не подключён). */
const commandQueues = new Map();

/** @type {Map<number, browser.runtime.Port>} Персистентные порты к content.js по tabId. */
const tabPorts = new Map();

/** @type {Map<number, {contextId: string, userAgent?: string, locale?: string, languages?: string[], platform?: string}>} Настройки изоляции по вкладкам. */
const tabContexts = new Map();

/** @type {Map<string, Array<{name: string, value: string, domain: string, path: string, secure: boolean, httpOnly: boolean}>>} Виртуальные cookie-хранилища по contextId. */
const virtualCookies = new Map();

/** @type {Set<number>} Вкладки с включённым перехватом запросов. */
const interceptEnabled = new Set();

/** @type {Map<string, Object<string, string>>} requestId → заголовки для модификации (передаются из sync XHR ответа в onBeforeSendHeaders). */
const pendingHeaderOverrides = new Map();

/** @type {Map<number, string>} tabId → compositeId для корректной идентификации вкладки при перехвате запросов. */
const tabCompositeIds = new Map();


// ─── Утилита: выполнение кода в MAIN world ────────────────────

/**
 * Выполняет JS-код в контексте MAIN world страницы.
 * MV3: chrome.scripting.executeScript с world: "MAIN".
 * MV2: chrome.tabs.executeScript → <script> tag + DOM bridge.
 * CSP-заголовки снимаются в onHeadersReceived для корректной работы MV2.
 * @param {number} tabId
 * @param {string} code — JS-код для выполнения
 * @param {boolean} [allFrames=false]
 * @returns {Promise<Array<{s: string, v: string}>>}
 */
function evalInMainWorld(tabId, code, allFrames) {
    if (browser.scripting?.executeScript) {
        // MV3: нативный MAIN world.
        return browser.scripting.executeScript({
            target: allFrames ? { tabId, allFrames: true } : { tabId },
            world: "MAIN",
            func: (c) => {
                try { return { s: "ok", v: String((0, eval)(c)) }; }
                catch (e) { return { s: "err", v: e.message }; }
            },
            args: [code],
        }).then(results => results.map(r => r.result));
    }

    // MV2: инъекция <script> через DOM bridge.
    // Код передаётся через DOM-атрибут (безопасно, не требует экранирования).
    // Inline <script> читает код из атрибута, eval-ит, пишет результат обратно.
    const wrapper =
        '(function(code){' +
        'var id="__ab"+Math.random().toString(36).slice(2,8);' +
        'var el=document.createElement("span");' +
        'el.id=id;el.style.display="none";' +
        'el.setAttribute("data-c",code);' +
        'document.documentElement.appendChild(el);' +
        'var s=document.createElement("script");' +
        "s.textContent=\"(function(){" +
        "var e=document.getElementById('\"+id+\"');" +
        "if(!e)return;" +
        "try{var r=(0,eval)(e.getAttribute('data-c'));" +
        "e.setAttribute('data-r',JSON.stringify({s:'ok',v:r!=null?String(r):'null'}))}" +
        "catch(x){e.setAttribute('data-r',JSON.stringify({s:'err',v:x.message}))}" +
        "})()\";" +
        'document.documentElement.appendChild(s);s.remove();' +
        'var j=el.getAttribute("data-r");el.remove();' +
        'if(!j)return{s:"err",v:"MAIN world injection blocked (CSP?)"};' +
        'try{return JSON.parse(j)}catch(x){return{s:"err",v:"Bridge parse error"}}' +
        '})(' + JSON.stringify(code) + ')';

    return browser.tabs.executeScript(tabId, { code: wrapper, allFrames: !!allFrames })
        .then(results => results || []);
}


// ─── Авто-конфигурация из config.json ────────────────────────

// При запуске расширения пробуем загрузить конфигурацию из файла config.json,
// который .NET-драйвер записывает в копию расширения перед запуском браузера.
// Это позволяет обойти проблему с браузерами (Vivaldi, Edge), которые при
// первом запуске нового профиля не открывают переданный URL.
(async () => {
    try {
        const response = await fetch(browser.runtime.getURL("config.json"));
        if (response.ok) {
            const config = await response.json();
            if (config.host && config.port && config.secret) {
                bridgeConfig = { host: config.host, port: config.port, secret: config.secret };
                autoConnectEnabled = true;

                const discoveryUrl = `http://${config.host}:${config.port}/`;

                // Перезагружаем все существующие http-вкладки, чтобы content.js
                // инъектировался (при argv URL вкладка открывается до регистрации
                // content_scripts расширения).
                // Firefox MV2 background.js стартует до загрузки argv URL —
                // даём время и повторяем запрос.
                let tabs = await browser.tabs.query({ url: "http://*/*" });
                if (tabs.length === 0) {
                    await new Promise(r => setTimeout(r, 1500));
                    tabs = await browser.tabs.query({ url: "http://*/*" });
                }

                if (tabs.length > 0) {
                    for (const tab of tabs) {
                        browser.tabs.reload(tab.id);
                    }
                } else {
                    await browser.tabs.create({ url: discoveryUrl, active: true });
                }
            }
        }
    } catch (e) {
        console.error("[Atom] config load error:", e.message);
    }
})();


// ─── Управление вкладками ────────────────────────────────────

browser.tabs.onRemoved.addListener((tabId) => {
    disconnectTab(tabId);
});

browser.tabs.onUpdated.addListener((tabId, changeInfo) => {
    if (changeInfo.status === "complete" && tabSockets.has(tabId)) {
        sendEvent(tabId, "PageLoaded", { url: changeInfo.url });
    }

    // Авто-подключение новых вкладок при наличии конфигурации.
    if (changeInfo.status === "complete" && autoConnectEnabled && !tabSockets.has(tabId)) {
        connectTab(tabId);
    }
});

/**
 * Программатически inject content.js в вкладку, где declarative content_scripts
 * не сработали (about:blank, chrome-extension://, data: URL и т.д.).
 * @param {number} tabId
 * @returns {Promise<void>}
 */
function injectContentScript(tabId) {
    if (browser.scripting?.executeScript) {
        // MV3 (Chrome, Brave, Edge, Opera, Vivaldi, Yandex).
        return browser.scripting.executeScript({
            target: { tabId },
            files: ["content.js"],
        });
    } else if (browser.tabs?.executeScript) {
        // MV2 (Firefox).
        return browser.tabs.executeScript(tabId, { file: "content.js" });
    }
    return Promise.resolve();
}

/**
 * Ожидает появления порта для вкладки (content.js подключился).
 * @param {number} tabId
 * @param {number} timeoutMs — максимальное время ожидания в мс.
 * @returns {Promise<void>}
 */
function waitForPort(tabId, timeoutMs) {
    if (tabPorts.has(tabId)) return Promise.resolve();
    return new Promise((resolve) => {
        const start = Date.now();
        const check = () => {
            if (tabPorts.has(tabId)) { resolve(); return; }
            if (Date.now() - start > timeoutMs) { resolve(); return; }
            setTimeout(check, 50);
        };
        check();
    });
}

// Chrome MV3: при смене активной вкладки переключаем proxy на прокси активного контекста.
browser.tabs.onActivated.addListener(({ tabId }) => {
    const ctx = tabContexts.get(tabId);
    if (ctx?.proxy) {
        applyChromiumProxy(ctx.proxy);
    } else {
        clearChromiumProxy();
    }
});

// ─── Обмен сообщениями с content.js ──────────────────────────

browser.runtime.onMessage.addListener((message, sender) => {
    if (!sender.tab?.id) return Promise.resolve({ ok: false, error: "Нет tabId." });

    const tabId = sender.tab.id;

    switch (message.action) {
        case "connect":
            connectTab(tabId);
            return Promise.resolve({ ok: true, tabId });

        case "disconnect":
            disconnectTab(tabId);
            return Promise.resolve({ ok: true });

        case "event":
            sendEvent(tabId, message.event, message.data);
            return Promise.resolve({ ok: true });

        case "response":
            forwardResponseToBridge(tabId, message.id, message.status, message.payload, message.error);
            return Promise.resolve({ ok: true });

        case "configure":
            bridgeConfig = { host: message.host, port: message.port, secret: message.secret };
            autoConnectEnabled = true;
            // Подключаем discovery-вкладку напрямую к мосту.
            connectTab(tabId);
            return Promise.resolve({ ok: true });

        case "getContext": {
            const ctx = tabContexts.get(tabId);
            return Promise.resolve(ctx || null);
        }

        case "poll": {
            const queue = commandQueues.get(tabId);
            if (queue && queue.length > 0) {
                const cmd = queue.shift();
                if (queue.length === 0) commandQueues.delete(tabId);
                return Promise.resolve(cmd);
            }
            return Promise.resolve(null);
        }

        default:
            return Promise.resolve({ ok: false, error: "Неизвестное действие." });
    }
});

// ─── Персистентные порты (push-модель) ───────────────────────

browser.runtime.onConnect.addListener((port) => {
    const tabId = port.sender?.tab?.id;
    if (!tabId) return;

    tabPorts.set(tabId, port);

    // Доставляем команды, накопленные пока порт не был подключён.
    const queue = commandQueues.get(tabId);
    if (queue) {
        while (queue.length > 0) {
            port.postMessage(queue.shift());
        }
        commandQueues.delete(tabId);
    }

    port.onMessage.addListener((msg) => {
        if (msg.action === "response") {
            forwardResponseToBridge(tabId, msg.id, msg.status, msg.payload, msg.error);
        } else if (msg.action === "event") {
            sendEvent(tabId, msg.event, msg.data);
        } else if (msg.action === "executeInMain") {
            evalInMainWorld(tabId, msg.script).then((results) => {
                const r = results?.[0];
                port.postMessage({
                    action: "mainWorldResult",
                    requestId: msg.requestId,
                    status: r?.s === "ok" ? "ok" : "err",
                    value: r?.s === "ok" ? r.v : undefined,
                    error: r?.s !== "ok" ? (r?.v || "Script execution failed.") : undefined,
                });
            }).catch((err) => {
                port.postMessage({
                    action: "mainWorldResult",
                    requestId: msg.requestId,
                    status: "err",
                    error: err.message,
                });
            });
        }
    });

    port.onDisconnect.addListener(() => {
        tabPorts.delete(tabId);
    });
});

// ─── WebSocket-соединение ────────────────────────────────────

/**
 * Подключает вкладку к WebSocket-мосту.
 * @param {number} tabId
 */
function connectTab(tabId) {
    if (tabSockets.has(tabId)) return;
    if (!bridgeConfig) {
        console.warn(`[Atom] Нет конфигурации моста для вкладки ${tabId}.`);
        return;
    }

    const { host, port, secret } = bridgeConfig;
    const url = `ws://${host}:${port}/?secret=${encodeURIComponent(secret)}`;
    const ws = new WebSocket(url);

    // Получаем windowId для формирования составного идентификатора.
    let compositeId = null;
    let wsReady = false;

    browser.tabs.get(tabId, (tab) => {
        if (browser.runtime.lastError || !tab) {
            compositeId = `0:${tabId}`;
        } else {
            compositeId = `${tab.windowId ?? 0}:${tabId}`;
        }
        tabCompositeIds.set(tabId, compositeId);
        if (wsReady) sendHandshake(ws, compositeId);
    });

    ws.addEventListener("open", () => {
        wsReady = true;
        if (compositeId) sendHandshake(ws, compositeId);
    });

    ws.addEventListener("message", (event) => {
        handleBridgeMessage(tabId, event.data);
    });

    ws.addEventListener("close", (e) => {
        tabSockets.delete(tabId);
        sendEvent(tabId, "TabDisconnected", {});
    });

    ws.addEventListener("error", () => {
        tabSockets.delete(tabId);
    });

    tabSockets.set(tabId, ws);
}

/**
 * Отключает вкладку от WebSocket-моста.
 * @param {number} tabId
 */
function disconnectTab(tabId) {
    const ws = tabSockets.get(tabId);
    if (!ws) return;

    tabSockets.delete(tabId);
    tabCompositeIds.delete(tabId);

    // Очистка контекста изоляции.
    if (tabContexts.has(tabId)) {
        removeTabNetworkRules(tabId);
        const ctx = tabContexts.get(tabId);
        if (ctx?.proxy) clearChromiumProxy();
        if (ctx?.contextId) virtualCookies.delete(ctx.contextId);
        tabContexts.delete(tabId);
    }

    if (ws.readyState === WebSocket.OPEN) {
        ws.close(1000, "Вкладка закрыта.");
    }
}

// ─── Обработка входящих сообщений от .NET ────────────────────

/**
 * @param {number} tabId
 * @param {string} raw
 */
function handleBridgeMessage(tabId, raw) {
    let message;
    try {
        message = JSON.parse(raw);
    } catch {
        return;
    }

    if (message.type === "Ping") {
        sendPong(tabId, message.id);
        return;
    }

    if (message.type !== "Request") return;

    // Команды, обрабатываемые background.js напрямую (не content.js).
    switch (message.command) {
        case "OpenTab":
            handleOpenTab(tabId, message);
            return;
        case "OpenWindow":
            handleOpenWindow(tabId, message);
            return;
        case "CloseTab":
            handleCloseTab(tabId, message);
            return;
        case "CloseWindow":
            handleCloseWindow(tabId, message);
            return;
        case "ActivateTab":
            handleActivateTab(tabId, message);
            return;
        case "ActivateWindow":
            handleActivateWindow(tabId, message);
            return;
        case "Navigate":
            handleNavigate(tabId, message);
            return;
        case "WaitForNavigation":
            handleWaitForNavigation(tabId, message);
            return;
        case "GetUrl":
            handleGetUrl(tabId, message);
            return;
        case "GetTitle":
            handleGetTitle(tabId, message);
            return;
        case "GetContent":
            handleGetContent(tabId, message);
            return;
        case "CaptureScreenshot":
            handleCaptureScreenshot(tabId, message);
            return;
        case "SetCookie":
            handleSetCookie(tabId, message);
            return;
        case "GetCookies":
            handleGetCookies(tabId, message);
            return;
        case "DeleteCookies":
            handleDeleteCookies(tabId, message);
            return;
        case "SetTabContext":
            handleSetTabContext(tabId, message);
            return;
        case "ExecuteScript":
            handleExecuteScript(tabId, message);
            return;
        case "ExecuteScriptInFrames":
            handleExecuteScriptInFrames(tabId, message);
            return;
        case "InterceptRequest": {
            const enabled = message.payload?.enabled;
            if (enabled) {
                interceptEnabled.add(tabId);
            } else {
                interceptEnabled.delete(tabId);
            }
            sendResponse(tabId, message.id, "Ok", { enabled: !!enabled });
            return;
        }
        case "DebugPortStatus": {
            const queue = commandQueues.get(tabId);
            sendResponse(tabId, message.id, "Ok", {
                tabId,
                hasPort: tabPorts.has(tabId),
                queueLength: queue?.length ?? 0,
                hasSocket: tabSockets.has(tabId),
                allPortTabIds: [...tabPorts.keys()],
                allSocketTabIds: [...tabSockets.keys()],
            });
            return;
        }
    }

    // Ставим команду в очередь для content.js (доставляется через polling).
    queueCommand(tabId, message.id, message.command, message.payload);
}

/**
 * Доставляет команду в content.js через порт (push) или очередь (fallback).
 * @param {number} tabId
 * @param {string} id
 * @param {string} command
 * @param {object} payload
 */
function queueCommand(tabId, id, command, payload) {
    const msg = { id, command, payload };
    const port = tabPorts.get(tabId);
    if (port) {
        try {
            port.postMessage(msg);
            return;
        } catch {
            tabPorts.delete(tabId);
        }
    }
    // Fallback: порт отсутствует — кладём в очередь.
    if (!commandQueues.has(tabId)) commandQueues.set(tabId, []);
    commandQueues.get(tabId).push(msg);
}

/**
 * Возвращает URL пустой страницы моста, на которой content_scripts inject'ятся автоматически.
 * Используется вместо about:blank, т.к. Chrome запрещает inject в about:blank.
 * @returns {string}
 */
function getBridgeBlankUrl() {
    if (bridgeConfig) {
        return `http://${bridgeConfig.host}:${bridgeConfig.port}/blank`;
    }
    return "about:blank";
}

/**
 * Открывает новую вкладку в текущем окне.
 * @param {number} senderTabId — вкладка-отправитель (для ответа).
 * @param {object} message
 */
function handleOpenTab(senderTabId, message) {
    // about:blank недоступен для content_scripts в Chrome MV3.
    // Используем bridge /blank endpoint, где content.js inject'ится автоматически.
    const requestedUrl = message.payload?.url;
    const url = requestedUrl || getBridgeBlankUrl();

    browser.tabs.create({ url, active: true }).then(async (tab) => {
        // Для bridge blank URL: ждём, пока content.js создаст порт.
        if (!requestedUrl) {
            await waitForPort(tab.id, 5000);
        }
        sendResponse(senderTabId, message.id, "Ok", { tabId: tab.id, windowId: tab.windowId });
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Открывает новое окно браузера.
 * @param {number} senderTabId — вкладка-отправитель (для ответа).
 * @param {object} message
 */
function handleOpenWindow(senderTabId, message) {
    const requestedUrl = message.payload?.url;
    const url = requestedUrl || getBridgeBlankUrl();

    browser.windows.create({ url, focused: true }).then(async (win) => {
        const newTabId = win.tabs?.[0]?.id ?? null;
        if (newTabId && !requestedUrl) {
            await waitForPort(newTabId, 5000);
        }
        sendResponse(senderTabId, message.id, "Ok", { windowId: win.id, tabId: newTabId });
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Закрывает указанную вкладку.
 * @param {number} tabId — вкладка, которую нужно закрыть.
 * @param {object} message
 */
function handleCloseTab(tabId, message) {
    let rawId = message.payload?.tabId;
    // Составной ID "windowId:tabId" → извлекаем числовой tabId.
    if (rawId && String(rawId).includes(":")) {
        rawId = String(rawId).split(":").pop();
    }
    const targetTabId = rawId ? Number(rawId) : tabId;

    disconnectTab(targetTabId);

    browser.tabs.remove(targetTabId).then(() => {
        sendResponse(tabId, message.id, "Ok", null);
    }).catch((err) => {
        sendResponse(tabId, message.id, "Error", null, err.message);
    });
}

/**
 * Закрывает окно браузера по windowId.
 * @param {number} senderTabId — вкладка-отправитель (для ответа).
 * @param {object} message
 */
function handleCloseWindow(senderTabId, message) {
    const windowId = Number(message.payload?.windowId);
    if (!windowId || isNaN(windowId)) {
        sendResponse(senderTabId, message.id, "Error", null, "windowId не указан или некорректен.");
        return;
    }

    // Отключаем все вкладки этого окна.
    browser.tabs.query({ windowId }).then((tabs) => {
        for (const tab of tabs) {
            if (tab.id) disconnectTab(tab.id);
        }
        return browser.windows.remove(windowId);
    }).then(() => {
        sendResponse(senderTabId, message.id, "Ok", null);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Активирует (переключает фокус на) вкладку.
 * @param {number} senderTabId — вкладка-отправитель (для ответа).
 * @param {object} message
 */
function handleActivateTab(senderTabId, message) {
    let rawId = message.payload?.tabId;
    if (rawId && String(rawId).includes(":")) {
        rawId = String(rawId).split(":").pop();
    }
    const targetTabId = rawId ? Number(rawId) : senderTabId;

    browser.tabs.update(targetTabId, { active: true }).then(() => {
        sendResponse(senderTabId, message.id, "Ok", null);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Активирует (переключает фокус на) окно.
 * @param {number} senderTabId — вкладка-отправитель (для ответа).
 * @param {object} message
 */
function handleActivateWindow(senderTabId, message) {
    const windowId = Number(message.payload?.windowId);
    if (!windowId || isNaN(windowId)) {
        sendResponse(senderTabId, message.id, "Error", null, "windowId не указан или некорректен.");
        return;
    }

    browser.windows.update(windowId, { focused: true }).then(() => {
        sendResponse(senderTabId, message.id, "Ok", null);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Выполняет JavaScript-код в главном фрейме вкладки (MAIN world).
 * @param {number} senderTabId
 * @param {object} message
 */
function handleExecuteScript(senderTabId, message) {
    const script = message.payload?.script;
    if (!script) {
        sendResponse(senderTabId, message.id, "Error", null, "script не указан.");
        return;
    }

    evalInMainWorld(senderTabId, script).then((results) => {
        const r = results?.[0];
        if (r?.s === "ok") sendResponse(senderTabId, message.id, "Ok", r.v);
        else sendResponse(senderTabId, message.id, "Error", null, r?.v || "Script execution failed.");
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Выполняет JavaScript-код во всех фреймах вкладки (включая cross-origin iframe).
 * MV3: scripting.executeScript с world: "MAIN", MV2: evalInMainWorld bridge.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleExecuteScriptInFrames(senderTabId, message) {
    const script = message.payload?.script;
    if (!script) {
        sendResponse(senderTabId, message.id, "Error", null, "script не указан.");
        return;
    }

    evalInMainWorld(senderTabId, script, true).then((results) => {
        const values = (results ?? []).map((r) =>
            r?.s === "ok" ? r.v : { __error: r?.v || "Script execution failed." }
        );
        sendResponse(senderTabId, message.id, "Ok", values);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Навигация через browser.tabs.update — не зависит от content.js.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleNavigate(senderTabId, message) {
    const url = message.payload?.url;
    const body = message.payload?.body;
    if (!url) {
        sendResponse(senderTabId, message.id, "Error", null, "url не указан.");
        return;
    }

    // Если передан body — подменяем ответ на сетевом уровне (Firefox)
    // или через scripting.executeScript после загрузки (Chrome/остальные).
    // Примечание: HTTP-запрос всё же уходит на сервер — без CDP невозможно
    // показать произвольный URL в адресной строке без реального запроса.
    // filterResponseData заменяет тело ответа ДО рендеринга страницы.
    let filterListener = null;
    const useFilterResponse = body && typeof browser.webRequest?.filterResponseData === "function";
    if (body && useFilterResponse) {
        // Firefox MV2: перехват response body через StreamFilter.
        // Замена происходит в onstart — как только приходят response headers,
        // до получения оригинального тела. Страница рендерит только наш HTML.
        filterListener = (details) => {
            browser.webRequest.onBeforeRequest.removeListener(filterListener);
            filterListener = null;
            const filter = browser.webRequest.filterResponseData(details.requestId);
            const encoder = new TextEncoder();
            filter.onstart = () => {
                filter.write(encoder.encode(body));
                filter.close();
            };
        };
        browser.webRequest.onBeforeRequest.addListener(
            filterListener,
            { urls: ["<all_urls>"], tabId: senderTabId, types: ["main_frame"] },
            ["blocking"],
        );
    }

    let responded = false;
    const respond = async () => {
        if (responded) return;
        responded = true;
        browser.tabs.onUpdated.removeListener(onUpdated);
        // После навигации content.js уничтожается и пере-inject'ится на новой странице.
        // Ждём восстановления порта, чтобы последующие команды не попали в пустоту.
        await waitForPort(senderTabId, 5000);
        sendResponse(senderTabId, message.id, "Ok", null);
    };

    // Регистрируем listener ДО update, чтобы не пропустить "complete".
    const onUpdated = (tabId, changeInfo) => {
        if (tabId === senderTabId && changeInfo.status === "complete") {
            if (body && !useFilterResponse) {
                // document.open/write/close полностью заменяет документ —
                // в отличие от innerHTML, скрипты (<script>) выполняются корректно.
                const injectBody = (html) => {
                    document.open();
                    document.write(html);
                    document.close();
                };
                if (browser.scripting?.executeScript) {
                    browser.scripting.executeScript({
                        target: { tabId: senderTabId },
                        world: "MAIN",
                        func: injectBody,
                        args: [body],
                    }).then(() => respond()).catch(() => respond());
                } else {
                    // MV2 fallback (не Firefox — у Firefox filterResponseData).
                    const code = `(() => {
                        document.open();
                        document.write(${JSON.stringify(body)});
                        document.close();
                    })();`;
                    browser.tabs.executeScript(senderTabId, { code }).then(() => respond()).catch(() => respond());
                }
            } else {
                respond();
            }
        }
    };
    browser.tabs.onUpdated.addListener(onUpdated);

    browser.tabs.update(senderTabId, { url }).catch((err) => {
        if (filterListener) {
            browser.webRequest.onBeforeRequest.removeListener(filterListener);
        }
        responded = true;
        browser.tabs.onUpdated.removeListener(onUpdated);
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Ожидает завершения навигации на вкладке (следующий "complete").
 * @param {number} senderTabId
 * @param {object} message
 */
function handleWaitForNavigation(senderTabId, message) {
    const timeout = message.payload?.timeoutMs || 30000;
    let responded = false;

    const respond = (status, payload, error) => {
        if (responded) return;
        responded = true;
        browser.tabs.onUpdated.removeListener(onUpdated);
        clearTimeout(timer);
        sendResponse(senderTabId, message.id, status, payload, error);
    };

    const onUpdated = (tabId, changeInfo) => {
        if (tabId === senderTabId && changeInfo.status === "complete") {
            browser.tabs.get(senderTabId).then((tab) => {
                respond("Ok", tab?.url || null, null);
            }).catch(() => {
                respond("Ok", null, null);
            });
        }
    };

    browser.tabs.onUpdated.addListener(onUpdated);

    const timer = setTimeout(() => {
        respond("Timeout", null, "Навигация не завершена в течение таймаута.");
    }, timeout);
}

/**
 * Получает URL вкладки через browser.tabs.get.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleGetUrl(senderTabId, message) {
    browser.tabs.get(senderTabId).then((tab) => {
        sendResponse(senderTabId, message.id, "Ok", tab.url ?? null);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Получает заголовок вкладки через browser.tabs.get.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleGetTitle(senderTabId, message) {
    browser.tabs.get(senderTabId).then((tab) => {
        sendResponse(senderTabId, message.id, "Ok", tab.title ?? null);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Получает HTML-содержимое страницы через scripting.executeScript (MV3) или tabs.executeScript (MV2).
 * @param {number} senderTabId
 * @param {object} message
 */
function handleGetContent(senderTabId, message) {
    if (browser.scripting?.executeScript) {
        // MV3 (Chrome, Brave, Edge, Opera, Vivaldi, Yandex).
        browser.scripting.executeScript({
            target: { tabId: senderTabId },
            func: () => document.documentElement.outerHTML,
        }).then((results) => {
            const html = results?.[0]?.result ?? null;
            sendResponse(senderTabId, message.id, "Ok", html);
        }).catch((err) => {
            sendResponse(senderTabId, message.id, "Error", null, err.message);
        });
    } else {
        // MV2 (Firefox).
        browser.tabs.executeScript(senderTabId, {
            code: "document.documentElement.outerHTML",
        }).then((results) => {
            const html = results?.[0] ?? null;
            sendResponse(senderTabId, message.id, "Ok", html);
        }).catch((err) => {
            sendResponse(senderTabId, message.id, "Error", null, err.message);
        });
    }
}

/**
 * Делает снимок видимой области вкладки через browser.tabs.captureVisibleTab.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleCaptureScreenshot(senderTabId, message) {
    browser.tabs.get(senderTabId).then((tab) => {
        return browser.tabs.captureVisibleTab(tab.windowId, { format: "png" });
    }).then((dataUrl) => {
        sendResponse(senderTabId, message.id, "Ok", dataUrl);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Устанавливает cookie через browser.cookies API или виртуальное хранилище.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleSetCookie(senderTabId, message) {
    const ctx = tabContexts.get(senderTabId);
    if (ctx) {
        // Изолированный контекст — сохраняем в виртуальное хранилище.
        const store = virtualCookies.get(ctx.contextId) || [];
        const { name, value, domain, path } = message.payload;
        const idx = store.findIndex(c => c.name === name && c.domain === (domain || ""));
        const cookie = { name, value, domain: domain || "", path: path || "/", secure: false, httpOnly: false };
        if (idx >= 0) store[idx] = cookie; else store.push(cookie);
        virtualCookies.set(ctx.contextId, store);

        // Chrome MV3: обновляем declarativeNetRequest правило для инъекции Cookie header.
        if (browser.declarativeNetRequest?.updateSessionRules) {
            applyTabNetworkRules(senderTabId, ctx);
        }

        sendResponse(senderTabId, message.id, "Ok", null);
        return;
    }

    browser.tabs.get(senderTabId).then((tab) => {
        const { name, value, domain, path } = message.payload;
        const details = {
            url: tab.url,
            name,
            value,
        };
        if (domain) details.domain = domain;
        if (path) details.path = path;
        return browser.cookies.set(details);
    }).then(() => {
        sendResponse(senderTabId, message.id, "Ok", null);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Получает все cookies текущей страницы через browser.cookies API или виртуальное хранилище.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleGetCookies(senderTabId, message) {
    const ctx = tabContexts.get(senderTabId);
    if (ctx) {
        // Изолированный контекст — читаем из виртуального хранилища.
        const store = virtualCookies.get(ctx.contextId) || [];
        const str = store.map(c => `${c.name}=${c.value}`).join("; ");
        sendResponse(senderTabId, message.id, "Ok", str);
        return;
    }

    browser.tabs.get(senderTabId).then((tab) => {
        return browser.cookies.getAll({ url: tab.url });
    }).then((cookies) => {
        // Формат совместимый с document.cookie: "name=value; name2=value2".
        const str = cookies.map(c => `${c.name}=${c.value}`).join("; ");
        sendResponse(senderTabId, message.id, "Ok", str);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

/**
 * Удаляет все cookies текущей страницы через browser.cookies API.
 * @param {number} senderTabId
 * @param {object} message
 */
function handleDeleteCookies(senderTabId, message) {
    const ctx = tabContexts.get(senderTabId);
    if (ctx) {
        // Изолированный контекст — удаляем из виртуального хранилища.
        virtualCookies.set(ctx.contextId, []);
        sendResponse(senderTabId, message.id, "Ok", null);
        return;
    }

    browser.tabs.get(senderTabId).then((tab) => {
        return browser.cookies.getAll({ url: tab.url });
    }).then((cookies) => {
        return Promise.all(cookies.map((c) => {
            const protocol = c.secure ? "https://" : "http://";
            const url = `${protocol}${c.domain.replace(/^\./, "")}${c.path}`;
            return browser.cookies.remove({ url, name: c.name });
        }));
    }).then(() => {
        sendResponse(senderTabId, message.id, "Ok", null);
    }).catch((err) => {
        sendResponse(senderTabId, message.id, "Error", null, err.message);
    });
}

// ─── Изоляция Tab Context ────────────────────────────────────

/**
 * Устанавливает настройки изоляции для вкладки.
 * @param {number} tabId
 * @param {object} message
 */
function handleSetTabContext(tabId, message) {
    const settings = message.payload || {};
    const contextId = settings.contextId || `ctx_${tabId}`;

    tabContexts.set(tabId, { ...settings, contextId });

    if (!virtualCookies.has(contextId)) {
        virtualCookies.set(contextId, []);
    }

    // Применяем сетевые правила (UA, Set-Cookie strip, proxy).
    applyTabNetworkRules(tabId, settings);

    // Firefox: per-tab прокси через proxy.onRequest.
    // Регистрируем listener один раз при первом контексте.
    ensureProxyListener();

    // Chrome: применяем прокси для активной вкладки.
    if (settings.proxy) {
        applyChromiumProxy(settings.proxy);
    }

    // Отправляем настройки контекста в content.js для MAIN world инъекции.
    queueCommand(tabId, message.id + "_ctx", "ApplyContext", {
        contextId,
        userAgent: settings.userAgent || null,
        locale: settings.locale || null,
        timezone: settings.timezone || null,
        languages: settings.languages || null,
        platform: settings.platform || null,
        screen: settings.screen || null,
        webgl: settings.webgl || null,
        canvasNoise: settings.canvasNoise ?? false,
        webrtcPolicy: settings.webrtcPolicy || null,
        geolocation: settings.geolocation || null,
        allowedFonts: settings.allowedFonts || null,
        audioNoise: settings.audioNoise ?? false,
        hardwareConcurrency: settings.hardwareConcurrency || null,
        deviceMemory: settings.deviceMemory || null,
        batteryProtection: settings.batteryProtection ?? false,
        permissionsProtection: settings.permissionsProtection ?? false,
        clientHints: settings.clientHints || null,
        networkInfo: settings.networkInfo || null,
        speechVoices: settings.speechVoices || null,
        mediaDevicesProtection: settings.mediaDevicesProtection ?? false,
        webglParams: settings.webglParams || null,
        doNotTrack: settings.doNotTrack ?? null,
        globalPrivacyControl: settings.globalPrivacyControl ?? null,
        intlSpoofing: settings.intlSpoofing ?? false,
        screenOrientation: settings.screenOrientation || null,
        colorScheme: settings.colorScheme || null,
        reducedMotion: settings.reducedMotion ?? null,
        timerPrecisionMs: settings.timerPrecisionMs ?? null,
        webSocketProtection: settings.webSocketProtection || null,
        webglNoise: settings.webglNoise ?? false,
        storageQuota: settings.storageQuota ?? null,
        keyboardLayout: settings.keyboardLayout || null,
        webrtcIcePolicy: settings.webrtcIcePolicy || null,
        pluginSpoofing: settings.pluginSpoofing ?? false,
        speechRecognitionProtection: settings.speechRecognitionProtection ?? false,
        maxTouchPoints: settings.maxTouchPoints ?? null,
        audioSampleRate: settings.audioSampleRate ?? null,
        audioChannelCount: settings.audioChannelCount ?? null,
        pdfViewerEnabled: settings.pdfViewerEnabled ?? null,
        notificationPermission: settings.notificationPermission || null,
        gamepadProtection: settings.gamepadProtection ?? false,
        hardwareApiProtection: settings.hardwareApiProtection ?? false,
        performanceProtection: settings.performanceProtection ?? false,
        documentReferrer: settings.documentReferrer ?? null,
        historyLength: settings.historyLength ?? null,
        deviceMotionProtection: settings.deviceMotionProtection ?? false,
        ambientLightProtection: settings.ambientLightProtection ?? false,
        connectionRtt: settings.connectionRtt ?? null,
        connectionDownlink: settings.connectionDownlink ?? null,
        mediaCapabilitiesProtection: settings.mediaCapabilitiesProtection ?? false,
        clipboardProtection: settings.clipboardProtection ?? false,
        webShareProtection: settings.webShareProtection ?? false,
        wakeLockProtection: settings.wakeLockProtection ?? false,
        idleDetectionProtection: settings.idleDetectionProtection ?? false,
        credentialProtection: settings.credentialProtection ?? false,
        paymentProtection: settings.paymentProtection ?? false,
        storageEstimateUsage: settings.storageEstimateUsage ?? null,
        fileSystemAccessProtection: settings.fileSystemAccessProtection ?? false,
        beaconProtection: settings.beaconProtection ?? false,
        visibilityStateOverride: settings.visibilityStateOverride ?? null,
        colorDepth: settings.colorDepth ?? null,
        installedAppsProtection: settings.installedAppsProtection ?? false,
        fontMetricsProtection: settings.fontMetricsProtection ?? false,
        crossOriginIsolationOverride: settings.crossOriginIsolationOverride ?? null,
        performanceNowJitter: settings.performanceNowJitter ?? null,
        windowControlsOverlayProtection: settings.windowControlsOverlayProtection ?? false,
        screenOrientationLockProtection: settings.screenOrientationLockProtection ?? false,
        keyboardApiProtection: settings.keyboardApiProtection ?? false,
        usbHidSerialProtection: settings.usbHidSerialProtection ?? false,
        presentationApiProtection: settings.presentationApiProtection ?? false,
        contactsApiProtection: settings.contactsApiProtection ?? false,
        bluetoothProtection: settings.bluetoothProtection ?? false,
        eyeDropperProtection: settings.eyeDropperProtection ?? false,
        multiScreenProtection: settings.multiScreenProtection ?? false,
        inkApiProtection: settings.inkApiProtection ?? false,
        virtualKeyboardProtection: settings.virtualKeyboardProtection ?? false,
        nfcProtection: settings.nfcProtection ?? false,
        fileHandlingProtection: settings.fileHandlingProtection ?? false,
        webXrProtection: settings.webXrProtection ?? false,
        webNnProtection: settings.webNnProtection ?? false,
        schedulingProtection: settings.schedulingProtection ?? false,
        storageAccessProtection: settings.storageAccessProtection ?? false,
        contentIndexProtection: settings.contentIndexProtection ?? false,
        backgroundSyncProtection: settings.backgroundSyncProtection ?? false,
        cookieStoreProtection: settings.cookieStoreProtection ?? false,
        webLocksProtection: settings.webLocksProtection ?? false,
        shapeDetectionProtection: settings.shapeDetectionProtection ?? false,
        webTransportProtection: settings.webTransportProtection ?? false,
        relatedAppsProtection: settings.relatedAppsProtection ?? false,
        digitalGoodsProtection: settings.digitalGoodsProtection ?? false,
        computePressureProtection: settings.computePressureProtection ?? false,
        fileSystemPickerProtection: settings.fileSystemPickerProtection ?? false,
        displayOverrideProtection: settings.displayOverrideProtection ?? false,
        batteryLevelOverride: settings.batteryLevelOverride ?? null,
        pictureInPictureProtection: settings.pictureInPictureProtection ?? false,
        devicePostureProtection: settings.devicePostureProtection ?? false,
        webAuthnProtection: settings.webAuthnProtection ?? false,
        fedCmProtection: settings.fedCmProtection ?? false,
        localFontAccessProtection: settings.localFontAccessProtection ?? false,
        autoplayPolicyProtection: settings.autoplayPolicyProtection ?? false,
        launchHandlerProtection: settings.launchHandlerProtection ?? false,
        topicsApiProtection: settings.topicsApiProtection ?? false,
        attributionReportingProtection: settings.attributionReportingProtection ?? false,
        fencedFrameProtection: settings.fencedFrameProtection ?? false,
        sharedStorageProtection: settings.sharedStorageProtection ?? false,
        privateAggregationProtection: settings.privateAggregationProtection ?? false,
        webOtpProtection: settings.webOtpProtection ?? false,
        webMidiProtection: settings.webMidiProtection ?? false,
        webCodecsProtection: settings.webCodecsProtection ?? false,
        navigationApiProtection: settings.navigationApiProtection ?? false,
        screenCaptureProtection: settings.screenCaptureProtection ?? false,
    });

    sendResponse(tabId, message.id, "Ok", null);
}

/**
 * Применяет сетевые правила для изолированной вкладки.
 * Chrome MV3: declarativeNetRequest (UA override + Set-Cookie strip + Cookie injection).
 * Firefox MV2: обработка в onBeforeSendHeaders/onHeadersReceived listeners.
 * @param {number} tabId
 * @param {object} settings
 */
function applyTabNetworkRules(tabId, settings) {
    if (browser.declarativeNetRequest?.updateSessionRules) {
        // Chrome MV3: одно правило на вкладку — UA override + Set-Cookie strip + Cookie inject.
        const requestHeaders = [];
        const responseHeaders = [];

        if (settings.userAgent) {
            requestHeaders.push({ header: "User-Agent", operation: "set", value: settings.userAgent });
        }

        // Client Hints HTTP headers.
        if (settings.clientHints) {
            const ch = settings.clientHints;
            if (ch.brands) {
                const val = ch.brands.map(b => `"${b.brand}";v="${b.version}"`).join(", ");
                requestHeaders.push({ header: "Sec-CH-UA", operation: "set", value: val });
            }
            if (ch.platform != null) {
                requestHeaders.push({ header: "Sec-CH-UA-Platform", operation: "set", value: `"${ch.platform}"` });
            }
            if (ch.mobile != null) {
                requestHeaders.push({ header: "Sec-CH-UA-Mobile", operation: "set", value: ch.mobile ? "?1" : "?0" });
            }
        }

        // Инъекция Cookie заголовка из виртуального хранилища.
        const ctx = tabContexts.get(tabId);
        if (ctx) {
            const store = virtualCookies.get(ctx.contextId);
            if (store && store.length > 0) {
                const cookieStr = store.map(c => `${c.name}=${c.value}`).join("; ");
                requestHeaders.push({ header: "Cookie", operation: "set", value: cookieStr });
            }
        }

        // Всегда strip Set-Cookie для изолированных вкладок.
        responseHeaders.push({ header: "Set-Cookie", operation: "remove" });

        const rule = {
            id: tabId,
            priority: 1,
            action: { type: "modifyHeaders" },
            condition: {
                tabIds: [tabId],
                resourceTypes: [
                    "main_frame", "sub_frame", "stylesheet", "script",
                    "image", "font", "xmlhttprequest", "ping", "media",
                    "websocket", "other",
                ],
            },
        };

        if (requestHeaders.length > 0) rule.action.requestHeaders = requestHeaders;
        if (responseHeaders.length > 0) rule.action.responseHeaders = responseHeaders;

        browser.declarativeNetRequest.updateSessionRules({
            removeRuleIds: [tabId],
            addRules: [rule],
        }).catch(() => { });
    }
    // Firefox: обработка в onBeforeSendHeaders/onHeadersReceived listeners (ниже).
}

/**
 * Удаляет сетевые правила при отключении вкладки.
 * @param {number} tabId
 */
function removeTabNetworkRules(tabId) {
    if (browser.declarativeNetRequest?.updateSessionRules) {
        browser.declarativeNetRequest.updateSessionRules({
            removeRuleIds: [tabId],
        }).catch(() => { });
    }
}

// ─── Chrome MV3: per-tab proxy через chrome.proxy.settings ──

/** @type {boolean} Флаг: proxy был установлен расширением. */
let chromiumProxySet = false;

/**
 * Устанавливает глобальный proxy для Chromium-браузера.
 * Прокси применяется ко ВСЕМУ трафику. При переключении активной вкладки
 * прокси обновляется на настройки контекста этой вкладки.
 * @param {string} proxyUrl — URL прокси (socks5://host:port, http://host:port).
 */
function applyChromiumProxy(proxyUrl) {
    if (!browser.proxy?.settings) return;

    try {
        const url = new URL(proxyUrl);
        const scheme = url.protocol.replace(":", "");
        const proxyScheme = (scheme === "socks5" || scheme === "socks") ? "socks5"
            : (scheme === "socks4") ? "socks4"
                : (scheme === "https") ? "https" : "http";
        const config = {
            mode: "fixed_servers",
            rules: {
                singleProxy: {
                    scheme: proxyScheme,
                    host: url.hostname,
                    port: parseInt(url.port, 10) || (proxyScheme.startsWith("socks") ? 1080 : 8080),
                },
            },
        };

        browser.proxy.settings.set({ value: config, scope: "regular" }, () => { });
        chromiumProxySet = true;
    } catch {
        // Невалидный URL прокси — игнорируем.
    }
}

/**
 * Сбрасывает proxy на direct (только если мы его устанавливали).
 */
function clearChromiumProxy() {
    if (!chromiumProxySet) return;
    if (!browser.proxy?.settings) return;

    browser.proxy.settings.set({ value: { mode: "direct" }, scope: "regular" }, () => { });
    chromiumProxySet = false;
}

// ─── Firefox: per-tab proxy через proxy.onRequest ───────────

let proxyListenerRegistered = false;

function ensureProxyListener() {
    if (proxyListenerRegistered) return;
    if (!browser.proxy?.onRequest) return;
    proxyListenerRegistered = true;

    browser.proxy.onRequest.addListener(
        (details) => {
            const ctx = tabContexts.get(details.tabId);
            if (!ctx?.proxy) return { type: "direct" };

            try {
                const url = new URL(ctx.proxy);
                const scheme = url.protocol.replace(":", "");
                const type = (scheme === "socks5" || scheme === "socks") ? "socks"
                    : (scheme === "socks4") ? "socks4"
                        : "http";
                const info = {
                    type,
                    host: url.hostname,
                    port: parseInt(url.port, 10) || (type === "socks" ? 1080 : 8080),
                };
                if (type === "socks") info.proxyDNS = true;
                if (url.username) info.username = decodeURIComponent(url.username);
                if (url.password) info.password = decodeURIComponent(url.password);
                return info;
            } catch {
                return { type: "direct" };
            }
        },
        { urls: ["<all_urls>"] },
    );
}

// Chrome MV3: non-blocking webRequest.onHeadersReceived для захвата Set-Cookie перед strip.
// declarativeNetRequest удаляет Set-Cookie из ответа, но webRequest видит оригинальные заголовки.
if (browser.declarativeNetRequest?.updateSessionRules && browser.webRequest?.onHeadersReceived) {
    browser.webRequest.onHeadersReceived.addListener(
        (details) => {
            const ctx = tabContexts.get(details.tabId);
            if (!ctx) return;

            const store = virtualCookies.get(ctx.contextId) || [];
            let captured = false;

            for (const header of (details.responseHeaders || [])) {
                if (header.name.toLowerCase() !== "set-cookie") continue;
                const raw = header.value || "";
                const parts = raw.split(";").map(s => s.trim());
                const [nameValue] = parts;
                if (!nameValue) continue;

                const eqIdx = nameValue.indexOf("=");
                if (eqIdx < 0) continue;

                const name = nameValue.substring(0, eqIdx).trim();
                const value = nameValue.substring(eqIdx + 1).trim();

                let domain = "", path = "/", secure = false, httpOnly = false;
                for (let i = 1; i < parts.length; i++) {
                    const lower = parts[i].toLowerCase();
                    if (lower.startsWith("domain=")) domain = parts[i].substring(7);
                    else if (lower.startsWith("path=")) path = parts[i].substring(5);
                    else if (lower === "secure") secure = true;
                    else if (lower === "httponly") httpOnly = true;
                }

                if (!domain) {
                    try { domain = new URL(details.url).hostname; } catch { /* ignore */ }
                }

                const idx = store.findIndex(c => c.name === name && c.domain === domain);
                const cookie = { name, value, domain, path, secure, httpOnly };
                if (idx >= 0) store[idx] = cookie; else store.push(cookie);
                captured = true;
            }

            if (captured) {
                virtualCookies.set(ctx.contextId, store);
                // Обновляем declarativeNetRequest правило для инъекции Cookie header.
                applyTabNetworkRules(details.tabId, ctx);
            }
        },
        { urls: ["<all_urls>"] },
        ["responseHeaders", "extraHeaders"],
    );
}

// Firefox MV2: блокирующий перехват запросов через sync XHR к BridgeServer.
if (browser.webRequest?.onBeforeRequest) {
    browser.webRequest.onBeforeRequest.addListener(
        (details) => {
            if (!interceptEnabled.has(details.tabId)) return {};
            if (bridgeConfig && details.url.startsWith(`http://${bridgeConfig.host}:${bridgeConfig.port}/`)) return {};

            const requestId = crypto.randomUUID().replaceAll("-", "");
            try {
                const xhr = new XMLHttpRequest();
                xhr.open("POST", `http://${bridgeConfig.host}:${bridgeConfig.port}/intercept`, false);
                xhr.setRequestHeader("Content-Type", "application/json");
                xhr.send(JSON.stringify({
                    requestId,
                    url: details.url,
                    method: details.method,
                    type: details.type,
                    tabId: tabCompositeIds.get(details.tabId) ?? String(details.tabId),
                }));

                if (xhr.status !== 200) return {};
                const response = JSON.parse(xhr.responseText);

                if (response.action === "abort") return { cancel: true };
                if (response.action === "fulfill") return { redirectUrl: response.url };
                if (response.action === "continue") {
                    if (response.headers) {
                        pendingHeaderOverrides.set(String(details.requestId), response.headers);
                    }
                    if (response.url) return { redirectUrl: response.url };
                }
            } catch { /* bridge unavailable — pass through */ }
            return {};
        },
        { urls: ["<all_urls>"] },
        ["blocking"],
    );
}

// Firefox MV2: глобальный listener для подмены User-Agent и инъекции Cookie.
if (browser.webRequest?.onBeforeSendHeaders) {
    browser.webRequest.onBeforeSendHeaders.addListener(
        (details) => {
            const ctx = tabContexts.get(details.tabId);
            if (!ctx) return {};

            let modified = false;

            // UA override.
            if (ctx.userAgent) {
                for (const header of details.requestHeaders) {
                    if (header.name.toLowerCase() === "user-agent") {
                        header.value = ctx.userAgent;
                        modified = true;
                        break;
                    }
                }
            }

            // Inject Cookie header из виртуального хранилища.
            const store = virtualCookies.get(ctx.contextId);
            if (store && store.length > 0) {
                const cookieStr = store
                    .filter(c => {
                        try {
                            const reqUrl = new URL(details.url);
                            return (!c.domain || reqUrl.hostname.endsWith(c.domain.replace(/^\./, "")))
                                && reqUrl.pathname.startsWith(c.path || "/");
                        } catch { return false; }
                    })
                    .map(c => `${c.name}=${c.value}`)
                    .join("; ");

                if (cookieStr) {
                    // Заменяем существующий Cookie header или добавляем новый.
                    const existing = details.requestHeaders.find(h => h.name.toLowerCase() === "cookie");
                    if (existing) {
                        existing.value = cookieStr;
                    } else {
                        details.requestHeaders.push({ name: "Cookie", value: cookieStr });
                    }
                    modified = true;
                }
            }

            // Применяем header overrides от перехватчика запросов.
            const overrides = pendingHeaderOverrides.get(String(details.requestId));
            if (overrides) {
                pendingHeaderOverrides.delete(String(details.requestId));
                for (const [name, value] of Object.entries(overrides)) {
                    const existing = details.requestHeaders.find(h => h.name.toLowerCase() === name.toLowerCase());
                    if (existing) {
                        existing.value = value;
                    } else {
                        details.requestHeaders.push({ name, value });
                    }
                }
                modified = true;
            }

            return modified ? { requestHeaders: details.requestHeaders } : {};
        },
        { urls: ["<all_urls>"] },
        ["blocking", "requestHeaders"],
    );

    // MV2: перехват Set-Cookie из HTTP-ответов + снятие CSP для MAIN world injection.
    browser.webRequest.onHeadersReceived.addListener(
        (details) => {
            let modified = false;

            // Снимаем CSP-заголовки для всех вкладок, чтобы evalInMainWorld
            // мог инжектить <script> теги без блокировки Content-Security-Policy.
            const origLen = details.responseHeaders.length;
            details.responseHeaders = details.responseHeaders.filter(h => {
                const n = h.name.toLowerCase();
                return n !== "content-security-policy" && n !== "content-security-policy-report-only";
            });
            if (details.responseHeaders.length < origLen) modified = true;

            const ctx = tabContexts.get(details.tabId);
            if (!ctx) return modified ? { responseHeaders: details.responseHeaders } : {};
            const store = virtualCookies.get(ctx.contextId) || [];

            details.responseHeaders = details.responseHeaders.filter(header => {
                if (header.name.toLowerCase() !== "set-cookie") return true;

                // Парсим Set-Cookie и сохраняем в виртуальное хранилище.
                const raw = header.value || "";
                const parts = raw.split(";").map(s => s.trim());
                const [nameValue] = parts;
                if (!nameValue) return false;

                const eqIdx = nameValue.indexOf("=");
                if (eqIdx < 0) return false;

                const name = nameValue.substring(0, eqIdx).trim();
                const value = nameValue.substring(eqIdx + 1).trim();

                let domain = "", path = "/", secure = false, httpOnly = false;
                for (let i = 1; i < parts.length; i++) {
                    const lower = parts[i].toLowerCase();
                    if (lower.startsWith("domain=")) domain = parts[i].substring(7);
                    else if (lower.startsWith("path=")) path = parts[i].substring(5);
                    else if (lower === "secure") secure = true;
                    else if (lower === "httponly") httpOnly = true;
                }

                if (!domain) {
                    try { domain = new URL(details.url).hostname; } catch { /* ignore */ }
                }

                const idx = store.findIndex(c => c.name === name && c.domain === domain);
                const cookie = { name, value, domain, path, secure, httpOnly };
                if (idx >= 0) store[idx] = cookie; else store.push(cookie);

                modified = true;
                return false; // Strip Set-Cookie header.
            });

            if (modified) {
                virtualCookies.set(ctx.contextId, store);
                return { responseHeaders: details.responseHeaders };
            }
            return {};
        },
        { urls: ["<all_urls>"] },
        ["blocking", "responseHeaders"],
    );
}



/**
 * Отправляет ответ на команду обратно на .NET-сервер.
 * @param {number} tabId — вкладка-отправитель (чей WebSocket использовать).
 * @param {string} messageId
 * @param {string} status
 * @param {*} payload
 * @param {string|null} error
 */
function sendResponse(tabId, messageId, status, payload, error) {
    const ws = tabSockets.get(tabId);
    if (!ws || ws.readyState !== WebSocket.OPEN) return;

    ws.send(JSON.stringify({
        id: messageId,
        type: "Response",
        tabId: String(tabId),
        status,
        payload: payload ?? null,
        error: error ?? null,
        timestamp: Date.now(),
    }));
}

// ─── Отправка данных обратно в .NET ──────────────────────────

/**
 * Отправляет событие на .NET-сервер.
 * @param {number} tabId
 * @param {string} eventType
 * @param {object} data
 */
function sendEvent(tabId, eventType, data) {
    const ws = tabSockets.get(tabId);
    if (!ws || ws.readyState !== WebSocket.OPEN) return;

    ws.send(JSON.stringify({
        id: crypto.randomUUID().replaceAll("-", ""),
        type: "Event",
        tabId: String(tabId),
        event: eventType,
        payload: data,
        timestamp: Date.now(),
    }));
}

/**
 * Пересылает ответ от content.js на .NET-сервер.
 * @param {number} tabId
 * @param {string} messageId
 * @param {string} status
 * @param {*} payload
 * @param {string|null} error
 */
function forwardResponseToBridge(tabId, messageId, status, payload, error) {
    const ws = tabSockets.get(tabId);
    if (!ws || ws.readyState !== WebSocket.OPEN) return;

    ws.send(JSON.stringify({
        id: messageId,
        type: "Response",
        tabId: String(tabId),
        status: status || "Ok",
        payload: payload ?? null,
        error: error ?? null,
        timestamp: Date.now(),
    }));
}

/**
 * Подключает все существующие вкладки к мосту.
 */
function connectAllExistingTabs() {
    if (!bridgeConfig) return;

    browser.tabs.query({}).then((tabs) => {
        for (const tab of tabs) {
            if (tab.id && !tabSockets.has(tab.id)) {
                connectTab(tab.id);
            }
        }
    });
}

/**
 * Отправляет Pong в ответ на Ping.
 * @param {number} tabId
 * @param {string} messageId
 */
function sendPong(tabId, messageId) {
    const ws = tabSockets.get(tabId);
    if (!ws || ws.readyState !== WebSocket.OPEN) return;

    ws.send(JSON.stringify({
        id: messageId,
        type: "Pong",
        tabId: String(tabId),
        timestamp: Date.now(),
    }));
}

/**
 * Отправляет handshake-сообщение серверу.
 * @param {WebSocket} ws
 * @param {string} compositeId — идентификатор формата "windowId:tabId".
 */
function sendHandshake(ws, compositeId) {
    if (ws.readyState !== WebSocket.OPEN) return;
    ws.send(JSON.stringify({
        id: crypto.randomUUID().replaceAll("-", ""),
        type: "Handshake",
        tabId: compositeId,
        timestamp: Date.now(),
    }));
}
