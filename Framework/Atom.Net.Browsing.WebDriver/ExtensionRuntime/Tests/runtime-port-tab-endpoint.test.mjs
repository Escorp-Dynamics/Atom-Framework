import test from 'node:test';
import assert from 'node:assert/strict';
import { RuntimePortTabEndpoint } from '../Background/Tabs/RuntimePortTabEndpoint.ts';

function createPort() {
    const messageListeners = [];
    const disconnectListeners = [];
    const postedMessages = [];

    return {
        postedMessages,
        emitMessage(message) {
            for (const listener of messageListeners) {
                listener(message);
            }
        },
        emitDisconnect() {
            for (const listener of disconnectListeners) {
                listener();
            }
        },
        port: {
            sender: {
                tab: {
                    id: 7,
                    windowId: 3,
                },
            },
            postMessage(message) {
                postedMessages.push(message);
            },
            onMessage: {
                addListener(listener) {
                    messageListeners.push(listener);
                },
                removeListener(listener) {
                    const index = messageListeners.indexOf(listener);
                    if (index >= 0) {
                        messageListeners.splice(index, 1);
                    }
                },
            },
            onDisconnect: {
                addListener(listener) {
                    disconnectListeners.push(listener);
                },
                removeListener(listener) {
                    const index = disconnectListeners.indexOf(listener);
                    if (index >= 0) {
                        disconnectListeners.splice(index, 1);
                    }
                },
            },
            disconnect() {
            },
        },
    };
}

test('RuntimePortTabEndpoint переводит bridge request в команду вкладки и чистит undefined поля контекста', async () => {
    const handle = createPort();

    const endpoint = new RuntimePortTabEndpoint(handle.port, {
        forwardToBridge: async () => {
        },
        executeInMainWorld: async (requestId) => ({
            action: 'mainWorldResult',
            requestId,
            status: 'ok',
            value: 'ignored',
        }),
        markReady: () => {
        },
        onDisconnected: () => {
        },
    });

    await endpoint.send({
        id: 'req_1',
        type: 'Request',
        command: 'ExecuteScript',
        payload: { code: '1 + 1' },
    });

    await endpoint.applyContext({
        sessionId: 'session-1',
        contextId: 'ctx-1',
        tabId: '7',
        connectedAt: 123,
        isReady: false,
        clientHints: {
            platform: 'Android',
            mobile: true,
            brands: [
                { brand: 'Chromium', version: '131' },
            ],
        },
    });

    assert.deepEqual(handle.postedMessages[0], {
        id: 'req_1',
        command: 'ExecuteScript',
        payload: { code: '1 + 1' },
    });

    assert.equal(handle.postedMessages[1].command, 'ApplyContext');
    assert.deepEqual(handle.postedMessages[1].payload, {
        sessionId: 'session-1',
        contextId: 'ctx-1',
        tabId: '7',
        connectedAt: 123,
        isReady: false,
        clientHints: {
            platform: 'Android',
            mobile: true,
            brands: [
                { brand: 'Chromium', version: '131' },
            ],
        },
    });
    assert.equal('windowId' in handle.postedMessages[1].payload, false);
    assert.equal('readyAt' in handle.postedMessages[1].payload, false);
});

test('RuntimePortTabEndpoint переводит ответ content слоя в bridge response', async () => {
    const handle = createPort();
    const forwarded = [];

    new RuntimePortTabEndpoint(handle.port, {
        forwardToBridge: async (message) => {
            forwarded.push(message);
        },
        executeInMainWorld: async (requestId) => ({
            action: 'mainWorldResult',
            requestId,
            status: 'ok',
            value: 'ignored',
        }),
        markReady: () => {
        },
        onDisconnected: () => {
        },
    });

    handle.emitMessage({
        action: 'response',
        id: 'resp_1',
        status: 'Ok',
        payload: { result: 2 },
    });

    assert.equal(forwarded.length, 1);
    assert.equal(forwarded[0].id, 'resp_1');
    assert.equal(forwarded[0].type, 'Response');
    assert.equal(forwarded[0].tabId, '7');
    assert.equal(forwarded[0].windowId, '3');
    assert.equal(forwarded[0].status, 'Ok');
    assert.deepEqual(forwarded[0].payload, { result: 2 });
});