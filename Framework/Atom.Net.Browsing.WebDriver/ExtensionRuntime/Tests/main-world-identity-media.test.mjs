import test from 'node:test';
import assert from 'node:assert/strict';
import { installIdentityInMainWorld } from '../Background/Identity/MainWorldIdentity.ts';

// Подмена MediaQueryList.prototype.matches читала незнакомые ей признаки через '.matches' на
// прототипе — то есть через саму себя — и уходила в рекурсию до переполнения стека. На живом
// стенде это держало рендерер на 100% CPU больше часа (14.10.2026).
test('подмена matches читает незнакомый признак родным геттером, без рекурсии', () => {
    class FakeMediaQueryList {
        constructor(media) {
            this.media = media;
        }
    }

    // Окружение «без мыши»: '(pointer: fine)' ложно, хотя профиль настольный, — подмена обязана встать.
    const nativeMatches = function () {
        return this.media !== '(pointer: fine)' && this.media !== '(hover: hover)';
    };
    Object.defineProperty(FakeMediaQueryList.prototype, 'matches', {
        configurable: true,
        enumerable: true,
        get: nativeMatches,
    });

    let matchMediaCalls = 0;
    const saved = {
        MediaQueryList: Object.getOwnPropertyDescriptor(globalThis, 'MediaQueryList'),
        matchMedia: Object.getOwnPropertyDescriptor(globalThis, 'matchMedia'),
    };

    globalThis.MediaQueryList = FakeMediaQueryList;
    globalThis.matchMedia = (media) => {
        matchMediaCalls++;
        return new FakeMediaQueryList(media);
    };

    try {
        installIdentityInMainWorld({ userAgent: 'Mozilla/5.0 (X11; Linux x86_64) media-recursion-test', isMobile: false, hasTouch: false });

        const installed = Object.getOwnPropertyDescriptor(FakeMediaQueryList.prototype, 'matches')?.get;
        assert.notEqual(installed, nativeMatches, 'подмена matches должна была встать: окружение расходится с профилем');

        assert.equal(globalThis.matchMedia('(pointer: fine)').matches, true, 'известный признак отвечает по профилю');

        matchMediaCalls = 0;
        assert.equal(globalThis.matchMedia('(min-width: 1px)').matches, true, 'незнакомый признак отвечает по окружению');
        assert.ok(matchMediaCalls <= 2, `незнакомый признак не должен уводить в рекурсию: matchMedia вызван ${matchMediaCalls} раз`);

        matchMediaCalls = 0;
        assert.equal(globalThis.matchMedia('(pointer: fine) and (min-width: 1px)').matches, true, 'составной запрос сочетает оба пути');
        assert.ok(matchMediaCalls <= 2, `составной запрос не должен уводить в рекурсию: matchMedia вызван ${matchMediaCalls} раз`);
    } finally {
        for (const [name, descriptor] of Object.entries(saved)) {
            if (descriptor) {
                Object.defineProperty(globalThis, name, descriptor);
            } else {
                delete globalThis[name];
            }
        }
    }
});
