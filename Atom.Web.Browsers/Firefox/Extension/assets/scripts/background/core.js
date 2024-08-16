'use strict';

class Utils {
    static toBase64(str) {
        if (!str) {
            return null;
        }

        return btoa(encodeURIComponent(str).replace(/%([0-9A-F]{2})/g, function toSolidBytes(match, p1) {
            return String.fromCharCode("0x" + p1);
        }));
    }

    static fromBase64(str) {
        if (!str) {
            return null;
        }

        return decodeURIComponent(atob(str).split("").map(function (c) {
            return "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2);
        }).join(""));
    }

    static getRandomColor() {
        const colors = ["blue", "turquoise", "green", "yellow", "orange", "red", "pink", "purple"];
        const randomIndex = Math.floor(Math.random() * colors.length);

        return colors[randomIndex];
    }
}

class Server {
    constructor(name) {
        try {
            this.port = browser.runtime.connectNative(name);
            this.port.onMessage.addListener(x => console.info("received: ", x));
            this.port.onDisconnect.addListener(() => console.info("disconnected: ", this.port.error));
        } catch (e) {
            console.error(e);
        }
    }

    async sendSignalAsync(signal, data) {
        const json = JSON.stringify(data);
        const str = `@atom:${signal}:${Utils.toBase64(json)}`;

        console.info(str);

        try {
            await this.port.postMessage(str);
            console.log(`${signal} sended`);
        } catch (e) {
            console.error(e);
        }
    }

    async sendInstalledSignalAsync() {
        await this.sendSignalAsync("installed");
    }

    async sendWindowCreatedSignalAsync(window) {
        await this.sendSignalAsync("windowCreated", window);
    }

    async sendWindowClosedSignalAsync(windowId) {
        await this.sendSignalAsync("windowClosed", { id: windowId });
    }

    async sendTabCreatedSignalAsync(tab) {
        await this.sendSignalAsync("tabCreated", tab);
    }

    async sendTabClosedSignalAsync(tabId, removeInfo) {
        await this.sendSignalAsync("tabClosed", { id: tabId });
    }
}

let server = new Server("Atom");

// Событие установки расширения.
browser.runtime.onInstalled.addListener(async () => {
    await server.sendInstalledSignalAsync();
});

// Событие открытия окна
browser.windows.onCreated.addListener(async window => {
    await server.sendWindowCreatedSignalAsync(window);
});

// Событие закрытия окна
browser.windows.onRemoved.addListener(async windowId => {
    await server.sendWindowClosedSignalAsync(windowId);
});

// Событие открытия вкладки
browser.tabs.onCreated.addListener(async tab => {
    var ctx = await browser.contextualIdentities.create({ name: "TempContainer" + tab.id, color: Utils.getRandomColor(), icon: "fingerprint" });
    await browser.tabs.update(tab.id, { cookieStoreId: ctx.cookieStoreId });

    await server.sendTabCreatedSignalAsync(tab);
});

// Событие закрытия вкладки
browser.tabs.onRemoved.addListener(async (tabId, removeInfo) => {
    const contexts = await browser.contextualIdentities.query({});

    for (let ctx of contexts) {
        if (ctx.name === "TempContainer" + tabId) {
            browser.contextualIdentities.remove(ctx.cookieStoreId);
        }
    }

    await server.sendTabClosedSignalAsync(tabId, removeInfo);
});

//await browser.tabs.create({});
//const tabs = await browser.tabs.query({ windowId: window.id });
//await browser.tabs.remove(tabs[0].id);
//const windows = await browser.windows.getAll();
//await browser.windows.create();