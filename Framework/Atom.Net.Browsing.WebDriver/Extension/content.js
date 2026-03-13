/**
 * Atom WebDriver Connector — Content Script.
 *
 * Работает в контексте каждой вкладки. Принимает команды от background.js,
 * выполняет их в DOM и возвращает результат обратно.
 *
 * Изоляция: каждый content.js живёт в отдельной вкладке,
 * что делает вкладки полностью независимыми друг от друга.
 *
 * Универсальный скрипт: работает как в Chrome (MV3), так и в Firefox (MV2).
 */

const browser = globalThis.browser ?? globalThis.chrome;

(async () => {
    "use strict";

    /** @type {Map<string, Element>} Карта элементов по временным идентификаторам. */
    const elementRegistry = new Map();
    let elementIdCounter = 0;

    // Проверяем, является ли текущая страница discovery-эндпоинтом BridgeServer.
    if (isDiscoveryPage()) {
        // Ждём загрузки DOM, чтобы <meta> теги стали доступны, и отправляем конфигурацию.
        await new Promise((resolve) => {
            const extractConfig = () => {
                const port = document.querySelector('meta[name="atom-bridge-port"]')?.content;
                const secret = document.querySelector('meta[name="atom-bridge-secret"]')?.content;

                if (port && secret) {
                    browser.runtime.sendMessage({
                        action: "configure",
                        host: "127.0.0.1",
                        port: parseInt(port, 10),
                        secret,
                    });
                }
                resolve();
            };

            if (document.readyState === "loading") {
                document.addEventListener("DOMContentLoaded", extractConfig);
            } else {
                extractConfig();
            }
        });
        // НЕ возвращаемся — discovery-таб работает как полноценный таб.
    }

    // Инициируем подключение к мосту и получаем tabId.
    const connectResult = await browser.runtime.sendMessage({ action: "connect" });
    const myTabId = connectResult?.tabId;

    // ─── Персистентный порт для мгновенной доставки команд (push) ─

    const port = browser.runtime.connect({ name: `tab-${myTabId}` });

    // ─── Порт-based запрос к background.js для выполнения в MAIN world ─
    const pendingMainWorldRequests = new Map();
    let mainWorldRequestCounter = 0;

    function executeInMainWorld(script) {
        return new Promise((resolve) => {
            const requestId = ++mainWorldRequestCounter;
            pendingMainWorldRequests.set(requestId, resolve);
            port.postMessage({ action: "executeInMain", requestId, script });
        });
    }

    port.onMessage.addListener(async (msg) => {
        // Ответ от background.js на executeInMainWorld запрос.
        if (msg.action === "mainWorldResult") {
            const resolve = pendingMainWorldRequests.get(msg.requestId);
            if (resolve) {
                pendingMainWorldRequests.delete(msg.requestId);
                resolve(msg);
            }
            return;
        }

        try {
            const result = await handleCommand(msg.id, msg.command, msg.payload);
            port.postMessage({
                action: "response",
                id: msg.id,
                status: result.status,
                payload: result.payload,
                error: result.error,
            });
        } catch (err) {
            port.postMessage({
                action: "response",
                id: msg.id,
                status: "Error",
                payload: null,
                error: err.message || String(err),
            });
        }
    });

    // ─── Forwarding DOM-событий ──────────────────────────────────

    document.addEventListener("DOMContentLoaded", () => {
        browser.runtime.sendMessage({ action: "event", event: "DomContentLoaded", data: { url: location.href } });
    });

    window.addEventListener("load", () => {
        browser.runtime.sendMessage({ action: "event", event: "PageLoaded", data: { url: location.href } });
    });

    window.addEventListener("error", (e) => {
        browser.runtime.sendMessage({
            action: "event",
            event: "ScriptError",
            data: { message: e.message, filename: e.filename, lineno: e.lineno, colno: e.colno },
        });
    });

    // ─── Обработчик команд ──────────────────────────────────────

    /**
     * @param {string} id
     * @param {string} command
     * @param {object} payload
     * @returns {Promise<{status: string, payload: *, error: string|null}>}
     */
    async function handleCommand(id, command, payload) {
        switch (command) {
            case "ExecuteScript":
                return cmdExecuteScript(payload);

            case "FindElement":
                return cmdFindElement(payload);

            case "FindElements":
                return cmdFindElements(payload);

            case "ElementAction":
                return cmdElementAction(payload);

            case "GetElementProperty":
                return cmdGetElementProperty(payload);

            case "WaitForElement":
                return cmdWaitForElement(payload);

            case "EmulateInput":
                return cmdEmulateInput(payload);

            case "ApplyContext":
                return cmdApplyContext(payload);

            default:
                return error(`Неизвестная команда: ${command}`);
        }
    }

    // ─── Реализация команд ──────────────────────────────────────

    async function cmdExecuteScript(payload) {
        const result = await executeInMainWorld(payload.script);
        if (result?.status === "ok") return ok(result.value ?? null);
        return error(result?.error || "Script execution failed.");
    }

    function cmdFindElement(payload) {
        const el = findSingle(payload.strategy, payload.value);
        if (!el) return Promise.resolve({ status: "NotFound", payload: null, error: null });

        const id = registerElement(el);
        return Promise.resolve(ok(id));
    }

    function cmdFindElements(payload) {
        const elements = findMultiple(payload.strategy, payload.value);
        const ids = elements.map((el) => registerElement(el));
        return Promise.resolve(ok(ids));
    }

    function cmdElementAction(payload) {
        const el = elementRegistry.get(payload.elementId);
        if (!el) return Promise.resolve({ status: "NotFound", payload: null, error: "Элемент не найден в реестре." });

        switch (payload.action) {
            case "Click":
                el.click();
                break;

            case "DoubleClick":
                el.dispatchEvent(new MouseEvent("dblclick", { bubbles: true }));
                break;

            case "Type":
                if ("value" in el) {
                    el.focus();
                    el.value = payload.value || "";
                    el.dispatchEvent(new Event("input", { bubbles: true }));
                    el.dispatchEvent(new Event("change", { bubbles: true }));
                }
                break;

            case "Clear":
                if ("value" in el) {
                    el.value = "";
                    el.dispatchEvent(new Event("input", { bubbles: true }));
                    el.dispatchEvent(new Event("change", { bubbles: true }));
                }
                break;

            case "Hover":
                el.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
                el.dispatchEvent(new MouseEvent("mouseenter", { bubbles: true }));
                break;

            case "Focus":
                el.focus();
                break;

            case "ScrollIntoView":
                el.scrollIntoView({ behavior: "smooth", block: "center" });
                break;

            case "Select":
                if (el.tagName === "SELECT") {
                    el.value = payload.value || "";
                    el.dispatchEvent(new Event("change", { bubbles: true }));
                }
                break;

            case "Check":
                if ("checked" in el) {
                    el.checked = !el.checked;
                    el.dispatchEvent(new Event("change", { bubbles: true }));
                }
                break;

            default:
                return Promise.resolve(error(`Неизвестное действие: ${payload.action}`));
        }

        return Promise.resolve(ok(null));
    }

    function cmdGetElementProperty(payload) {
        const el = elementRegistry.get(payload.elementId);
        if (!el) return Promise.resolve({ status: "NotFound", payload: null, error: "Элемент не найден в реестре." });

        const name = payload.propertyName;

        // Пробуем как свойство DOM, потом как атрибут.
        let value = el[name];
        if (value === undefined) {
            value = el.getAttribute(name);
        }

        return Promise.resolve(ok(value != null ? String(value) : null));
    }

    function cmdWaitForElement(payload) {
        const timeout = payload.timeoutMs || 10000;

        return new Promise((resolve) => {
            const existing = findSingle(payload.strategy, payload.value);
            if (existing) {
                resolve(ok(registerElement(existing)));
                return;
            }

            const observer = new MutationObserver(() => {
                const el = findSingle(payload.strategy, payload.value);
                if (el) {
                    observer.disconnect();
                    clearTimeout(timer);
                    resolve(ok(registerElement(el)));
                }
            });

            observer.observe(document.documentElement, { childList: true, subtree: true });

            const timer = setTimeout(() => {
                observer.disconnect();
                resolve({ status: "Timeout", payload: null, error: "Элемент не появился в течение таймаута." });
            }, timeout);
        });
    }

    function cmdEmulateInput(payload) {
        const target = payload.elementId ? elementRegistry.get(payload.elementId) : document.activeElement;
        if (!target) return Promise.resolve(error("Целевой элемент не найден."));

        if (payload.type === "keyboard") {
            for (const char of payload.text || "") {
                target.dispatchEvent(new KeyboardEvent("keydown", { key: char, bubbles: true }));
                target.dispatchEvent(new KeyboardEvent("keypress", { key: char, bubbles: true }));

                if ("value" in target) {
                    target.value += char;
                    target.dispatchEvent(new Event("input", { bubbles: true }));
                }

                target.dispatchEvent(new KeyboardEvent("keyup", { key: char, bubbles: true }));
            }
        } else if (payload.type === "mouse") {
            const rect = target.getBoundingClientRect();
            const x = payload.x ?? (rect.left + rect.width / 2);
            const y = payload.y ?? (rect.top + rect.height / 2);

            target.dispatchEvent(new MouseEvent("mousemove", { clientX: x, clientY: y, bubbles: true }));
            target.dispatchEvent(new MouseEvent("mousedown", { clientX: x, clientY: y, bubbles: true }));
            target.dispatchEvent(new MouseEvent("mouseup", { clientX: x, clientY: y, bubbles: true }));
            target.dispatchEvent(new MouseEvent("click", { clientX: x, clientY: y, bubbles: true }));
        }

        return Promise.resolve(ok(null));
    }

    // ─── Изоляция контекста (MAIN world injection) ──────────────

    /**
     * Применяет настройки изоляции: подменяет navigator.*, localStorage,
     * sessionStorage, document.cookie, IndexedDB, Cache API и fingerprint
     * в MAIN world страницы.
     * @param {object} payload — { contextId, userAgent, locale, languages, platform, screen, webgl, canvasNoise, webrtcPolicy }
     * @returns {Promise<{status: string, payload: *, error: string|null}>}
     */
    async function cmdApplyContext(payload) {
        const { contextId, userAgent, locale, languages, platform } = payload;

        const overrides = [];

        // ── Navigator overrides ──────────────────────────────
        if (userAgent) {
            overrides.push(`Object.defineProperty(navigator,'userAgent',{get(){return ${JSON.stringify(userAgent)}}});`);
            overrides.push(`Object.defineProperty(navigator,'appVersion',{get(){return ${JSON.stringify(userAgent.replace(/^Mozilla\//, ''))}}});`);
        }
        if (platform) {
            overrides.push(`Object.defineProperty(navigator,'platform',{get(){return ${JSON.stringify(platform)}}});`);
        }
        if (languages) {
            overrides.push(`Object.defineProperty(navigator,'languages',{get(){return ${JSON.stringify(languages)}}});`);
        }
        if (locale) {
            overrides.push(`Object.defineProperty(navigator,'language',{get(){return ${JSON.stringify(locale)}}});`);
        }

        // navigator.webdriver = false (anti-detection).
        overrides.push(`Object.defineProperty(navigator,'webdriver',{get(){return false}});`);

        // ── Storage isolation (localStorage, sessionStorage) ─
        if (contextId) {
            overrides.push(buildStorageShim(contextId));
        }

        // ── document.cookie isolation ────────────────────────
        if (contextId) {
            overrides.push(buildCookieShim());
        }

        // ── IndexedDB / Cache API isolation ──────────────────
        if (contextId) {
            overrides.push(buildIndexedDBShim(contextId));
            overrides.push(buildCacheShim(contextId));
        }

        // ── Screen overrides ─────────────────────────────────
        if (payload.screen) {
            overrides.push(buildScreenOverride(payload.screen));
        }

        // ── Canvas fingerprint noise ─────────────────────────
        if (payload.canvasNoise) {
            overrides.push(buildCanvasNoise(contextId || 'default'));
        }

        // ── WebGL vendor/renderer override ───────────────────
        if (payload.webgl) {
            overrides.push(buildWebGLOverride(payload.webgl));
        }

        // ── WebRTC IP leak prevention ────────────────────────
        if (payload.webrtcPolicy) {
            overrides.push(buildWebRTCOverride(payload.webrtcPolicy));
        }

        // ── Timezone override ────────────────────────────────
        // ── Timezone override ──────────────────────────────
        if (payload.timezone) {
            overrides.push(buildTimezoneOverride(payload.timezone));
        }

        // ── Geolocation override ─────────────────────────
        if (payload.geolocation) {
            overrides.push(buildGeolocationOverride(payload.geolocation));
        }

        // ── Font fingerprint protection ──────────────────
        if (payload.allowedFonts) {
            overrides.push(buildFontFingerprintOverride(payload.allowedFonts));
        }

        // ── AudioContext fingerprint noise ────────────────
        if (payload.audioNoise) {
            overrides.push(buildAudioNoise(contextId || 'default'));
        }

        // ── Hardware concurrency / deviceMemory ──────────
        if (payload.hardwareConcurrency) {
            overrides.push(`Object.defineProperty(navigator,'hardwareConcurrency',{get(){return ${Number(payload.hardwareConcurrency)}}});`);
        }
        if (payload.deviceMemory) {
            overrides.push(`Object.defineProperty(navigator,'deviceMemory',{get(){return ${Number(payload.deviceMemory)}}});`);
        }

        // ── Battery API protection ───────────────────────
        if (payload.batteryProtection) {
            overrides.push(buildBatteryOverride());
        }

        // ── Permissions API spoof ────────────────────────
        if (payload.permissionsProtection) {
            overrides.push(buildPermissionsOverride());
        }

        // ── Client Hints override ────────────────────────
        if (payload.clientHints) {
            overrides.push(buildClientHintsOverride(payload.clientHints));
        }

        // ── Network Information API override ─────────────
        if (payload.networkInfo) {
            overrides.push(buildNetworkInfoOverride(payload.networkInfo));
        }

        // ── Speech Synthesis override ────────────────────
        if (payload.speechVoices) {
            overrides.push(buildSpeechVoicesOverride(payload.speechVoices));
        }

        // ── MediaDevices override ────────────────────────
        if (payload.mediaDevicesProtection) {
            overrides.push(buildMediaDevicesOverride());
        }

        // ── WebGL extended parameters ────────────────────
        if (payload.webglParams) {
            overrides.push(buildWebGLParamsOverride(payload.webglParams));
        }

        // ── Do Not Track / GPC ───────────────────────────
        if (payload.doNotTrack != null) {
            overrides.push(`Object.defineProperty(navigator,'doNotTrack',{get(){return ${JSON.stringify(payload.doNotTrack)}}});`);
        }
        if (payload.globalPrivacyControl != null) {
            overrides.push(`Object.defineProperty(navigator,'globalPrivacyControl',{get(){return ${payload.globalPrivacyControl ? 'true' : 'false'}}});`);
        }

        // ── Intl locale spoofing ─────────────────────────
        if (payload.intlSpoofing && payload.locale) {
            overrides.push(buildIntlSpoofing(payload.locale));
        }

        // ── Screen orientation / matchMedia ──────────────
        if (payload.screenOrientation) {
            overrides.push(buildScreenOrientationOverride(payload.screenOrientation));
        }
        if (payload.colorScheme || payload.reducedMotion != null) {
            overrides.push(buildMatchMediaOverride(payload.colorScheme, payload.reducedMotion));
        }

        // ── Timer precision reduction ────────────────────
        if (payload.timerPrecisionMs) {
            overrides.push(buildTimerPrecisionOverride(payload.timerPrecisionMs));
        }

        // ── WebSocket protection ─────────────────────────
        if (payload.webSocketProtection) {
            overrides.push(buildWebSocketProtection(payload.webSocketProtection));
        }

        // ── WebGL readPixels noise ───────────────────────
        if (payload.webglNoise) {
            overrides.push(buildWebGLNoise(contextId || 'default'));
        }

        // ── Storage quota spoofing ───────────────────────
        if (payload.storageQuota) {
            overrides.push(buildStorageQuotaOverride(payload.storageQuota));
        }

        // ── Keyboard layout fingerprint ──────────────────
        if (payload.keyboardLayout) {
            overrides.push(buildKeyboardLayoutOverride(payload.keyboardLayout));
        }

        // ── WebRTC ICE candidate rewrite ─────────────────
        if (payload.webrtcIcePolicy) {
            overrides.push(buildWebRtcIceRewrite(payload.webrtcIcePolicy));
        }

        // ── Plugin/MimeType spoofing ─────────────────────
        if (payload.pluginSpoofing) {
            overrides.push(buildPluginSpoofing());
        }

        // ── SpeechRecognition protection ───────────────
        if (payload.speechRecognitionProtection) {
            overrides.push(buildSpeechRecognitionProtection());
        }

        // ── Touch / maxTouchPoints ─────────────────────
        if (payload.maxTouchPoints != null) {
            overrides.push(buildMaxTouchPointsOverride(payload.maxTouchPoints));
        }

        // ── AudioContext params ───────────────────────
        if (payload.audioSampleRate || payload.audioChannelCount) {
            overrides.push(buildAudioContextParamsOverride(payload.audioSampleRate, payload.audioChannelCount));
        }

        // ── PDF Viewer ─────────────────────────────────
        if (payload.pdfViewerEnabled != null) {
            overrides.push(`Object.defineProperty(navigator,'pdfViewerEnabled',{get(){return ${payload.pdfViewerEnabled ? 'true' : 'false'}}});`);
        }

        // ── Notification permission ────────────────────
        if (payload.notificationPermission) {
            overrides.push(buildNotificationOverride(payload.notificationPermission));
        }

        // ── Gamepad protection ─────────────────────────
        if (payload.gamepadProtection) {
            overrides.push(buildGamepadProtection());
        }

        // ── Hardware API blocking ──────────────────────
        if (payload.hardwareApiProtection) {
            overrides.push(buildHardwareApiProtection());
        }

        // ── Performance protection ─────────────────────
        if (payload.performanceProtection) {
            overrides.push(buildPerformanceProtection());
        }

        // ── Document referrer ──────────────────────────
        if (payload.documentReferrer != null) {
            overrides.push(`Object.defineProperty(document,'referrer',{get(){return ${JSON.stringify(payload.documentReferrer)}}});`);
        }

        // ── History length ─────────────────────────────
        if (payload.historyLength != null) {
            overrides.push(`Object.defineProperty(history,'length',{get(){return ${payload.historyLength}}});`);
        }

        // ── Device motion / orientation protection ─────
        if (payload.deviceMotionProtection) {
            overrides.push(buildDeviceMotionProtection());
        }

        // ── Ambient light sensor protection ────────────
        if (payload.ambientLightProtection) {
            overrides.push(buildAmbientLightProtection());
        }

        // ── Connection RTT / downlink standalone ───────
        if (payload.connectionRtt != null || payload.connectionDownlink != null) {
            overrides.push(buildConnectionOverride(payload.connectionRtt, payload.connectionDownlink));
        }

        // ── Media Capabilities protection ──────────────
        if (payload.mediaCapabilitiesProtection) {
            overrides.push(buildMediaCapabilitiesProtection());
        }

        // ── Clipboard protection ───────────────────────
        if (payload.clipboardProtection) {
            overrides.push(buildClipboardProtection());
        }

        // ── Web Share API protection ───────────────────
        if (payload.webShareProtection) {
            overrides.push(buildWebShareProtection());
        }

        // ── Wake Lock API protection ───────────────────
        if (payload.wakeLockProtection) {
            overrides.push(buildWakeLockProtection());
        }

        // ── Idle Detection API protection ──────────────
        if (payload.idleDetectionProtection) {
            overrides.push(buildIdleDetectionProtection());
        }

        // ── Credential Management protection ───────────
        if (payload.credentialProtection) {
            overrides.push(buildCredentialProtection());
        }

        // ── Payment Request API protection ─────────────
        if (payload.paymentProtection) {
            overrides.push(buildPaymentProtection());
        }

        // ── Storage estimate usage override ────────────
        if (payload.storageEstimateUsage != null) {
            overrides.push(buildStorageEstimateUsage(payload.storageQuota, payload.storageEstimateUsage));
        }

        // ── File System Access protection ──────────────
        if (payload.fileSystemAccessProtection) {
            overrides.push(buildFileSystemAccessProtection());
        }

        // ── Beacon protection ──────────────────────────
        if (payload.beaconProtection) {
            overrides.push(buildBeaconProtection());
        }

        // ── Visibility state override ──────────────────
        if (payload.visibilityStateOverride) {
            overrides.push(buildVisibilityStateOverride(payload.visibilityStateOverride));
        }

        // ── Color/Pixel depth override ─────────────────
        if (payload.colorDepth != null) {
            overrides.push(`Object.defineProperty(screen,'colorDepth',{get(){return ${payload.colorDepth}}});` +
                `Object.defineProperty(screen,'pixelDepth',{get(){return ${payload.colorDepth}}});`);
        }

        // ── Installed Related Apps protection ──────────
        if (payload.installedAppsProtection) {
            overrides.push(buildInstalledAppsProtection());
        }

        // ── Font Metrics protection ────────────────────
        if (payload.fontMetricsProtection) {
            overrides.push(buildFontMetricsProtection());
        }

        // ── Cross-Origin Isolation override ────────────
        if (payload.crossOriginIsolationOverride != null) {
            overrides.push(buildCrossOriginIsolationOverride(payload.crossOriginIsolationOverride));
        }

        // ── Performance.now() jitter ───────────────────
        if (payload.performanceNowJitter != null) {
            overrides.push(buildPerformanceNowJitter(payload.performanceNowJitter));
        }

        // ── Window Controls Overlay protection ─────────
        if (payload.windowControlsOverlayProtection) {
            overrides.push(buildWindowControlsOverlayProtection());
        }

        // ── Screen Orientation Lock protection ─────────
        if (payload.screenOrientationLockProtection) {
            overrides.push(buildScreenOrientationLockProtection());
        }

        // ── Keyboard API protection ────────────────────
        if (payload.keyboardApiProtection) {
            overrides.push(buildKeyboardApiProtection());
        }

        // ── USB/HID/Serial protection ──────────────────
        if (payload.usbHidSerialProtection) {
            overrides.push(buildUsbHidSerialProtection());
        }

        // ── Presentation API protection ────────────────
        if (payload.presentationApiProtection) {
            overrides.push(buildPresentationApiProtection());
        }

        // ── Contacts API protection ────────────────────
        if (payload.contactsApiProtection) {
            overrides.push(buildContactsApiProtection());
        }

        // ── Bluetooth protection ───────────────────────
        if (payload.bluetoothProtection) {
            overrides.push(buildBluetoothProtection());
        }

        // ── Eye Dropper protection ─────────────────────
        if (payload.eyeDropperProtection) {
            overrides.push(buildEyeDropperProtection());
        }

        // ── Multi-Screen protection ────────────────────
        if (payload.multiScreenProtection) {
            overrides.push(buildMultiScreenProtection());
        }

        // ── Ink API protection ─────────────────────────
        if (payload.inkApiProtection) {
            overrides.push(buildInkApiProtection());
        }

        // ── Virtual Keyboard protection ────────────────
        if (payload.virtualKeyboardProtection) {
            overrides.push(buildVirtualKeyboardProtection());
        }

        // ── Web NFC protection ─────────────────────────
        if (payload.nfcProtection) {
            overrides.push(buildNfcProtection());
        }

        // ── File Handling protection ───────────────────
        if (payload.fileHandlingProtection) {
            overrides.push(buildFileHandlingProtection());
        }

        // ── WebXR protection ───────────────────────────
        if (payload.webXrProtection) {
            overrides.push(buildWebXrProtection());
        }

        // ── Web Neural Network protection ──────────────
        if (payload.webNnProtection) {
            overrides.push(buildWebNnProtection());
        }

        // ── Scheduling API protection ──────────────────
        if (payload.schedulingProtection) {
            overrides.push(buildSchedulingProtection());
        }

        // ── Storage Access API protection ──────────────────
        if (payload.storageAccessProtection) {
            overrides.push(buildStorageAccessProtection());
        }

        // ── Content Index API protection ──────────────────
        if (payload.contentIndexProtection) {
            overrides.push(buildContentIndexProtection());
        }

        // ── Background Sync API protection ──────────────────
        if (payload.backgroundSyncProtection) {
            overrides.push(buildBackgroundSyncProtection());
        }

        // ── Cookie Store API protection ──────────────────
        if (payload.cookieStoreProtection) {
            overrides.push(buildCookieStoreProtection());
        }

        // ── Web Locks API protection ──────────────────
        if (payload.webLocksProtection) {
            overrides.push(buildWebLocksProtection());
        }

        // ── Shape Detection API protection ──────────────────
        if (payload.shapeDetectionProtection) {
            overrides.push(buildShapeDetectionProtection());
        }

        // ── Web Transport API protection ──────────────────
        if (payload.webTransportProtection) {
            overrides.push(buildWebTransportProtection());
        }

        // ── Related Apps API protection ──────────────────
        if (payload.relatedAppsProtection) {
            overrides.push(buildRelatedAppsProtection());
        }

        // ── Digital Goods API protection ──────────────────
        if (payload.digitalGoodsProtection) {
            overrides.push(buildDigitalGoodsProtection());
        }

        // ── Compute Pressure API protection ──────────────────
        if (payload.computePressureProtection) {
            overrides.push(buildComputePressureProtection());
        }

        // ── File System Picker protection ──────────────────
        if (payload.fileSystemPickerProtection) {
            overrides.push(buildFileSystemPickerProtection());
        }

        // ── Display Override API protection ──────────────────
        if (payload.displayOverrideProtection) {
            overrides.push(buildDisplayOverrideProtection());
        }

        // ── Battery Level Override ──────────────────
        if (payload.batteryLevelOverride != null) {
            overrides.push(buildBatteryLevelOverride(payload.batteryLevelOverride));
        }

        // ── Picture-in-Picture protection ──────────────────
        if (payload.pictureInPictureProtection) {
            overrides.push(buildPictureInPictureProtection());
        }

        // ── Device Posture protection ──────────────────
        if (payload.devicePostureProtection) {
            overrides.push(buildDevicePostureProtection());
        }

        // ── WebAuthn protection ──────────────────
        if (payload.webAuthnProtection) {
            overrides.push(buildWebAuthnProtection());
        }

        // ── FedCM protection ──────────────────
        if (payload.fedCmProtection) {
            overrides.push(buildFedCmProtection());
        }

        // ── Local Font Access protection ──────────────────
        if (payload.localFontAccessProtection) {
            overrides.push(buildLocalFontAccessProtection());
        }

        // ── Autoplay Policy protection ──────────────────
        if (payload.autoplayPolicyProtection) {
            overrides.push(buildAutoplayPolicyProtection());
        }

        // ── Launch Handler protection ──────────────────
        if (payload.launchHandlerProtection) {
            overrides.push(buildLaunchHandlerProtection());
        }

        // ── Topics API protection ──────────────────────
        if (payload.topicsApiProtection) {
            overrides.push(buildTopicsApiProtection());
        }

        // ── Attribution Reporting protection ───────────
        if (payload.attributionReportingProtection) {
            overrides.push(buildAttributionReportingProtection());
        }

        // ── Fenced Frame protection ────────────────────
        if (payload.fencedFrameProtection) {
            overrides.push(buildFencedFrameProtection());
        }

        // ── Shared Storage protection ──────────────────
        if (payload.sharedStorageProtection) {
            overrides.push(buildSharedStorageProtection());
        }

        // ── Private Aggregation protection ─────────────
        if (payload.privateAggregationProtection) {
            overrides.push(buildPrivateAggregationProtection());
        }

        // ── Web OTP protection ─────────────────────
        if (payload.webOtpProtection) {
            overrides.push(buildWebOtpProtection());
        }

        // ── Web MIDI protection ────────────────────
        if (payload.webMidiProtection) {
            overrides.push(buildWebMidiProtection());
        }

        // ── WebCodecs protection ───────────────────
        if (payload.webCodecsProtection) {
            overrides.push(buildWebCodecsProtection());
        }

        // ── Navigation API protection ──────────────
        if (payload.navigationApiProtection) {
            overrides.push(buildNavigationApiProtection());
        }

        // ── Screen Capture protection ──────────────
        if (payload.screenCaptureProtection) {
            overrides.push(buildScreenCaptureProtection());
        }

        // ── Inject overrides into MAIN world ─────────
        if (overrides.length === 0) return ok(null);

        const script = overrides.join('\n');
        const result = await browser.runtime.sendMessage({
            action: "executeInMain",
            script,
        });
        if (result?.status === "ok") return ok(null);
        return error(result?.error || "Context application failed.");
    }

    /**
     * Строит localStorage/sessionStorage shim для контекстной изоляции,
     * изолируя данные между контекстами.
     * @param {string} contextId
     * @returns {string}
     */
    function buildStorageShim(contextId) {
        return `(function(){` +
            `var pfx=${JSON.stringify(contextId + '/')};` +
            `function wrap(s){` +
            `var orig={getItem:s.getItem.bind(s),setItem:s.setItem.bind(s),removeItem:s.removeItem.bind(s),clear:s.clear.bind(s),key:s.key.bind(s)};` +
            `Object.defineProperty(s,'getItem',{value(k){return orig.getItem(pfx+k)},configurable:true});` +
            `Object.defineProperty(s,'setItem',{value(k,v){return orig.setItem(pfx+k,v)},configurable:true});` +
            `Object.defineProperty(s,'removeItem',{value(k){return orig.removeItem(pfx+k)},configurable:true});` +
            `Object.defineProperty(s,'key',{value(i){var k=orig.key(i);return k&&k.startsWith(pfx)?k.slice(pfx.length):k},configurable:true});` +
            `Object.defineProperty(s,'clear',{value(){var n=s.length;for(var i=n-1;i>=0;i--){var k=orig.key(i);if(k&&k.startsWith(pfx))orig.removeItem(k)}},configurable:true});` +
            `}` +
            `try{wrap(localStorage)}catch(e){}` +
            `try{wrap(sessionStorage)}catch(e){}` +
            `})();`;
    }

    /**
     * Строит document.cookie override — локальное cookie-хранилище в MAIN world.
     * Изолирует cookies от реального браузерного хранилища.
     * @returns {string}
     */
    function buildCookieShim() {
        return `(function(){` +
            `var _cookies={};` +
            `Object.defineProperty(document,'cookie',{` +
            `get(){return Object.entries(_cookies).map(function(e){return e[0]+'='+e[1]}).join('; ')},` +
            `set(s){` +
            `var parts=s.split(';');var nv=parts[0];if(!nv)return;` +
            `var eq=nv.indexOf('=');if(eq<0)return;` +
            `var name=nv.substring(0,eq).trim();var val=nv.substring(eq+1).trim();` +
            `var expires=null;` +
            `for(var i=1;i<parts.length;i++){` +
            `var p=parts[i].trim().toLowerCase();` +
            `if(p.startsWith('max-age=')&&parseInt(p.substring(8),10)<=0){expires=0}` +
            `if(p.startsWith('expires=')){try{expires=new Date(parts[i].trim().substring(8)).getTime()}catch(e){}}` +
            `}` +
            `if(expires!==null&&expires<=Date.now()){delete _cookies[name]}` +
            `else{_cookies[name]=val}` +
            `},configurable:true});` +
            `})();`;
    }

    /**
     * Строит IndexedDB isolation — prefix для имён баз данных.
     * @param {string} contextId
     * @returns {string}
     */
    function buildIndexedDBShim(contextId) {
        return `(function(){` +
            `var pfx=${JSON.stringify(contextId + '/')};` +
            `var origOpen=indexedDB.open.bind(indexedDB);` +
            `Object.defineProperty(indexedDB,'open',{value:function(name,ver){return origOpen(pfx+name,ver)},configurable:true});` +
            `var origDelete=indexedDB.deleteDatabase.bind(indexedDB);` +
            `Object.defineProperty(indexedDB,'deleteDatabase',{value:function(name){return origDelete(pfx+name)},configurable:true});` +
            `})();`;
    }

    /**
     * Строит Cache API isolation — prefix для имён кэшей.
     * @param {string} contextId
     * @returns {string}
     */
    function buildCacheShim(contextId) {
        return `(function(){` +
            `if(!window.caches)return;` +
            `var pfx=${JSON.stringify(contextId + '/')};` +
            `var origOpen=caches.open.bind(caches);` +
            `Object.defineProperty(caches,'open',{value:function(name){return origOpen(pfx+name)},configurable:true});` +
            `var origDel=caches.delete.bind(caches);` +
            `Object.defineProperty(caches,'delete',{value:function(name){return origDel(pfx+name)},configurable:true});` +
            `var origHas=caches.has.bind(caches);` +
            `Object.defineProperty(caches,'has',{value:function(name){return origHas(pfx+name)},configurable:true});` +
            `})();`;
    }

    /**
     * Строит screen override — подменяет screen.width/height/colorDepth.
     * @param {{width?: number, height?: number, colorDepth?: number}} opts
     * @returns {string}
     */
    function buildScreenOverride(opts) {
        const lines = [];
        if (opts.width != null) {
            lines.push(`Object.defineProperty(screen,'width',{get(){return ${opts.width}}});`);
            lines.push(`Object.defineProperty(screen,'availWidth',{get(){return ${opts.width}}});`);
        }
        if (opts.height != null) {
            lines.push(`Object.defineProperty(screen,'height',{get(){return ${opts.height}}});`);
            lines.push(`Object.defineProperty(screen,'availHeight',{get(){return ${opts.height}}});`);
        }
        if (opts.colorDepth != null) {
            lines.push(`Object.defineProperty(screen,'colorDepth',{get(){return ${opts.colorDepth}}});`);
            lines.push(`Object.defineProperty(screen,'pixelDepth',{get(){return ${opts.colorDepth}}});`);
        }
        return lines.join('\n');
    }

    /**
     * Строит canvas fingerprint noise — детерминированный шум на основе contextId.
     * Подменяет toDataURL, toBlob, getImageData для добавления минимальных артефактов.
     * @param {string} seed — contextId как seed для hash.
     * @returns {string}
     */
    function buildCanvasNoise(seed) {
        return `(function(){` +
            `var seed=${JSON.stringify(seed)};` +
            `function hash(s){var h=0;for(var i=0;i<s.length;i++){h=((h<<5)-h+s.charCodeAt(i))|0}return h}` +
            `var h=hash(seed);` +
            // toDataURL — добавляем невидимый пиксель перед экспортом.
            `var origToDataURL=HTMLCanvasElement.prototype.toDataURL;` +
            `HTMLCanvasElement.prototype.toDataURL=function(){` +
            `var ctx=this.getContext('2d');if(ctx){` +
            `var d=ctx.getImageData(0,0,1,1);d.data[0]^=(h&3);d.data[1]^=((h>>2)&3);ctx.putImageData(d,0,0)}` +
            `return origToDataURL.apply(this,arguments)};` +
            // toBlob — аналогично.
            `var origToBlob=HTMLCanvasElement.prototype.toBlob;` +
            `HTMLCanvasElement.prototype.toBlob=function(cb){` +
            `var ctx=this.getContext('2d');if(ctx){` +
            `var d=ctx.getImageData(0,0,1,1);d.data[0]^=(h&3);d.data[1]^=((h>>2)&3);ctx.putImageData(d,0,0)}` +
            `return origToBlob.apply(this,arguments)};` +
            // getImageData — добавляем шум к первым пикселям.
            `var origGetImageData=CanvasRenderingContext2D.prototype.getImageData;` +
            `CanvasRenderingContext2D.prototype.getImageData=function(){` +
            `var d=origGetImageData.apply(this,arguments);` +
            `if(d.data.length>=4){d.data[0]^=(h&3);d.data[1]^=((h>>2)&3)}return d};` +
            `})();`;
    }

    /**
     * Строит WebGL vendor/renderer override.
     * @param {{vendor?: string, renderer?: string}} opts
     * @returns {string}
     */
    function buildWebGLOverride(opts) {
        const lines = [];
        if (opts.vendor || opts.renderer) {
            lines.push(`(function(){`);
            lines.push(`var origGetParam=WebGLRenderingContext.prototype.getParameter;`);
            lines.push(`function patchGetParam(orig){return function(p){`);
            if (opts.vendor) {
                lines.push(`if(p===0x9245)return ${JSON.stringify(opts.vendor)};`); // UNMASKED_VENDOR_WEBGL
            }
            if (opts.renderer) {
                lines.push(`if(p===0x9246)return ${JSON.stringify(opts.renderer)};`); // UNMASKED_RENDERER_WEBGL
            }
            lines.push(`return orig.call(this,p)}}`);
            lines.push(`WebGLRenderingContext.prototype.getParameter=patchGetParam(origGetParam);`);
            lines.push(`if(window.WebGL2RenderingContext){`);
            lines.push(`var origGetParam2=WebGL2RenderingContext.prototype.getParameter;`);
            lines.push(`WebGL2RenderingContext.prototype.getParameter=patchGetParam(origGetParam2)}`);
            lines.push(`})();`);
        }
        return lines.join('\n');
    }

    /**
     * Строит WebRTC override для предотвращения утечки IP.
     * @param {string} policy — "disable" | "relay-only"
     * @returns {string}
     */
    function buildWebRTCOverride(policy) {
        if (policy === "disable") {
            return `(function(){` +
                `window.RTCPeerConnection=function(){throw new DOMException('RTCPeerConnection disabled','NotAllowedError')};` +
                `window.webkitRTCPeerConnection=window.RTCPeerConnection;` +
                `})();`;
        }
        if (policy === "relay-only") {
            return `(function(){` +
                `var Orig=window.RTCPeerConnection;` +
                `window.RTCPeerConnection=function(cfg,constraints){` +
                `cfg=cfg||{};cfg.iceTransportPolicy='relay';` +
                `return new Orig(cfg,constraints)};` +
                `window.RTCPeerConnection.prototype=Orig.prototype;` +
                `if(window.webkitRTCPeerConnection)window.webkitRTCPeerConnection=window.RTCPeerConnection;` +
                `})();`;
        }
        return '';
    }

    /**
     * Строит timezone override: подменяет Intl.DateTimeFormat, Date.prototype.getTimezoneOffset
     * и toLocale* методы Date для указанной IANA таймзоны.
     * @param {string} tz — IANA timezone (напр. "America/New_York")
     * @returns {string}
     */
    function buildTimezoneOverride(tz) {
        const tzJson = JSON.stringify(tz);
        const lines = [];
        lines.push(`(function(){`);
        lines.push(`var tz=${tzJson};`);

        // --- Intl.DateTimeFormat override ---
        lines.push(`var OrigDTF=Intl.DateTimeFormat;`);
        lines.push(`function PatchedDTF(loc,opts){`);
        lines.push(`  if(!(this instanceof PatchedDTF))return new PatchedDTF(loc,opts);`);
        lines.push(`  opts=Object.assign({},opts||{});`);
        lines.push(`  if(!opts.timeZone)opts.timeZone=tz;`);
        lines.push(`  return new OrigDTF(loc,opts)`);
        lines.push(`}`);
        lines.push(`PatchedDTF.prototype=OrigDTF.prototype;`);
        lines.push(`PatchedDTF.supportedLocalesOf=OrigDTF.supportedLocalesOf.bind(OrigDTF);`);
        lines.push(`Object.defineProperty(PatchedDTF,'name',{value:'DateTimeFormat'});`);
        lines.push(`Intl.DateTimeFormat=PatchedDTF;`);

        // --- Date.prototype.getTimezoneOffset override ---
        // Вычисляем смещение UTC для произвольной даты в целевой таймзоне
        // через formatToParts, учитывая DST.
        lines.push(`var origGTO=Date.prototype.getTimezoneOffset;`);
        lines.push(`Date.prototype.getTimezoneOffset=function(){`);
        lines.push(`  var d=this;`);
        lines.push(`  var fmt=new OrigDTF('en-US',{timeZone:tz,hour12:false,`);
        lines.push(`    year:'numeric',month:'numeric',day:'numeric',`);
        lines.push(`    hour:'numeric',minute:'numeric',second:'numeric'});`);
        lines.push(`  var p=fmt.formatToParts(d);`);
        lines.push(`  var v={};`);
        lines.push(`  for(var i=0;i<p.length;i++){if(p[i].type!=='literal')v[p[i].type]=parseInt(p[i].value,10)}`);
        lines.push(`  var localMs=Date.UTC(v.year,v.month-1,v.day,v.hour===24?0:v.hour,v.minute,v.second);`);
        lines.push(`  if(v.hour===24)localMs+=86400000;`);
        lines.push(`  var utcMs=Date.UTC(d.getUTCFullYear(),d.getUTCMonth(),d.getUTCDate(),`);
        lines.push(`    d.getUTCHours(),d.getUTCMinutes(),d.getUTCSeconds());`);
        lines.push(`  return(utcMs-localMs)/60000`);
        lines.push(`};`);

        // --- toLocaleString / toLocaleDateString / toLocaleTimeString ---
        lines.push(`var origTLS=Date.prototype.toLocaleString;`);
        lines.push(`Date.prototype.toLocaleString=function(loc,opts){`);
        lines.push(`  opts=Object.assign({},opts||{});if(!opts.timeZone)opts.timeZone=tz;`);
        lines.push(`  return origTLS.call(this,loc,opts)};`);

        lines.push(`var origTLDS=Date.prototype.toLocaleDateString;`);
        lines.push(`Date.prototype.toLocaleDateString=function(loc,opts){`);
        lines.push(`  opts=Object.assign({},opts||{});if(!opts.timeZone)opts.timeZone=tz;`);
        lines.push(`  return origTLDS.call(this,loc,opts)};`);

        lines.push(`var origTLTS=Date.prototype.toLocaleTimeString;`);
        lines.push(`Date.prototype.toLocaleTimeString=function(loc,opts){`);
        lines.push(`  opts=Object.assign({},opts||{});if(!opts.timeZone)opts.timeZone=tz;`);
        lines.push(`  return origTLTS.call(this,loc,opts)};`);

        lines.push(`})();`);
        return lines.join('\n');
    }

    /**
     * Строит geolocation override: подменяет navigator.geolocation.getCurrentPosition
     * и watchPosition для возврата заданных координат.
     * @param {{latitude: number, longitude: number, accuracy?: number}} geo
     * @returns {string}
     */
    function buildGeolocationOverride(geo) {
        const lat = Number(geo.latitude);
        const lng = Number(geo.longitude);
        const acc = Number(geo.accuracy) || 10;
        const lines = [];
        lines.push(`(function(){`);
        lines.push(`var pos={coords:{latitude:${lat},longitude:${lng},accuracy:${acc},`);
        lines.push(`altitude:null,altitudeAccuracy:null,heading:null,speed:null},timestamp:Date.now()};`);
        lines.push(`var origGeo=navigator.geolocation;`);
        lines.push(`var fakeGeo={`);
        lines.push(`  getCurrentPosition:function(ok,err,opts){if(typeof ok==='function')setTimeout(function(){ok(pos)},1)},`);
        lines.push(`  watchPosition:function(ok,err,opts){if(typeof ok==='function')setTimeout(function(){ok(pos)},1);return 1},`);
        lines.push(`  clearWatch:function(){}`);
        lines.push(`};`);
        lines.push(`Object.defineProperty(navigator,'geolocation',{get:function(){return fakeGeo},configurable:true});`);
        lines.push(`})();`);
        return lines.join('\n');
    }

    /**
     * Строит font fingerprint protection: ограничивает document.fonts.check()
     * списком разрешённых семейств шрифтов.
     * @param {string[]} fonts — список разрешённых font-family
     * @returns {string}
     */
    function buildFontFingerprintOverride(fonts) {
        const fontsJson = JSON.stringify(fonts.map(f => f.toLowerCase()));
        const lines = [];
        lines.push(`(function(){`);
        lines.push(`var allowed=new Set(${fontsJson});`);

        // Override document.fonts.check() — возвращает true только для разрешённых шрифтов.
        lines.push(`if(document.fonts&&document.fonts.check){`);
        lines.push(`  var origCheck=document.fonts.check.bind(document.fonts);`);
        lines.push(`  document.fonts.check=function(font,text){`);
        lines.push(`    var m=font.match(/(?:^|,|\\s)([\\w\\s-]+)\\s*$/);`);
        lines.push(`    if(m){var fam=m[1].trim().toLowerCase();`);
        lines.push(`      if(!allowed.has(fam))return false;`);
        lines.push(`    }`);
        lines.push(`    return origCheck(font,text)};`);
        lines.push(`}`);

        // Override FontFaceSet.prototype.check на прототипе.
        lines.push(`if(typeof FontFaceSet!=='undefined'&&FontFaceSet.prototype.check){`);
        lines.push(`  var origProtoCheck=FontFaceSet.prototype.check;`);
        lines.push(`  FontFaceSet.prototype.check=function(font,text){`);
        lines.push(`    var m=font.match(/(?:^|,|\\s)([\\w\\s-]+)\\s*$/);`);
        lines.push(`    if(m){var fam=m[1].trim().toLowerCase();`);
        lines.push(`      if(!allowed.has(fam))return false;`);
        lines.push(`    }`);
        lines.push(`    return origProtoCheck.call(this,font,text)};`);
        lines.push(`}`);

        lines.push(`})();`);
        return lines.join('\n');
    }

    /**
     * Строит AudioContext fingerprint noise: добавляет детерминированный шум
     * к результатам OfflineAudioContext.startRendering() на основе contextId.
     * @param {string} contextId
     * @returns {string}
     */
    function buildAudioNoise(contextId) {
        const seed = JSON.stringify(contextId);
        const lines = [];
        lines.push(`(function(){`);
        // Простой seed-хеш для детерминированного шума.
        lines.push(`var s=${seed};`);
        lines.push(`var h=0;for(var i=0;i<s.length;i++){h=((h<<5)-h)+s.charCodeAt(i);h|=0}`);
        lines.push(`function rng(){h^=h<<13;h^=h>>17;h^=h<<5;return(h>>>0)/4294967296}`);

        // Patch OfflineAudioContext.prototype.startRendering.
        lines.push(`if(typeof OfflineAudioContext!=='undefined'){`);
        lines.push(`  var origStart=OfflineAudioContext.prototype.startRendering;`);
        lines.push(`  OfflineAudioContext.prototype.startRendering=function(){`);
        lines.push(`    return origStart.call(this).then(function(buf){`);
        lines.push(`      for(var ch=0;ch<buf.numberOfChannels;ch++){`);
        lines.push(`        var d=buf.getChannelData(ch);`);
        lines.push(`        for(var j=0;j<d.length;j++){d[j]+=1e-7*(rng()-0.5)}`);
        lines.push(`      }`);
        lines.push(`      return buf`);
        lines.push(`    })`);
        lines.push(`  }`);
        lines.push(`}`);
        lines.push(`})();`);
        return lines.join('\n');
    }

    /**
     * Строит Battery API override: navigator.getBattery() возвращает фейковый объект
     * с charging=true, level=1.0 (предотвращает fingerprinting по заряду).
     * @returns {string}
     */
    function buildBatteryOverride() {
        return `(function(){` +
            `var fakeBat={charging:true,chargingTime:0,dischargingTime:Infinity,level:1,` +
            `addEventListener:function(){},removeEventListener:function(){},dispatchEvent:function(){return true}};` +
            `Object.defineProperties(fakeBat,{` +
            `onchargingchange:{get:function(){return null},set:function(){}},` +
            `onchargingtimechange:{get:function(){return null},set:function(){}},` +
            `ondischargingtimechange:{get:function(){return null},set:function(){}},` +
            `onlevelchange:{get:function(){return null},set:function(){}}` +
            `});` +
            `navigator.getBattery=function(){return Promise.resolve(fakeBat)};` +
            `})();`;
    }

    /**
     * Строит Permissions API override: navigator.permissions.query()
     * всегда возвращает {state: "prompt"} для предотвращения fingerprinting.
     * @returns {string}
     */
    function buildPermissionsOverride() {
        return `(function(){` +
            `if(navigator.permissions&&navigator.permissions.query){` +
            `navigator.permissions.query=function(desc){` +
            `return Promise.resolve({state:'prompt',status:'prompt',` +
            `onchange:null,addEventListener:function(){},removeEventListener:function(){},dispatchEvent:function(){return true}})` +
            `}}` +
            `})();`;
    }

    /**
     * Подменяет navigator.userAgentData (Client Hints JS API).
     * @param {object} hints - {brands, fullVersionList, platform, platformVersion, mobile, architecture, model, bitness}
     * @returns {string}
     */
    function buildClientHintsOverride(hints) {
        return `(function(){` +
            `var brands=${JSON.stringify(hints.brands || [])};` +
            `var fullVersionList=${JSON.stringify(hints.fullVersionList || hints.brands || [])};` +
            `var platform=${JSON.stringify(hints.platform || "")};` +
            `var platformVersion=${JSON.stringify(hints.platformVersion || "")};` +
            `var mobile=${hints.mobile ? "true" : "false"};` +
            `var architecture=${JSON.stringify(hints.architecture || "")};` +
            `var model=${JSON.stringify(hints.model || "")};` +
            `var bitness=${JSON.stringify(hints.bitness || "")};` +
            `var uaData={` +
            `brands:brands,mobile:mobile==="true",platform:platform,` +
            `toJSON:function(){return{brands:this.brands,mobile:this.mobile,platform:this.platform}},` +
            `getHighEntropyValues:function(hs){` +
            `var r={brands:brands,mobile:mobile==="true",platform:platform};` +
            `if(hs.indexOf("platformVersion")>=0)r.platformVersion=platformVersion;` +
            `if(hs.indexOf("architecture")>=0)r.architecture=architecture;` +
            `if(hs.indexOf("model")>=0)r.model=model;` +
            `if(hs.indexOf("bitness")>=0)r.bitness=bitness;` +
            `if(hs.indexOf("fullVersionList")>=0)r.fullVersionList=fullVersionList;` +
            `return Promise.resolve(r)` +
            `}};` +
            `Object.defineProperty(navigator,'userAgentData',{get:function(){return uaData},configurable:true})` +
            `})();`;
    }

    /**
     * Подменяет navigator.connection (Network Information API).
     * @param {object} info - {effectiveType, rtt, downlink, saveData}
     * @returns {string}
     */
    function buildNetworkInfoOverride(info) {
        return `(function(){` +
            `var conn={` +
            `effectiveType:${JSON.stringify(info.effectiveType || "4g")},` +
            `rtt:${Number(info.rtt || 50)},` +
            `downlink:${Number(info.downlink || 10)},` +
            `saveData:${info.saveData ? "true" : "false"},` +
            `type:"wifi",` +
            `onchange:null,` +
            `addEventListener:function(){},removeEventListener:function(){},dispatchEvent:function(){return true}` +
            `};` +
            `Object.defineProperty(navigator,'connection',{get:function(){return conn},configurable:true})` +
            `})();`;
    }

    /**
     * Подменяет speechSynthesis.getVoices() фиксированным набором голосов.
     * @param {Array<{name:string,lang:string,localService?:boolean}>} voices
     * @returns {string}
     */
    function buildSpeechVoicesOverride(voices) {
        return `(function(){` +
            `var fakeVoices=${JSON.stringify(voices)}.map(function(v){` +
            `var o={};` +
            `Object.defineProperties(o,{` +
            `name:{get:function(){return v.name}},` +
            `lang:{get:function(){return v.lang}},` +
            `localService:{get:function(){return v.localService!==false}},` +
            `voiceURI:{get:function(){return v.name}},` +
            `default:{get:function(){return false}}` +
            `});return o});` +
            `if(window.speechSynthesis){` +
            `speechSynthesis.getVoices=function(){return fakeVoices};` +
            `speechSynthesis.addEventListener=function(){};` +
            `speechSynthesis.onvoiceschanged=null` +
            `}` +
            `})();`;
    }

    /**
     * Подменяет navigator.mediaDevices.enumerateDevices() стандартным набором.
     * @returns {string}
     */
    function buildMediaDevicesOverride() {
        return `(function(){` +
            `if(navigator.mediaDevices&&navigator.mediaDevices.enumerateDevices){` +
            `var fakeDevices=[` +
            `{deviceId:"default",kind:"audioinput",label:"",groupId:"g1"},` +
            `{deviceId:"default",kind:"videoinput",label:"",groupId:"g2"},` +
            `{deviceId:"default",kind:"audiooutput",label:"",groupId:"g3"}` +
            `];` +
            `navigator.mediaDevices.enumerateDevices=function(){return Promise.resolve(fakeDevices)}` +
            `}` +
            `})();`;
    }

    /**
     * Подменяет расширенные параметры WebGL (MAX_TEXTURE_SIZE и др.).
     * @param {object} params
     * @returns {string}
     */
    function buildWebGLParamsOverride(params) {
        var overrideMap = {};
        if (params.maxTextureSize != null) overrideMap['3379'] = params.maxTextureSize;
        if (params.maxRenderbufferSize != null) overrideMap['34024'] = params.maxRenderbufferSize;
        if (params.maxVaryingVectors != null) overrideMap['36348'] = params.maxVaryingVectors;
        if (params.maxVertexUniformVectors != null) overrideMap['36347'] = params.maxVertexUniformVectors;
        if (params.maxFragmentUniformVectors != null) overrideMap['36349'] = params.maxFragmentUniformVectors;

        var dimsPart = '';
        if (params.maxViewportDims) {
            dimsPart = `if(p===3386)return new Int32Array(${JSON.stringify(params.maxViewportDims)});`;
        }

        return `(function(){` +
            `var map=${JSON.stringify(overrideMap)};` +
            `var origGetParam=WebGLRenderingContext.prototype.getParameter;` +
            `WebGLRenderingContext.prototype.getParameter=function(p){` +
            `${dimsPart}` +
            `if(map[p]!==undefined)return map[p];` +
            `return origGetParam.call(this,p)};` +
            `if(typeof WebGL2RenderingContext!=='undefined'){` +
            `var origGetParam2=WebGL2RenderingContext.prototype.getParameter;` +
            `WebGL2RenderingContext.prototype.getParameter=function(p){` +
            `${dimsPart}` +
            `if(map[p]!==undefined)return map[p];` +
            `return origGetParam2.call(this,p)}}` +
            `})();`;
    }

    function buildIntlSpoofing(locale) {
        var loc = JSON.stringify(locale);
        return `(function(){` +
            `var loc=${loc};` +
            `var ctors=['DateTimeFormat','NumberFormat','ListFormat','RelativeTimeFormat','PluralRules'];` +
            `ctors.forEach(function(name){` +
            `if(!Intl[name])return;` +
            `var Orig=Intl[name];` +
            `Intl[name]=function(locales){` +
            `var args=Array.prototype.slice.call(arguments);` +
            `if(locales===undefined||locales===null||locales==='')args[0]=loc;` +
            `return new(Function.prototype.bind.apply(Orig,[null].concat(args)));` +
            `};` +
            `Intl[name].prototype=Orig.prototype;` +
            `Object.defineProperty(Intl[name],'name',{value:Orig.name});` +
            `Intl[name].supportedLocalesOf=Orig.supportedLocalesOf;` +
            `})` +
            `})();`;
    }

    function buildScreenOrientationOverride(orientation) {
        var angle = orientation.startsWith('landscape') ? 90 : 0;
        return `(function(){` +
            `var type=${JSON.stringify(orientation)};` +
            `var angle=${angle};` +
            `var so=screen.orientation;` +
            `Object.defineProperty(so,'type',{get:function(){return type},configurable:true});` +
            `Object.defineProperty(so,'angle',{get:function(){return angle},configurable:true});` +
            `})();`;
    }

    function buildMatchMediaOverride(colorScheme, reducedMotion) {
        var overrides = {};
        if (colorScheme) {
            overrides['prefers-color-scheme'] = colorScheme;
        }
        if (reducedMotion != null) {
            overrides['prefers-reduced-motion'] = reducedMotion ? 'reduce' : 'no-preference';
        }
        return `(function(){` +
            `var ov=${JSON.stringify(overrides)};` +
            `var origMM=window.matchMedia.bind(window);` +
            `window.matchMedia=function(q){` +
            `var result=origMM(q);` +
            `for(var feat in ov){` +
            `if(q.indexOf(feat)!==-1){` +
            `var val=ov[feat];` +
            `var matches=q.indexOf(val)!==-1;` +
            `Object.defineProperty(result,'matches',{get:function(){return matches},configurable:true});` +
            `break}}` +
            `return result};` +
            `})();`;
    }

    function buildTimerPrecisionOverride(precisionMs) {
        return `(function(){` +
            `var p=${precisionMs};` +
            `var origNow=performance.now.bind(performance);` +
            `performance.now=function(){return Math.round(origNow()/p)*p};` +
            `var origDate=Date.now;` +
            `Date.now=function(){return Math.round(origDate()/p)*p};` +
            `})();`;
    }

    function buildWebSocketProtection(policy) {
        if (policy === 'block') {
            return `(function(){` +
                `window.WebSocket=function(){throw new DOMException('WebSocket is disabled','SecurityError')};` +
                `window.WebSocket.prototype={};` +
                `window.WebSocket.CONNECTING=0;window.WebSocket.OPEN=1;` +
                `window.WebSocket.CLOSING=2;window.WebSocket.CLOSED=3;` +
                `})();`;
        }
        return `(function(){` +
            `var OrigWS=window.WebSocket;` +
            `window.WebSocket=function(url,protocols){` +
            `var a=document.createElement('a');a.href=url;` +
            `var wsOrigin=a.protocol+'//'+a.host;` +
            `if(wsOrigin!==location.origin)` +
            `throw new DOMException('Cross-origin WebSocket blocked','SecurityError');` +
            `return new OrigWS(url,protocols)};` +
            `window.WebSocket.prototype=OrigWS.prototype;` +
            `window.WebSocket.CONNECTING=0;window.WebSocket.OPEN=1;` +
            `window.WebSocket.CLOSING=2;window.WebSocket.CLOSED=3;` +
            `})();`;
    }

    function buildWebGLNoise(contextId) {
        return `(function(){` +
            `var seed=${JSON.stringify(contextId)};` +
            `function hash(s){var h=0;for(var i=0;i<s.length;i++){h=((h<<5)-h)+s.charCodeAt(i);h|=0}return h}` +
            `var h=hash(seed);` +
            `function rng(){h^=h<<13;h^=h>>17;h^=h<<5;return(h&0xff)/255}` +
            `var orig=WebGLRenderingContext.prototype.readPixels;` +
            `WebGLRenderingContext.prototype.readPixels=function(){` +
            `orig.apply(this,arguments);` +
            `var buf=arguments[6];` +
            `if(buf&&buf.length){for(var i=0;i<buf.length;i+=4){` +
            `buf[i]=(buf[i]+(rng()*2-1))|0;` +
            `buf[i+1]=(buf[i+1]+(rng()*2-1))|0;` +
            `buf[i+2]=(buf[i+2]+(rng()*2-1))|0}}};` +
            `if(typeof WebGL2RenderingContext!=='undefined'){` +
            `var orig2=WebGL2RenderingContext.prototype.readPixels;` +
            `WebGL2RenderingContext.prototype.readPixels=function(){` +
            `orig2.apply(this,arguments);` +
            `var buf=arguments[6];` +
            `if(buf&&buf.length){for(var i=0;i<buf.length;i+=4){` +
            `buf[i]=(buf[i]+(rng()*2-1))|0;` +
            `buf[i+1]=(buf[i+1]+(rng()*2-1))|0;` +
            `buf[i+2]=(buf[i+2]+(rng()*2-1))|0}}}}` +
            `})();`;
    }

    function buildStorageQuotaOverride(quota) {
        return `(function(){` +
            `var q=${quota};` +
            `if(navigator.storage&&navigator.storage.estimate){` +
            `navigator.storage.estimate=function(){` +
            `return Promise.resolve({quota:q,usage:0,usageDetails:{}})}}` +
            `})();`;
    }

    function buildKeyboardLayoutOverride(layout) {
        return `(function(){` +
            `var qwerty={KeyA:'a',KeyB:'b',KeyC:'c',KeyD:'d',KeyE:'e',KeyF:'f',` +
            `KeyG:'g',KeyH:'h',KeyI:'i',KeyJ:'j',KeyK:'k',KeyL:'l',` +
            `KeyM:'m',KeyN:'n',KeyO:'o',KeyP:'p',KeyQ:'q',KeyR:'r',` +
            `KeyS:'s',KeyT:'t',KeyU:'u',KeyV:'v',KeyW:'w',KeyX:'x',` +
            `KeyY:'y',KeyZ:'z',Digit0:'0',Digit1:'1',Digit2:'2',Digit3:'3',` +
            `Digit4:'4',Digit5:'5',Digit6:'6',Digit7:'7',Digit8:'8',Digit9:'9',` +
            `Minus:'-',Equal:'=',BracketLeft:'[',BracketRight:']',` +
            `Semicolon:';',Quote:"'",Backquote:'\`',Backslash:'\\\\',` +
            `Comma:',',Period:'.',Slash:'/'};` +
            `var fakeMap={get:function(k){return qwerty[k]},has:function(k){return k in qwerty},` +
            `get size(){return Object.keys(qwerty).length},` +
            `forEach:function(cb){for(var k in qwerty)cb(qwerty[k],k,this)},` +
            `entries:function(){var e=[];for(var k in qwerty)e.push([k,qwerty[k]]);return e[Symbol.iterator]()},` +
            `keys:function(){return Object.keys(qwerty)[Symbol.iterator]()},` +
            `values:function(){return Object.values(qwerty)[Symbol.iterator]()}};` +
            `fakeMap[Symbol.iterator]=fakeMap.entries;` +
            `if(navigator.keyboard&&navigator.keyboard.getLayoutMap){` +
            `navigator.keyboard.getLayoutMap=function(){return Promise.resolve(fakeMap)}}` +
            `})();`;
    }

    function buildWebRtcIceRewrite(policy) {
        if (policy === 'block') {
            return `(function(){` +
                `var Orig=window.RTCPeerConnection;` +
                `if(!Orig)return;` +
                `var origSet=Object.getOwnPropertyDescriptor(Orig.prototype,'onicecandidate');` +
                `Object.defineProperty(Orig.prototype,'onicecandidate',{` +
                `set:function(fn){origSet.set.call(this,function(e){` +
                `if(e.candidate)return;fn.call(this,e)})},` +
                `get:origSet.get,configurable:true});` +
                `var origAdd=Orig.prototype.addEventListener;` +
                `Orig.prototype.addEventListener=function(type,fn,opts){` +
                `if(type==='icecandidate')return origAdd.call(this,type,function(e){` +
                `if(e.candidate)return;fn.call(this,e)},opts);` +
                `return origAdd.call(this,type,fn,opts)};` +
                `})();`;
        }
        return `(function(){` +
            `var Orig=window.RTCPeerConnection;` +
            `if(!Orig)return;` +
            `var privRe=/([0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3})/g;` +
            `function isPrivate(ip){var p=ip.split('.');var a=+p[0];` +
            `return a===10||(a===172&&+p[1]>=16&&+p[1]<=31)||(a===192&&+p[1]===168)||a===127}` +
            `function sanitize(e){if(!e.candidate||!e.candidate.candidate)return e;` +
            `var c=e.candidate.candidate;var m=c.match(privRe);` +
            `if(!m)return e;var changed=false;` +
            `m.forEach(function(ip){if(isPrivate(ip)){c=c.replace(ip,'0.0.0.0');changed=true}});` +
            `if(!changed)return e;` +
            `return Object.create(e,{candidate:{value:Object.create(e.candidate,{candidate:{value:c}})}})}` +
            `var origSet=Object.getOwnPropertyDescriptor(Orig.prototype,'onicecandidate');` +
            `Object.defineProperty(Orig.prototype,'onicecandidate',{` +
            `set:function(fn){origSet.set.call(this,function(e){fn.call(this,sanitize(e))})},` +
            `get:origSet.get,configurable:true});` +
            `var origAdd=Orig.prototype.addEventListener;` +
            `Orig.prototype.addEventListener=function(type,fn,opts){` +
            `if(type==='icecandidate')return origAdd.call(this,type,function(e){fn.call(this,sanitize(e))},opts);` +
            `return origAdd.call(this,type,fn,opts)};` +
            `})();`;
    }

    function buildPluginSpoofing() {
        return `(function(){` +
            `var fakeMime={type:'application/pdf',suffixes:'pdf',description:'Portable Document Format'};` +
            `var fakePlugin={name:'PDF Viewer',description:'Portable Document Format',filename:'internal-pdf-viewer',` +
            `length:1,item:function(i){return i===0?fakeMime:null},` +
            `namedItem:function(n){return n==='application/pdf'?fakeMime:null}};` +
            `fakePlugin[Symbol.iterator]=function(){var i=0;return{next:function(){` +
            `return i<1?{value:fakeMime,done:(i++,false)}:{done:true}}}};` +
            `fakePlugin[0]=fakeMime;` +
            `fakeMime.enabledPlugin=fakePlugin;` +
            `var plugins={length:1,item:function(i){return i===0?fakePlugin:null},` +
            `namedItem:function(n){return n==='PDF Viewer'?fakePlugin:null},refresh:function(){}};` +
            `plugins[0]=fakePlugin;` +
            `plugins[Symbol.iterator]=function(){var i=0;return{next:function(){` +
            `return i<1?{value:fakePlugin,done:(i++,false)}:{done:true}}}};` +
            `Object.defineProperty(navigator,'plugins',{get:function(){return plugins}});` +
            `var mimes={length:1,item:function(i){return i===0?fakeMime:null},` +
            `namedItem:function(n){return n==='application/pdf'?fakeMime:null}};` +
            `mimes[0]=fakeMime;` +
            `mimes[Symbol.iterator]=function(){var i=0;return{next:function(){` +
            `return i<1?{value:fakeMime,done:(i++,false)}:{done:true}}}};` +
            `Object.defineProperty(navigator,'mimeTypes',{get:function(){return mimes}});` +
            `})();`;
    }

    function buildSpeechRecognitionProtection() {
        return `(function(){` +
            `function FakeSR(){this.lang='';this.continuous=false;this.interimResults=false;` +
            `this.maxAlternatives=1;this.grammars=null}` +
            `FakeSR.prototype.start=function(){var self=this;` +
            `setTimeout(function(){if(typeof self.onerror==='function')` +
            `self.onerror({error:'not-allowed',message:'Speech recognition disabled'})},0)};` +
            `FakeSR.prototype.stop=function(){};` +
            `FakeSR.prototype.abort=function(){};` +
            `FakeSR.prototype.addEventListener=function(){};` +
            `FakeSR.prototype.removeEventListener=function(){};` +
            `window.SpeechRecognition=FakeSR;` +
            `window.webkitSpeechRecognition=FakeSR;` +
            `})();`;
    }

    function buildMaxTouchPointsOverride(maxTP) {
        return `(function(){` +
            `Object.defineProperty(navigator,'maxTouchPoints',{get:function(){return ${maxTP}},configurable:true});` +
            (maxTP === 0
                ? `delete window.TouchEvent;delete window.Touch;`
                : '') +
            `})();`;
    }

    function buildAudioContextParamsOverride(sampleRate, channelCount) {
        var parts = [];
        if (sampleRate) {
            parts.push(
                `var origAC=window.AudioContext||window.webkitAudioContext;` +
                `if(origAC){` +
                `var NewAC=function(opts){opts=opts||{};opts.sampleRate=${sampleRate};` +
                `return new origAC(opts)};` +
                `NewAC.prototype=origAC.prototype;` +
                `if(window.AudioContext)window.AudioContext=NewAC;` +
                `if(window.webkitAudioContext)window.webkitAudioContext=NewAC}`
            );
        }
        if (channelCount) {
            parts.push(
                `var origGet=Object.getOwnPropertyDescriptor(AudioNode.prototype,'channelCount');` +
                `if(origGet){Object.defineProperty(AudioDestinationNode.prototype,'maxChannelCount',` +
                `{get:function(){return ${channelCount}},configurable:true})}`
            );
        }
        return `(function(){${parts.join(';')}})();`;
    }

    function buildNotificationOverride(permission) {
        var perm = JSON.stringify(permission);
        return `(function(){` +
            `if(typeof Notification!=='undefined'){` +
            `Object.defineProperty(Notification,'permission',{get:function(){return ${perm}},configurable:true});` +
            `Notification.requestPermission=function(){return Promise.resolve(${perm})}}` +
            `})();`;
    }

    /**
     * Блокирует Gamepad API — navigator.getGamepads возвращает пустой массив,
     * события gamepadconnected/gamepaddisconnected подавляются.
     * @returns {string}
     */
    function buildGamepadProtection() {
        return `(function(){` +
            `navigator.getGamepads=function(){return[]};` +
            `var noop=function(){};` +
            `Object.defineProperty(navigator,'getGamepads',{value:function(){return[]},configurable:true});` +
            `window.addEventListener=new Proxy(window.addEventListener,{apply(t,s,a){` +
            `if(a[0]==='gamepadconnected'||a[0]==='gamepaddisconnected')return;` +
            `return Reflect.apply(t,s,a)}});` +
            `})();`;
    }

    /**
     * Блокирует доступ к аппаратным API: Bluetooth, USB, Serial, HID.
     * Свойства navigator заменяются на undefined.
     * @returns {string}
     */
    function buildHardwareApiProtection() {
        return `(function(){` +
            `var apis=['bluetooth','usb','serial','hid'];` +
            `apis.forEach(function(a){` +
            `Object.defineProperty(navigator,a,{get:function(){return undefined},configurable:true})` +
            `});` +
            `})();`;
    }

    /**
     * Защита PerformanceObserver — фильтрует записи performance API.
     * getEntries/getEntriesByType/getEntriesByName возвращают пустые массивы,
     * PerformanceObserver передаёт пустой список записей.
     * @returns {string}
     */
    function buildPerformanceProtection() {
        return `(function(){` +
            `performance.getEntries=function(){return[]};` +
            `performance.getEntriesByType=function(){return[]};` +
            `performance.getEntriesByName=function(){return[]};` +
            `if(typeof PerformanceObserver!=='undefined'){` +
            `var OrigObs=PerformanceObserver;` +
            `window.PerformanceObserver=function(cb){` +
            `return new OrigObs(function(list,obs){` +
            `var fake={getEntries:function(){return[]},getEntriesByType:function(){return[]},getEntriesByName:function(){return[]}};` +
            `cb(fake,obs)})};` +
            `window.PerformanceObserver.supportedEntryTypes=OrigObs.supportedEntryTypes;` +
            `window.PerformanceObserver.prototype=OrigObs.prototype}` +
            `})();`;
    }

    /**
     * Блокирует DeviceOrientationEvent и DeviceMotionEvent —
     * подавляет события и переопределяет конструктор.
     * @returns {string}
     */
    function buildDeviceMotionProtection() {
        return `(function(){` +
            `window.addEventListener=new Proxy(window.addEventListener,{apply(t,s,a){` +
            `if(a[0]==='deviceorientation'||a[0]==='devicemotion')return;` +
            `return Reflect.apply(t,s,a)}});` +
            `window.DeviceOrientationEvent=function(){};` +
            `window.DeviceMotionEvent=function(){};` +
            `})();`;
    }

    /**
     * Блокирует AmbientLightSensor API — конструктор бросает ошибку.
     * @returns {string}
     */
    function buildAmbientLightProtection() {
        return `(function(){` +
            `if(typeof AmbientLightSensor!=='undefined'){` +
            `window.AmbientLightSensor=function(){throw new DOMException('Sensor is disabled','NotAllowedError')}}` +
            `})();`;
    }

    /**
     * Патчит navigator.connection.rtt и/или downlink без полной замены NetworkInfo.
     * @param {number|null} rtt
     * @param {number|null} downlink
     * @returns {string}
     */
    function buildConnectionOverride(rtt, downlink) {
        var parts = [];
        if (rtt != null) parts.push(`Object.defineProperty(c,'rtt',{get:function(){return ${rtt}},configurable:true})`);
        if (downlink != null) parts.push(`Object.defineProperty(c,'downlink',{get:function(){return ${downlink}},configurable:true})`);
        return `(function(){` +
            `var c=navigator.connection||{};` +
            parts.join(';') + `;` +
            `Object.defineProperty(navigator,'connection',{get:function(){return c},configurable:true})` +
            `})();`;
    }

    /**
     * Подменяет navigator.mediaCapabilities.decodingInfo —
     * всегда возвращает supported/smooth/powerEfficient.
     * @returns {string}
     */
    function buildMediaCapabilitiesProtection() {
        return `(function(){` +
            `if(navigator.mediaCapabilities){` +
            `navigator.mediaCapabilities.decodingInfo=function(){` +
            `return Promise.resolve({supported:true,smooth:true,powerEfficient:true})};` +
            `navigator.mediaCapabilities.encodingInfo=function(){` +
            `return Promise.resolve({supported:true,smooth:true,powerEfficient:true})}` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует navigator.clipboard.read и readText — бросает NotAllowedError.
     * @returns {string}
     */
    function buildClipboardProtection() {
        return `(function(){` +
            `if(navigator.clipboard){` +
            `var deny=function(){return Promise.reject(new DOMException('Clipboard access denied','NotAllowedError'))};` +
            `navigator.clipboard.read=deny;` +
            `navigator.clipboard.readText=deny` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует Web Share API — navigator.share/canShare.
     * @returns {string}
     */
    function buildWebShareProtection() {
        return `(function(){` +
            `navigator.share=function(){return Promise.reject(new DOMException('Share is disabled','NotAllowedError'))};` +
            `navigator.canShare=function(){return false}` +
            `})();`;
    }

    /**
     * Блокирует Wake Lock API — navigator.wakeLock.request.
     * @returns {string}
     */
    function buildWakeLockProtection() {
        return `(function(){` +
            `if(navigator.wakeLock){` +
            `navigator.wakeLock.request=function(){return Promise.reject(new DOMException('Wake Lock is disabled','NotAllowedError'))}` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует Idle Detection API — конструктор IdleDetector бросает ошибку.
     * @returns {string}
     */
    function buildIdleDetectionProtection() {
        return `(function(){` +
            `if(typeof IdleDetector!=='undefined'){` +
            `window.IdleDetector=function(){throw new DOMException('Idle detection is disabled','NotAllowedError')}}` +
            `})();`;
    }

    /**
     * Блокирует Credential Management API — navigator.credentials.get/create.
     * @returns {string}
     */
    function buildCredentialProtection() {
        return `(function(){` +
            `if(navigator.credentials){` +
            `var deny=function(){return Promise.reject(new DOMException('Credentials access denied','NotAllowedError'))};` +
            `navigator.credentials.get=deny;` +
            `navigator.credentials.create=deny` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует Payment Request API — конструктор PaymentRequest бросает ошибку.
     * @returns {string}
     */
    function buildPaymentProtection() {
        return `(function(){` +
            `if(typeof PaymentRequest!=='undefined'){` +
            `window.PaymentRequest=function(){throw new DOMException('Payment is disabled','NotAllowedError')}}` +
            `})();`;
    }

    /**
     * Подменяет navigator.storage.estimate() с кастомным usage + quota.
     * @param {number|null} quota
     * @param {number} usage
     * @returns {string}
     */
    function buildStorageEstimateUsage(quota, usage) {
        var q = quota || 2147483648;
        return `(function(){` +
            `if(navigator.storage&&navigator.storage.estimate){` +
            `navigator.storage.estimate=function(){return Promise.resolve({quota:${q},usage:${usage}})}}` +
            `})();`;
    }

    /**
     * Блокирует File System Access API — showOpenFilePicker, showSaveFilePicker, showDirectoryPicker.
     * @returns {string}
     */
    function buildFileSystemAccessProtection() {
        return `(function(){` +
            `var deny=function(){return Promise.reject(new DOMException('File access denied','NotAllowedError'))};` +
            `window.showOpenFilePicker=deny;` +
            `window.showSaveFilePicker=deny;` +
            `window.showDirectoryPicker=deny` +
            `})();`;
    }

    /**
     * Блокирует navigator.sendBeacon — тихо возвращает true.
     * @returns {string}
     */
    function buildBeaconProtection() {
        return `(function(){` +
            `navigator.sendBeacon=function(){return true}` +
            `})();`;
    }

    /**
     * Подменяет document.visibilityState и блокирует visibilitychange.
     * @param {string} state
     * @returns {string}
     */
    function buildVisibilityStateOverride(state) {
        var s = JSON.stringify(state);
        return `(function(){` +
            `Object.defineProperty(document,'visibilityState',{get:function(){return ${s}},configurable:true});` +
            `Object.defineProperty(document,'hidden',{get:function(){return ${s}==='hidden'},configurable:true});` +
            `document.addEventListener=new Proxy(document.addEventListener,{apply(t,s,a){` +
            `if(a[0]==='visibilitychange')return;` +
            `return Reflect.apply(t,s,a)}})` +
            `})();`;
    }

    /**
     * Блокирует navigator.getInstalledRelatedApps — возвращает пустой массив.
     * @returns {string}
     */
    function buildInstalledAppsProtection() {
        return `(function(){` +
            `if(navigator.getInstalledRelatedApps){` +
            `Object.defineProperty(navigator,'getInstalledRelatedApps',{value:function(){return Promise.resolve([])},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Нормализует метрики шрифтов через getComputedStyle, скрывая уникальные значения.
     * @returns {string}
     */
    function buildFontMetricsProtection() {
        return `(function(){` +
            `var origGCS=window.getComputedStyle;` +
            `window.getComputedStyle=function(el,pseudo){` +
            `var s=origGCS.call(window,el,pseudo);` +
            `var fp=['fontKerning','fontVariantLigatures','fontFeatureSettings','fontVariationSettings'];` +
            `return new Proxy(s,{get(t,p){` +
            `if(fp.includes(p)){return 'normal'}` +
            `var v=typeof t[p]==='function'?t[p].bind(t):t[p];return v}})` +
            `}` +
            `})();`;
    }

    /**
     * Переопределяет window.crossOriginIsolated и блокирует SharedArrayBuffer при false.
     * @param {boolean} isolated
     * @returns {string}
     */
    function buildCrossOriginIsolationOverride(isolated) {
        var code = `Object.defineProperty(window,'crossOriginIsolated',{get(){return ${!!isolated}},configurable:true});`;
        if (!isolated) {
            code += `Object.defineProperty(window,'SharedArrayBuffer',{get(){return undefined},configurable:true});`;
        }
        return `(function(){${code}})();`;
    }

    /**
     * Добавляет рандомный jitter к performance.now().
     * @param {number} jitter — максимальная дельта в мс
     * @returns {string}
     */
    function buildPerformanceNowJitter(jitter) {
        return `(function(){` +
            `var origNow=performance.now.bind(performance);` +
            `var j=${Number(jitter)};` +
            `Object.defineProperty(performance,'now',{value:function(){return origNow()+(Math.random()*j*2-j)},configurable:true})` +
            `})();`;
    }

    /**
     * Скрывает navigator.windowControlsOverlay API.
     * @returns {string}
     */
    function buildWindowControlsOverlayProtection() {
        return `(function(){` +
            `if(navigator.windowControlsOverlay){` +
            `Object.defineProperty(navigator,'windowControlsOverlay',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует screen.orientation.lock() — всегда reject.
     * @returns {string}
     */
    function buildScreenOrientationLockProtection() {
        return `(function(){` +
            `if(screen.orientation&&screen.orientation.lock){` +
            `Object.defineProperty(screen.orientation,'lock',{value:function(){return Promise.reject(new DOMException('screen orientation lock is blocked','NotAllowedError'))},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует navigator.keyboard.getLayoutMap() — возвращает пустую Map.
     * @returns {string}
     */
    function buildKeyboardApiProtection() {
        return `(function(){` +
            `if(navigator.keyboard){` +
            `Object.defineProperty(navigator.keyboard,'getLayoutMap',{value:function(){return Promise.resolve(new Map())},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.usb, navigator.hid, navigator.serial.
     * @returns {string}
     */
    function buildUsbHidSerialProtection() {
        return `(function(){` +
            `['usb','hid','serial'].forEach(function(p){` +
            `if(p in navigator){Object.defineProperty(navigator,p,{get(){return undefined},configurable:true})}` +
            `})` +
            `})();`;
    }

    /**
     * Скрывает navigator.presentation API.
     * @returns {string}
     */
    function buildPresentationApiProtection() {
        return `(function(){` +
            `if(navigator.presentation){` +
            `Object.defineProperty(navigator,'presentation',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.contacts API.
     * @returns {string}
     */
    function buildContactsApiProtection() {
        return `(function(){` +
            `if(navigator.contacts){` +
            `Object.defineProperty(navigator,'contacts',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.bluetooth API.
     * @returns {string}
     */
    function buildBluetoothProtection() {
        return `(function(){` +
            `if(navigator.bluetooth){` +
            `Object.defineProperty(navigator,'bluetooth',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует EyeDropper API — конструктор бросает ошибку.
     * @returns {string}
     */
    function buildEyeDropperProtection() {
        return `(function(){` +
            `if(window.EyeDropper){` +
            `Object.defineProperty(window,'EyeDropper',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует window.getScreenDetails() (Multi-Screen Window Placement API).
     * @returns {string}
     */
    function buildMultiScreenProtection() {
        return `(function(){` +
            `if(window.getScreenDetails){` +
            `Object.defineProperty(window,'getScreenDetails',{value:function(){return Promise.reject(new DOMException('getScreenDetails is blocked','NotAllowedError'))},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.ink API.
     * @returns {string}
     */
    function buildInkApiProtection() {
        return `(function(){` +
            `if(navigator.ink){` +
            `Object.defineProperty(navigator,'ink',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.virtualKeyboard API.
     * @returns {string}
     */
    function buildVirtualKeyboardProtection() {
        return `(function(){` +
            `if(navigator.virtualKeyboard){` +
            `Object.defineProperty(navigator,'virtualKeyboard',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.nfc (Web NFC API).
     * @returns {string}
     */
    function buildNfcProtection() {
        return `(function(){` +
            `if('NDEFReader' in window){` +
            `Object.defineProperty(window,'NDEFReader',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Блокирует window.launchQueue (File Handling API).
     * @returns {string}
     */
    function buildFileHandlingProtection() {
        return `(function(){` +
            `if(window.launchQueue){` +
            `Object.defineProperty(window,'launchQueue',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.xr (WebXR API).
     * @returns {string}
     */
    function buildWebXrProtection() {
        return `(function(){` +
            `if(navigator.xr){` +
            `Object.defineProperty(navigator,'xr',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.ml (Web Neural Network API).
     * @returns {string}
     */
    function buildWebNnProtection() {
        return `(function(){` +
            `if(navigator.ml){` +
            `Object.defineProperty(navigator,'ml',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * Скрывает navigator.scheduling (Scheduling API).
     * @returns {string}
     */
    function buildSchedulingProtection() {
        return `(function(){` +
            `if(navigator.scheduling){` +
            `Object.defineProperty(navigator,'scheduling',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildStorageAccessProtection() {
        return `(function(){` +
            `if(document.requestStorageAccess){` +
            `document.requestStorageAccess=function(){return Promise.reject(new DOMException('Storage access denied','NotAllowedError'))}` +
            `}` +
            `if(document.hasStorageAccess){` +
            `document.hasStorageAccess=function(){return Promise.resolve(false)}` +
            `}` +
            `})();`;
    }

    function buildContentIndexProtection() {
        return `(function(){` +
            `if('ServiceWorkerRegistration' in window){` +
            `var p=ServiceWorkerRegistration.prototype;` +
            `if('index' in p){Object.defineProperty(p,'index',{get(){return undefined},configurable:true})}` +
            `}` +
            `})();`;
    }

    function buildBackgroundSyncProtection() {
        return `(function(){` +
            `if('ServiceWorkerRegistration' in window){` +
            `var p=ServiceWorkerRegistration.prototype;` +
            `if('sync' in p){Object.defineProperty(p,'sync',{get(){return undefined},configurable:true})}` +
            `if('periodicSync' in p){Object.defineProperty(p,'periodicSync',{get(){return undefined},configurable:true})}` +
            `}` +
            `})();`;
    }

    function buildCookieStoreProtection() {
        return `(function(){` +
            `if('cookieStore' in window){` +
            `Object.defineProperty(window,'cookieStore',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildWebLocksProtection() {
        return `(function(){` +
            `if(navigator.locks){` +
            `Object.defineProperty(navigator,'locks',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildShapeDetectionProtection() {
        return `(function(){` +
            `['BarcodeDetector','FaceDetector','TextDetector'].forEach(function(c){` +
            `if(c in window){Object.defineProperty(window,c,{get(){return undefined},configurable:true})}` +
            `})` +
            `})();`;
    }

    function buildWebTransportProtection() {
        return `(function(){` +
            `if('WebTransport' in window){` +
            `Object.defineProperty(window,'WebTransport',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildRelatedAppsProtection() {
        return `(function(){` +
            `if(navigator.getInstalledRelatedApps){` +
            `navigator.getInstalledRelatedApps=function(){return Promise.resolve([])}` +
            `}` +
            `})();`;
    }

    function buildDigitalGoodsProtection() {
        return `(function(){` +
            `if(window.getDigitalGoodsService){` +
            `Object.defineProperty(window,'getDigitalGoodsService',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildComputePressureProtection() {
        return `(function(){` +
            `if('PressureObserver' in window){` +
            `Object.defineProperty(window,'PressureObserver',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildFileSystemPickerProtection() {
        return `(function(){` +
            `['showDirectoryPicker','showOpenFilePicker','showSaveFilePicker'].forEach(function(m){` +
            `if(m in window){window[m]=function(){return Promise.reject(new DOMException('Picker blocked','AbortError'))}}` +
            `})` +
            `})();`;
    }

    function buildDisplayOverrideProtection() {
        return `(function(){` +
            `if(navigator.windowControlsOverlay){` +
            `Object.defineProperty(navigator,'windowControlsOverlay',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildBatteryLevelOverride(level) {
        return `(function(){` +
            `var lvl=${JSON.stringify(level)};` +
            `if(navigator.getBattery){` +
            `var fake={level:lvl,charging:true,chargingTime:0,dischargingTime:Infinity,` +
            `addEventListener:function(){},removeEventListener:function(){},dispatchEvent:function(){return true}};` +
            `navigator.getBattery=function(){return Promise.resolve(fake)}` +
            `}` +
            `})();`;
    }

    function buildPictureInPictureProtection() {
        return `(function(){` +
            `['DocumentPictureInPicture','PictureInPictureWindow'].forEach(function(c){` +
            `if(c in window){Object.defineProperty(window,c,{get(){return undefined},configurable:true})}` +
            `});` +
            `if('pictureInPictureEnabled' in document){` +
            `Object.defineProperty(document,'pictureInPictureEnabled',{get(){return false},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildDevicePostureProtection() {
        return `(function(){` +
            `if(navigator.devicePosture){` +
            `Object.defineProperty(navigator,'devicePosture',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildWebAuthnProtection() {
        return `(function(){` +
            `if(navigator.credentials){` +
            `var orig=navigator.credentials.create.bind(navigator.credentials);` +
            `navigator.credentials.create=function(o){` +
            `if(o&&o.publicKey)return Promise.reject(new DOMException('WebAuthn blocked','NotAllowedError'));` +
            `return orig(o)};` +
            `var origGet=navigator.credentials.get.bind(navigator.credentials);` +
            `navigator.credentials.get=function(o){` +
            `if(o&&o.publicKey)return Promise.reject(new DOMException('WebAuthn blocked','NotAllowedError'));` +
            `return origGet(o)}` +
            `}` +
            `})();`;
    }

    function buildFedCmProtection() {
        return `(function(){` +
            `if(navigator.credentials){` +
            `var orig=navigator.credentials.get.bind(navigator.credentials);` +
            `navigator.credentials.get=function(o){` +
            `if(o&&o.identity)return Promise.reject(new DOMException('FedCM blocked','NotAllowedError'));` +
            `return orig(o)}` +
            `}` +
            `})();`;
    }

    function buildLocalFontAccessProtection() {
        return `(function(){` +
            `if(window.queryLocalFonts){` +
            `window.queryLocalFonts=function(){return Promise.resolve([])}` +
            `}` +
            `})();`;
    }

    function buildAutoplayPolicyProtection() {
        return `(function(){` +
            `if(navigator.getAutoplayPolicy){` +
            `navigator.getAutoplayPolicy=function(){return 'allowed'}` +
            `}` +
            `})();`;
    }

    function buildLaunchHandlerProtection() {
        return `(function(){` +
            `if('LaunchParams' in window){` +
            `Object.defineProperty(window,'LaunchParams',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildTopicsApiProtection() {
        return `(function(){` +
            `if(document.browsingTopics){` +
            `document.browsingTopics=function(){return Promise.resolve([])}` +
            `}` +
            `})();`;
    }

    function buildAttributionReportingProtection() {
        return `(function(){` +
            `if(window.AttributionReportingRequestOptions){` +
            `Object.defineProperty(window,'AttributionReportingRequestOptions',{get(){return undefined},configurable:true})` +
            `}` +
            `var dp=Object.defineProperty;` +
            `try{dp(HTMLAnchorElement.prototype,'attributionSrc',{get(){return ''},set(){},configurable:true})}catch(e){}` +
            `try{dp(HTMLImageElement.prototype,'attributionSrc',{get(){return ''},set(){},configurable:true})}catch(e){}` +
            `try{dp(HTMLScriptElement.prototype,'attributionSrc',{get(){return ''},set(){},configurable:true})}catch(e){}` +
            `})();`;
    }

    function buildFencedFrameProtection() {
        return `(function(){` +
            `if(window.HTMLFencedFrameElement){` +
            `Object.defineProperty(window,'HTMLFencedFrameElement',{get(){return undefined},configurable:true})` +
            `}` +
            `if(window.fence){` +
            `Object.defineProperty(window,'fence',{get(){return undefined},configurable:true})` +
            `}` +
            `if(window.FencedFrameConfig){` +
            `Object.defineProperty(window,'FencedFrameConfig',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildSharedStorageProtection() {
        return `(function(){` +
            `if(window.sharedStorage){` +
            `Object.defineProperty(window,'sharedStorage',{get(){return undefined},configurable:true})` +
            `}` +
            `if(window.SharedStorage){` +
            `Object.defineProperty(window,'SharedStorage',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildPrivateAggregationProtection() {
        return `(function(){` +
            `if(window.privateAggregation){` +
            `Object.defineProperty(window,'privateAggregation',{get(){return undefined},configurable:true})` +
            `}` +
            `if(window.PrivateAggregation){` +
            `Object.defineProperty(window,'PrivateAggregation',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildWebOtpProtection() {
        return `(function(){` +
            `if(window.OTPCredential){` +
            `Object.defineProperty(window,'OTPCredential',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildWebMidiProtection() {
        return `(function(){` +
            `if(navigator.requestMIDIAccess){` +
            `Object.defineProperty(navigator,'requestMIDIAccess',{value:()=>Promise.reject(new DOMException('MIDI access denied','NotAllowedError')),configurable:true})` +
            `}` +
            `})();`;
    }

    function buildWebCodecsProtection() {
        return `(function(){` +
            `var targets=['VideoEncoder','VideoDecoder','AudioEncoder','AudioDecoder','EncodedVideoChunk','EncodedAudioChunk','VideoFrame','AudioData'];` +
            `targets.forEach(function(n){` +
            `if(window[n]){Object.defineProperty(window,n,{get(){return undefined},configurable:true})}` +
            `});` +
            `})();`;
    }

    function buildNavigationApiProtection() {
        return `(function(){` +
            `if(window.navigation){` +
            `Object.defineProperty(window,'navigation',{get(){return undefined},configurable:true})` +
            `}` +
            `if(window.NavigateEvent){` +
            `Object.defineProperty(window,'NavigateEvent',{get(){return undefined},configurable:true})` +
            `}` +
            `if(window.NavigationTransition){` +
            `Object.defineProperty(window,'NavigationTransition',{get(){return undefined},configurable:true})` +
            `}` +
            `})();`;
    }

    function buildScreenCaptureProtection() {
        return `(function(){` +
            `if(navigator.mediaDevices&&navigator.mediaDevices.getDisplayMedia){` +
            `Object.defineProperty(navigator.mediaDevices,'getDisplayMedia',{value:()=>Promise.reject(new DOMException('Screen capture denied','NotAllowedError')),configurable:true})` +
            `}` +
            `})();`;
    }

    /**
     * @param {string} strategy
     * @param {string} value
     * @returns {Element|null}
     */
    function findSingle(strategy, value) {
        switch (strategy) {
            case "Css":
                return document.querySelector(value);
            case "XPath":
                return document.evaluate(value, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
            case "Id":
                return document.getElementById(value);
            case "Text":
                return findByText(value);
            case "Name":
                return document.querySelector(`[name="${CSS.escape(value)}"]`);
            case "TagName":
                return document.querySelector(value);
            default:
                return null;
        }
    }

    /**
     * @param {string} strategy
     * @param {string} value
     * @returns {Element[]}
     */
    function findMultiple(strategy, value) {
        switch (strategy) {
            case "Css":
                return [...document.querySelectorAll(value)];
            case "XPath": {
                const result = document.evaluate(value, document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
                const elements = [];
                for (let i = 0; i < result.snapshotLength; i++) {
                    elements.push(result.snapshotItem(i));
                }
                return elements;
            }
            case "Id": {
                const el = document.getElementById(value);
                return el ? [el] : [];
            }
            case "Text":
                return findAllByText(value);
            case "Name":
                return [...document.querySelectorAll(`[name="${CSS.escape(value)}"]`)];
            case "TagName":
                return [...document.querySelectorAll(value)];
            default:
                return [];
        }
    }

    /**
     * @param {string} text
     * @returns {Element|null}
     */
    function findByText(text) {
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
            if (walker.currentNode.textContent?.trim() === text) {
                return walker.currentNode.parentElement;
            }
        }
        return null;
    }

    /**
     * @param {string} text
     * @returns {Element[]}
     */
    function findAllByText(text) {
        const results = [];
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
            if (walker.currentNode.textContent?.trim() === text && walker.currentNode.parentElement) {
                results.push(walker.currentNode.parentElement);
            }
        }
        return results;
    }

    // ─── Реестр элементов ────────────────────────────────────────

    /**
     * Регистрирует DOM-элемент и возвращает его идентификатор.
     * @param {Element} element
     * @returns {string}
     */
    function registerElement(element) {
        // Проверяем, не зарегистрирован ли уже.
        for (const [id, el] of elementRegistry) {
            if (el === element) return id;
        }

        const id = `el_${++elementIdCounter}`;
        elementRegistry.set(id, element);
        return id;
    }

    // ─── Хелперы ─────────────────────────────────────────────────

    function ok(payload) {
        return { status: "Ok", payload, error: null };
    }

    function error(message) {
        return { status: "Error", payload: null, error: message };
    }

    /**
     * Определяет, является ли текущая страница discovery-эндпоинтом BridgeServer.
     * @returns {boolean}
     */
    function isDiscoveryPage() {
        try {
            const url = new URL(location.href);
            return url.hostname === "127.0.0.1" && url.pathname === "/";
        } catch {
            return false;
        }
    }
})();
