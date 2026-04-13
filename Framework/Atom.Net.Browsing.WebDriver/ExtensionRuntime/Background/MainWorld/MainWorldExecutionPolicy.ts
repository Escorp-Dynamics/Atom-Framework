import { type BrowserHost, invokeBrowserCall } from '../Browser/BrowserApi';

type JsonRecord = Record<string, unknown>;

export async function evaluateMainWorldScript(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
    script: string,
    preferPageContextOnNull = false,
    forcePageContextExecution = false,
    debug?: (kind: string, details: unknown) => void,
): Promise<string> {
    debug?.('execute-script-main-start', {
        preferPageContextOnNull,
        forcePageContextExecution,
        scriptPreview: summarizeScript(script),
    });

    if (browserHost.scripting?.executeScript !== undefined) {
        if (forcePageContextExecution) {
            debug?.('execute-script-force-page-start', {
                scriptPreview: summarizeScript(script),
            });

            const forcedValue = await tryEvaluateViaScriptingPageInjection(browserHost, runtime, tabId, script);
            if (forcedValue !== null) {
                debug?.('execute-script-force-page-result', {
                    status: 'ok',
                    valuePreview: summarizeValue(forcedValue),
                });
                return forcedValue;
            }

            debug?.('execute-script-force-page-result', {
                status: 'fallback-missed',
            });
        }

        const results = await invokeBrowserCall<any[]>(
            runtime,
            browserHost.scripting.executeScript,
            browserHost.scripting,
            {
                target: { tabId },
                world: 'MAIN',
                func: async (code: string) => {
                    try {
                        const compile = (source: string) => {
                            try {
                                return new Function(`return (${source});`);
                            } catch {
                                return new Function(source);
                            }
                        };

                        let result = compile(code)();
                        if (result !== null && typeof result === 'object' && 'then' in result && typeof result.then === 'function') {
                            result = await result;
                        }

                        return {
                            ok: true,
                            value: result !== null && result !== undefined ? String(result) : 'null',
                        };
                    } catch (error) {
                        return {
                            ok: false,
                            error: error instanceof Error ? error.message : String(error),
                        };
                    }
                },
                args: [script],
            },
        );

        const payload = results[0]?.result;
        if (isRecord(payload) && payload.ok === true && typeof payload.value === 'string') {
            debug?.('execute-script-main-result', {
                stage: 'main',
                status: 'ok',
                valuePreview: summarizeValue(payload.value),
            });

            if (preferPageContextOnNull && payload.value === 'null') {
                debug?.('execute-script-null-fallback-start', {
                    scriptPreview: summarizeScript(script),
                });

                const fallbackValue = await tryEvaluateViaScriptingPageInjection(browserHost, runtime, tabId, script);
                if (fallbackValue !== null) {
                    debug?.('execute-script-null-fallback-result', {
                        status: 'ok',
                        valuePreview: summarizeValue(fallbackValue),
                    });
                    return fallbackValue;
                }

                debug?.('execute-script-null-fallback-result', {
                    status: 'fallback-missed',
                });
            }

            return payload.value;
        }

        if (isRecord(payload) && payload.ok === false && typeof payload.error === 'string') {
            debug?.('execute-script-main-result', {
                stage: 'main',
                status: 'error',
                error: payload.error,
            });
            return await evaluateViaLegacyTabInjection(browserHost, runtime, tabId, script, new Error(payload.error));
        }

        debug?.('execute-script-main-result', {
            stage: 'main',
            status: 'invalid-payload',
        });

        return await evaluateViaLegacyTabInjection(browserHost, runtime, tabId, script);
    }

    debug?.('execute-script-main-result', {
        stage: 'main',
        status: 'scripting-unavailable',
    });

    return await evaluateViaLegacyTabInjection(browserHost, runtime, tabId, script);
}

async function tryEvaluateViaScriptingPageInjection(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
    script: string,
): Promise<string | null> {
    if (browserHost.scripting?.executeScript === undefined) {
        return null;
    }

    try {
        const results = await invokeBrowserCall<any[]>(
            runtime,
            browserHost.scripting.executeScript,
            browserHost.scripting,
            {
                target: { tabId },
                func: async (source: string, timeoutMs: number) => {
                    return await new Promise<{ ok: boolean; value?: string; error?: string }>((resolve) => {
                        const responseId = `atom-main-world-response-${Math.random().toString(36).slice(2)}`;
                        const eventName = `atom-main-world-result-${responseId}`;
                        let settled = false;
                        const cleanup = () => {
                            globalThis.removeEventListener(eventName, handleResult);
                            globalThis.clearTimeout(timeoutId);
                            document.getElementById(responseId)?.remove();
                            injection.remove();
                        };
                        const finish = (payload: { ok: boolean; value?: string; error?: string }) => {
                            if (settled) {
                                return;
                            }

                            settled = true;
                            cleanup();
                            resolve(payload);
                        };
                        const handleResult = () => {
                            const payloadText = document.getElementById(responseId)?.textContent;
                            if (!payloadText) {
                                finish({ ok: false, error: 'Основной мир не вернул результат выполнения скрипта.' });
                                return;
                            }

                            try {
                                const payload = JSON.parse(payloadText) as { status?: string; value?: string; error?: string };
                                finish({
                                    ok: payload?.status === 'ok',
                                    value: typeof payload?.value === 'string' ? payload.value : 'null',
                                    error: typeof payload?.error === 'string' ? payload.error : undefined,
                                });
                            } catch (error) {
                                finish({ ok: false, error: error instanceof Error ? error.message : String(error) });
                            }
                        };
                        const timeoutId = globalThis.setTimeout(() => {
                            finish({ ok: false, error: 'Истекло ожидание результата выполнения скрипта в основном мире.' });
                        }, timeoutMs);
                        const injection = document.createElement('script');
                        injection.textContent = [
                            '(() => {',
                            `const responseId = ${JSON.stringify(responseId)};`,
                            `const eventName = ${JSON.stringify(eventName)};`,
                            `const source = ${JSON.stringify(source)};`,
                            'const publish = (payload) => {',
                            '    const root = document.documentElement ?? document.head ?? document.body;',
                            '    if (!root) {',
                            '        return;',
                            '    }',
                            '',
                            '    let node = document.getElementById(responseId);',
                            '    if (!node) {',
                            "        node = document.createElement('script');",
                            '        node.id = responseId;',
                            "        node.type = 'application/json';",
                            '        root.appendChild(node);',
                            '    }',
                            '',
                            '    node.textContent = JSON.stringify(payload);',
                            '    globalThis.dispatchEvent(new Event(eventName));',
                            '};',
                            '',
                            'Promise.resolve()',
                            '    .then(() => {',
                            '        const compile = (script) => {',
                            '            try {',
                            '                return new Function(`return (${script});`);',
                            '            } catch {',
                            '                return new Function(script);',
                            '            }',
                            '        };',
                            '        return compile(source)();',
                            '    })',
                            '    .then((result) => {',
                            '        publish({',
                            "            status: 'ok',",
                            "            value: result !== null && result !== undefined ? String(result) : 'null',",
                            '        });',
                            '    })',
                            '    .catch((error) => {',
                            '        publish({',
                            "            status: 'err',",
                            "            error: error instanceof Error ? error.message : String(error),",
                            '        });',
                            '    });',
                            '})();',
                        ].join('\n');

                        globalThis.addEventListener(eventName, handleResult, { once: true });

                        const root = document.documentElement ?? document.head ?? document.body;
                        if (!root) {
                            finish({ ok: false, error: 'Не удалось найти корневой узел документа для выполнения скрипта в основном мире.' });
                            return;
                        }

                        root.appendChild(injection);
                    });
                },
                args: [script, 500],
            },
        );

        const payload = results[0]?.result;
        if (isRecord(payload) && payload.ok === true && typeof payload.value === 'string') {
            return payload.value;
        }
    } catch {
    }

    return null;
}

async function evaluateViaLegacyTabInjection(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
    script: string,
    previousError?: Error,
): Promise<string> {
    if (browserHost.tabs?.executeScript === undefined) {
        throw previousError ?? new Error('Выполнение в основном мире пока поддерживается только через scripting.executeScript');
    }

    const results = await invokeBrowserCall<any[]>(
        runtime,
        browserHost.tabs.executeScript,
        browserHost.tabs,
        tabId,
        {
            code: buildLegacyMainWorldInjectionSource(script),
            runAt: 'document_idle',
        },
    );

    const payload = parseLegacyResultPayload(results[0]);
    if (isRecord(payload) && payload.ok === true && typeof payload.value === 'string') {
        return payload.value;
    }

    if (isRecord(payload) && payload.ok === false && typeof payload.error === 'string') {
        throw new Error(payload.error);
    }

    throw previousError ?? new Error('Основной мир не вернул результат выполнения скрипта.');
}

function buildLegacyMainWorldInjectionSource(script: string): string {
    const serializedScript = JSON.stringify(script);

    return [
        '(() => {',
        'return new Promise((resolve) => {',
        "    const responseId = 'atom-main-world-response-' + Math.random().toString(36).slice(2);",
        "    const eventName = 'atom-main-world-result-' + responseId;",
        '    let settled = false;',
        '    const cleanup = () => {',
        '        globalThis.removeEventListener(eventName, handleResult);',
        '        globalThis.clearTimeout(timeoutId);',
        '        document.getElementById(responseId)?.remove();',
        '        injection.remove();',
        '    };',
        '    const finish = (payload) => {',
        '        if (settled) {',
        '            return;',
        '        }',
        '',
        '        settled = true;',
        '        cleanup();',
        '        resolve(JSON.stringify(payload));',
        '    };',
        '    const handleResult = () => {',
        '        const payloadText = document.getElementById(responseId)?.textContent;',
        '        if (!payloadText) {',
        "            finish({ ok: false, error: 'Основной мир не вернул результат выполнения скрипта.' });",
        '            return;',
        '        }',
        '',
        '        try {',
        '            const payload = JSON.parse(payloadText);',
        '            finish({',
        "                ok: payload?.status === 'ok',",
        "                value: typeof payload?.value === 'string' ? payload.value : 'null',",
        "                error: typeof payload?.error === 'string' ? payload.error : undefined,",
        '            });',
        '        } catch (error) {',
        "            finish({ ok: false, error: error instanceof Error ? error.message : String(error) });",
        '        }',
        '    };',
        '    const timeoutId = globalThis.setTimeout(() => {',
        "        finish({ ok: false, error: 'Истекло ожидание результата выполнения скрипта в основном мире.' });",
        '    }, 5000);',
        `    const source = ${serializedScript};`,
        "    const injection = document.createElement('script');",
        '    injection.textContent = `(() => {',
        'const responseId = ${JSON.stringify(responseId)};',
        'const eventName = ${JSON.stringify(eventName)};',
        'const source = ${JSON.stringify(source)};',
        'const publish = (payload) => {',
        '    const root = document.documentElement ?? document.head ?? document.body;',
        '    if (!root) {',
        '        return;',
        '    }',
        '',
        '    let node = document.getElementById(responseId);',
        '    if (!node) {',
        "        node = document.createElement('script');",
        '        node.id = responseId;',
        "        node.type = 'application/json';",
        '        root.appendChild(node);',
        '    }',
        '',
        '    node.textContent = JSON.stringify(payload);',
        '    globalThis.dispatchEvent(new Event(eventName));',
        '};',
        '',
        'Promise.resolve()',
        '    .then(() => (0, eval)(source))',
        '    .then((result) => {',
        '        publish({',
        "            status: 'ok',",
        "            value: result !== null && result !== undefined ? String(result) : 'null',",
        '        });',
        '    })',
        '    .catch((error) => {',
        '        publish({',
        "            status: 'err',",
        "            error: error instanceof Error ? error.message : String(error),",
        '        });',
        '    });',
        '})();`;',
        '',
        '    globalThis.addEventListener(eventName, handleResult, { once: true });',
        '',
        '    const root = document.documentElement ?? document.head ?? document.body;',
        '    if (!root) {',
        "        finish({ ok: false, error: 'Не удалось найти корневой узел документа для выполнения скрипта в основном мире.' });",
        '        return;',
        '    }',
        '',
        '    root.appendChild(injection);',
        '});',
        '})()',
    ].join('\n');
}

function parseLegacyResultPayload(value: unknown): unknown {
    if (isRecord(value) && 'result' in value) {
        return parseLegacyResultPayload(value.result);
    }

    if (typeof value !== 'string') {
        return value;
    }

    try {
        return JSON.parse(value);
    } catch {
        return value;
    }
}

function isRecord(value: unknown): value is JsonRecord {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function summarizeScript(script: string): string {
    return script.replace(/\s+/g, ' ').trim().slice(0, 160);
}

function summarizeValue(value: string): string {
    return value.length <= 160 ? value : value.slice(0, 160);
}