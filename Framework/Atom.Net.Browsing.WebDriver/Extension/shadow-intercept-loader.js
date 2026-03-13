/**
 * Atom WebDriver Connector — Shadow Intercept Loader.
 *
 * MV2 не поддерживает world: "MAIN" в content_scripts.
 * Этот лоадер (ISOLATED world) инжектирует shadow-intercept.js
 * в MAIN world через <script src="...">.
 */
(() => {
    const b = typeof browser !== "undefined" && browser.runtime ? browser : chrome;
    const s = document.createElement("script");
    s.src = b.runtime.getURL("shadow-intercept.js");
    (document.head || document.documentElement).appendChild(s);
    s.onload = () => s.remove();
})();
