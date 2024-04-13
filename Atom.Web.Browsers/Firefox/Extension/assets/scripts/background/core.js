'use strict';

class Server {
    constructor(name) {
        this.port = browser.runtime.connectNative(name);
    }

    async sendAsync(data) {
        const json = JSON.stringify(data);
        await this.port.postMessage(json);
    }

    async sendSignalAsync(signal, data) {
        await this.sendAsync({
            signal: signal,
            data: data
        });
    }

    async sendInstalledSignalAsync() {
        await this.sendSignalAsync("installed");
    }
}

let server = new Server("Atom");

browser.runtime.onInstalled.addListener(async () => {
  await server.sendInstalledSignalAsync();
});