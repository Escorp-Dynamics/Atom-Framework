import test from 'node:test';
import assert from 'node:assert/strict';
import { compileInterceptionPattern } from '../Background/BackgroundRuntimeHost.ts';

const secondaryTokenRoute = 'POST https://**/cdn-cgi/challenge-platform/h/*/c/*';
const secondaryTokenUrl = 'https://visa.vfsglobal.com/cdn-cgi/challenge-platform/h/g/c/a3871409692e569c';

test('шаблон с префиксом метода ловит нужный POST', () => {
    const pattern = compileInterceptionPattern(secondaryTokenRoute);

    assert.equal(pattern.method, 'POST');
    assert.equal(pattern.test(secondaryTokenUrl, 'POST'), true);
});

test('шаблон с префиксом метода отсекает другой метод на том же адресе', () => {
    const pattern = compileInterceptionPattern(secondaryTokenRoute);

    assert.equal(pattern.test(secondaryTokenUrl, 'GET'), false);
});

test('шаблон с префиксом метода отсекает соседний путь того же поколения', () => {
    // Телеметрия /h/g/fo/ идёт тем же POST по соседнему пути. Захват соседей = отказ решения:
    // каждый перехваченный запрос идёт раунд-трипом через мост, и Cloudflare видит это как бота.
    const pattern = compileInterceptionPattern(secondaryTokenRoute);

    assert.equal(pattern.test('https://challenges.cloudflare.com/cdn-cgi/challenge-platform/h/g/fo/123', 'POST'), false);
});

test('шаблон без префикса ведёт себя как прежде', () => {
    const pattern = compileInterceptionPattern('https://**/queue*');

    assert.equal(pattern.method, null);
    assert.equal(pattern.test('https://waitroom.smoke/queue', 'GET'), true);
    assert.equal(pattern.test('https://waitroom.smoke/queue', 'POST'), true);
    assert.equal(pattern.test('https://waitroom.smoke/queue', undefined), true);
});

test('схема адреса не принимается за префикс метода', () => {
    const pattern = compileInterceptionPattern('https://example.test/a b/*');

    assert.equal(pattern.method, null);
});

test('вопросительный знак в шаблоне остаётся литералом', () => {
    const pattern = compileInterceptionPattern('GET https://example.test/x?onload=*');

    assert.equal(pattern.method, 'GET');
    assert.equal(pattern.test('https://example.test/x?onload=cb', 'GET'), true);
    assert.equal(pattern.test('https://example.test/xonload=cb', 'GET'), false);
});
