/**
 * Atom WebDriver Connector — Shadow DOM, Console & Event Interceptor.
 *
 * Запускается в MAIN world до всех скриптов страницы (document_start).
 * Перехватывает:
 *   - Element.prototype.attachShadow → window.__shadowRoots
 *   - console.* → window.__consoleLogs
 *   - EventTarget.prototype.addEventListener → window.__eventListeners + Proxy-обёртка isTrusted
 *
 * Авто-клик Turnstile: при обнаружении click-обработчика на INPUT внутри
 * challenges.cloudflare.com мгновенно включается spoofing и симулируется клик.
 */
(() => {
    "use strict";

    // ─── attachShadow interception ──────────────────────────────

    const origAttachShadow = Element.prototype.attachShadow;

    Element.prototype.attachShadow = function (init) {
        const shadowRoot = origAttachShadow.call(this, init);

        this.__capturedShadowRoot = shadowRoot;

        window.__shadowRoots = window.__shadowRoots || [];
        window.__shadowRoots.push({
            element: this,
            shadowRoot,
            mode: init.mode,
        });

        return shadowRoot;
    };

    // ─── Console interception ───────────────────────────────────

    window.__consoleLogs = [];
    const maxLogs = 500;

    for (const level of ["log", "warn", "error", "info", "debug"]) {
        const orig = console[level];
        console[level] = function (...args) {
            if (window.__consoleLogs.length < maxLogs) {
                window.__consoleLogs.push({
                    level,
                    ts: Date.now(),
                    args: args.map((a) => {
                        try {
                            return typeof a === "object" ? JSON.stringify(a)?.substring(0, 500) : String(a).substring(0, 500);
                        } catch {
                            return String(a).substring(0, 500);
                        }
                    }),
                });
            }
            return orig.apply(this, args);
        };
    }

    // ─── addEventListener interception + isTrusted Proxy ────────

    window.__eventListeners = [];
    window.__clickTargets = [];
    const maxListeners = 200;
    const origAddEventListener = EventTarget.prototype.addEventListener;

    // Типы событий, для которых активируется Proxy-обёртка isTrusted.
    const spoofTypes = new Set([
        "click", "mousedown", "mouseup", "mousemove",
        "mouseenter", "mouseleave", "mouseover", "mouseout",
        "pointerdown", "pointerup", "pointermove",
        "pointerenter", "pointerleave", "pointerover", "pointerout",
        "keydown", "keyup", "keypress",
        "touchstart", "touchend", "touchmove",
    ]);

    // ─── Turnstile auto-click ───────────────────────────────────

    let autoClickScheduled = false;

    function scheduleTurnstileAutoClick() {
        if (autoClickScheduled) return;
        if (location.hostname !== "challenges.cloudflare.com") return;
        autoClickScheduled = true;

        // Ждём один микротик — дать CF зарегистрировать остальные обработчики.
        Promise.resolve().then(() => {
            window.__spoofTrusted = true;

            const targets = window.__clickTargets || [];
            const target =
                targets.find((e) => e.tagName === "INPUT") ||
                targets.find((e) => e.tagName === "DIV") ||
                targets[0];

            if (!target) return;

            let cx = 25,
                cy = 30;
            try {
                const rect = target.getBoundingClientRect();
                if (rect.width > 0) {
                    cx = rect.x + rect.width / 2;
                    cy = rect.y + rect.height / 2;
                }
            } catch {}

            // Симуляция движения мыши к цели.
            for (let i = 1; i <= 5; i++) {
                document.dispatchEvent(
                    new MouseEvent("mousemove", {
                        bubbles: true,
                        cancelable: true,
                        view: window,
                        clientX: (cx * i) / 5 + (Math.random() * 3 - 1.5),
                        clientY: (cy * i) / 5 + (Math.random() * 3 - 1.5),
                    }),
                );
            }

            target.dispatchEvent(
                new MouseEvent("mouseenter", {
                    bubbles: false,
                    cancelable: false,
                    view: window,
                    clientX: cx,
                    clientY: cy,
                }),
            );
            target.dispatchEvent(
                new MouseEvent("mousemove", {
                    bubbles: true,
                    cancelable: true,
                    view: window,
                    clientX: cx,
                    clientY: cy,
                }),
            );

            for (const evType of ["pointerdown", "mousedown", "pointerup", "mouseup", "click"]) {
                target.dispatchEvent(
                    new MouseEvent(evType, {
                        bubbles: true,
                        cancelable: true,
                        view: window,
                        clientX: cx,
                        clientY: cy,
                        button: 0,
                    }),
                );
            }
        });
    }

    // ─── addEventListener override ──────────────────────────────

    EventTarget.prototype.addEventListener = function (type, listener, options) {
        // Логирование.
        if (window.__eventListeners.length < maxListeners) {
            let targetName;
            if (this === window) targetName = "window";
            else if (this === document) targetName = "document";
            else if (this === document.body) targetName = "body";
            else if (this === document.documentElement) targetName = "html";
            else if (this instanceof Element) targetName = this.tagName + (this.id ? "#" + this.id : "");
            else targetName = String(this).substring(0, 60);

            window.__eventListeners.push({
                target: targetName,
                type,
                capture: typeof options === "boolean" ? options : !!options?.capture,
                ts: Date.now(),
            });
        }

        // Сохраняем ссылки на элементы с click-обработчиками.
        if (type === "click" && this instanceof Element) {
            window.__clickTargets.push(this);

            // Авто-клик: INPUT с click-обработчиком в CF iframe → мгновенный клик.
            if (this.tagName === "INPUT") {
                scheduleTurnstileAutoClick();
            }
        }

        // Proxy-обёртка: при window.__spoofTrusted === true синтетические
        // события получают isTrusted = true для обработчиков страницы.
        if (spoofTypes.has(type) && typeof listener === "function") {
            const origListener = listener;
            const wrapped = function (event) {
                if (window.__spoofTrusted && !event.isTrusted) {
                    const proxy = new Proxy(event, {
                        get(target, prop) {
                            if (prop === "isTrusted") return true;
                            const val = Reflect.get(target, prop);
                            return typeof val === "function" ? val.bind(target) : val;
                        },
                        getOwnPropertyDescriptor(target, prop) {
                            if (prop === "isTrusted") {
                                return { value: true, writable: false, enumerable: true, configurable: false };
                            }
                            return Reflect.getOwnPropertyDescriptor(target, prop);
                        },
                    });
                    return origListener.call(this, proxy);
                }
                return origListener.call(this, event);
            };
            return origAddEventListener.call(this, type, wrapped, options);
        }

        return origAddEventListener.call(this, type, listener, options);
    };
})();
