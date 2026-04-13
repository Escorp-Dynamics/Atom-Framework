import test from 'node:test';
import assert from 'node:assert/strict';
import { DefaultRouteFailurePolicy } from '../Background/Routing/DefaultRouteFailurePolicy.ts';
import { TabPortCommandRouter } from '../Background/Routing/TabPortCommandRouter.ts';

test('TabPortCommandRouter направляет команду в endpoint вкладки', async () => {
    const sent = [];
    const endpoint = {
        connected: true,
        async send(message) {
            sent.push(message);
        },
    };

    const router = new TabPortCommandRouter({
        tabs: {
            get(tabId) {
                return tabId === '7' ? { endpoint } : null;
            },
        },
        transport: {
            async send() {
                throw new Error('Не должно вызываться при успешной маршрутизации');
            },
        },
        failures: new DefaultRouteFailurePolicy(),
    });

    const request = {
        id: 'req_1',
        type: 'Request',
        tabId: '7',
        command: 'ExecuteScript',
    };

    await router.route(request);

    assert.equal(sent.length, 1);
    assert.deepEqual(sent[0], request);
});

test('TabPortCommandRouter возвращает ошибку через transport когда вкладка не найдена', async () => {
    const forwarded = [];

    const router = new TabPortCommandRouter({
        tabs: {
            get() {
                return null;
            },
        },
        transport: {
            async send(message) {
                forwarded.push(message);
            },
        },
        failures: new DefaultRouteFailurePolicy(),
    });

    await router.route({
        id: 'req_2',
        type: 'Request',
        tabId: '99',
        command: 'ExecuteScript',
    });

    assert.equal(forwarded.length, 1);
    assert.equal(forwarded[0].type, 'Response');
    assert.equal(forwarded[0].status, 'Error');
    assert.equal(forwarded[0].id, 'req_2');
    assert.equal(forwarded[0].tabId, '99');
    assert.match(forwarded[0].error, /вкладки/u);
});