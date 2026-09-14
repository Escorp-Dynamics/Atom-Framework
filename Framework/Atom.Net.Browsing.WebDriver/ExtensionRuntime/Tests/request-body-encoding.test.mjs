import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { encodeRequestBodyBytes } from '../Background/BackgroundRuntimeHost.ts';

const decode = (base64) => Buffer.from(base64, 'base64').toString('utf8');

test('тело POST кодируется из ArrayBuffer текущего realm', () => {
    const bytes = new TextEncoder().encode('{"secondaryToken":"1.abc"}').buffer;

    assert.equal(decode(encodeRequestBodyBytes([{ bytes }])), '{"secondaryToken":"1.abc"}');
});

test('тело POST из ЧУЖОГО realm кодируется так же', () => {
    // Firefox отдаёт requestBody.raw[].bytes из привилегированного контекста браузера, то есть из
    // другого JS-realm. Прежняя проверка `instanceof ArrayBuffer` для такого объекта давала false,
    // тело молча отбрасывалось, и вторичный токен Turnstile не извлекался вовсе.
    const payload = '{"secondaryToken":"1.xyz"}';
    const foreign = vm.runInNewContext(
        'Uint8Array.from(codes).buffer',
        { codes: [...payload].map((char) => char.charCodeAt(0)) });

    assert.equal(foreign instanceof ArrayBuffer, false, 'предусловие: чужой realm не проходит instanceof');
    assert.equal(decode(encodeRequestBodyBytes([{ bytes: foreign }])), '{"secondaryToken":"1.xyz"}');
});

test('типизированное представление тоже принимается', () => {
    const view = new TextEncoder().encode('{"secondaryToken":"1.view"}');

    assert.equal(decode(encodeRequestBodyBytes([{ bytes: view }])), '{"secondaryToken":"1.view"}');
});

test('несколько кусков склеиваются по порядку', () => {
    const head = new TextEncoder().encode('{"secondaryToken":').buffer;
    const tail = new TextEncoder().encode('"1.split"}').buffer;

    assert.equal(decode(encodeRequestBodyBytes([{ bytes: head }, { bytes: tail }])), '{"secondaryToken":"1.split"}');
});

test('непригодные значения дают undefined', () => {
    assert.equal(encodeRequestBodyBytes(undefined), undefined);
    assert.equal(encodeRequestBodyBytes([]), undefined);
    assert.equal(encodeRequestBodyBytes([{ bytes: 'строка' }]), undefined);
    assert.equal(encodeRequestBodyBytes([{ file: '/tmp/x' }]), undefined);
});
