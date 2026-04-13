import { type BrowserHost, invokeBrowserCall } from '../Browser/BrowserApi';

type ExecutionWorld = 'MAIN' | 'ISOLATED';

type ScriptExecutionResult = {
    readonly s: string;
    readonly v: string;
    readonly id?: string;
};

type TabFrameInfo = {
    readonly frameId?: number;
    readonly parentFrameId?: number;
    readonly url?: string;
    readonly errorOccurred?: boolean;
};

export async function executeScriptInFrames(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
    code: string,
    world: ExecutionWorld,
    includeMetadata: boolean,
): Promise<unknown[]> {
    return includeMetadata
        ? executeScriptInFramesWithMetadata(browserHost, runtime, tabId, code, world)
        : mapFrameExecutionValues(await evalInWorld(browserHost, runtime, tabId, code, true, world).catch((error) => {
            throw new Error(describeFrameExecutionError(error));
        }));
}

async function executeScriptInFramesWithMetadata(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
    code: string,
    world: ExecutionWorld,
): Promise<unknown[]> {
    const frameInfos = await getAllFramesForTab(browserHost, runtime, tabId).catch(() => null);

    if (!Array.isArray(frameInfos) || frameInfos.length === 0) {
        const results = await evalInWorld(browserHost, runtime, tabId, code, true, world);
        return (results ?? []).map((result, index) => ({
            ordinal: index,
            status: result?.s ?? 'err',
            value: result?.s === 'ok' ? result.v : null,
            error: result?.s === 'ok' ? null : (result?.v ?? 'Script execution failed.'),
        }));
    }

    return Promise.all(frameInfos.map(async (frameInfo, index) => {
        try {
            const result = await evalInWorld(browserHost, runtime, tabId, code, false, world, frameInfo.frameId ?? null);
            const first = Array.isArray(result) ? result[0] : result;

            return {
                ordinal: index,
                frameId: typeof frameInfo.frameId === 'number' ? frameInfo.frameId : undefined,
                parentFrameId: typeof frameInfo.parentFrameId === 'number' ? frameInfo.parentFrameId : undefined,
                url: typeof frameInfo.url === 'string' ? frameInfo.url : '',
                errorOccurred: frameInfo.errorOccurred === true,
                status: first?.s ?? 'err',
                value: first?.s === 'ok' ? first.v : null,
                error: first?.s === 'ok' ? null : (first?.v ?? 'Script execution failed.'),
            };
        } catch (error) {
            return {
                ordinal: index,
                frameId: typeof frameInfo.frameId === 'number' ? frameInfo.frameId : undefined,
                parentFrameId: typeof frameInfo.parentFrameId === 'number' ? frameInfo.parentFrameId : undefined,
                url: typeof frameInfo.url === 'string' ? frameInfo.url : '',
                errorOccurred: frameInfo.errorOccurred === true,
                status: 'err',
                value: null,
                error: describeFrameExecutionError(error),
            };
        }
    }));
}

async function getAllFramesForTab(browserHost: BrowserHost, runtime: any, tabId: number): Promise<TabFrameInfo[] | null> {
    if (browserHost.webNavigation?.getAllFrames === undefined) {
        return null;
    }

    const frames = await invokeBrowserCall<TabFrameInfo[]>(
        runtime,
        browserHost.webNavigation.getAllFrames,
        browserHost.webNavigation,
        { tabId },
    );

    return Array.isArray(frames) ? frames : null;
}

async function evalInWorld(
    browserHost: BrowserHost,
    runtime: any,
    tabId: number,
    code: string,
    allFrames: boolean,
    world: ExecutionWorld = 'MAIN',
    frameId: number | null = null,
): Promise<ScriptExecutionResult[]> {
    const preferLegacyTabsExecution = world === 'MAIN' && browserHost.tabs?.executeScript !== undefined;

    if (!preferLegacyTabsExecution && browserHost.scripting?.executeScript !== undefined) {
        const target = typeof frameId === 'number'
            ? { tabId, frameIds: [frameId] }
            : (allFrames ? { tabId, allFrames: true } : { tabId });

        const results = await invokeBrowserCall<any[]>(
            runtime,
            browserHost.scripting.executeScript,
            browserHost.scripting,
            {
                target,
                world,
                func: async (scriptSource: string) => {
                    try {
                        let result = (0, eval)(scriptSource);
                        if (result !== null && typeof result === 'object' && 'then' in result && typeof result.then === 'function') {
                            result = await result;
                        }

                        return {
                            s: 'ok',
                            v: result !== null && result !== undefined ? String(result) : 'null',
                        };
                    } catch (error) {
                        return {
                            s: 'err',
                            v: error instanceof Error ? error.message : String(error),
                        };
                    }
                },
                args: [code],
            },
        );

        return (results ?? []).map((result) => normalizeExecutionResult(result?.result));
    }

    if (browserHost.tabs?.executeScript === undefined) {
        throw new Error('API выполнения скриптов по вкладкам недоступен');
    }

    const pageContextWrapper = [
        '(function(elementId){',
        'var e=document.getElementById(elementId);',
        'if(!e)return;',
        'try{',
        'var r=(0,eval)(e.getAttribute("data-c"));',
        'if(r!=null&&typeof r==="object"&&typeof r.then==="function"){',
        'e.setAttribute("data-a","1");',
        'r.then(function(v){e.setAttribute("data-r",JSON.stringify({s:"ok",v:v!=null?String(v):"null"}));})',
        '.catch(function(x){e.setAttribute("data-r",JSON.stringify({s:"err",v:x&&x.message?x.message:String(x)}));});',
        '}else{',
        'e.setAttribute("data-r",JSON.stringify({s:"ok",v:r!=null?String(r):"null"}));',
        '}',
        '}catch(x){',
        'e.setAttribute("data-r",JSON.stringify({s:"err",v:x&&x.message?x.message:String(x)}));',
        '}',
        '})(__ATOM_ELEMENT_ID__);',
    ].join('');

    const wrapper = [
        '(function(code){',
        'var id="__ab"+Math.random().toString(36).slice(2,8);',
        'var el=document.createElement("span");',
        'el.id=id;el.style.display="none";',
        'el.setAttribute("data-c",code);',
        'document.documentElement.appendChild(el);',
        'var s=document.createElement("script");',
        's.textContent=' + JSON.stringify(pageContextWrapper) + '.replace("__ATOM_ELEMENT_ID__", JSON.stringify(id));',
        'document.documentElement.appendChild(s);s.remove();',
        'var j=el.getAttribute("data-r");',
        'if(j){el.remove();try{return JSON.parse(j)}catch(x){return{s:"err",v:"Bridge parse error"}}}',
        'if(el.getAttribute("data-a")==="1"){return{s:"async",id:id}}',
        'el.remove();return{s:"err",v:"MAIN world injection blocked (CSP?)"}',
        '})(' + JSON.stringify(code) + ')',
    ].join('');

    const executeOptions: Record<string, unknown> = { code: wrapper };
    if (typeof frameId === 'number') {
        executeOptions.frameId = frameId;
    } else {
        executeOptions.allFrames = allFrames;
    }

    executeOptions.matchAboutBlank = true;

    const results = await invokeBrowserCall<any[]>(runtime, browserHost.tabs.executeScript, browserHost.tabs, tabId, executeOptions);
    const first = (results ?? [])[0];
    if (first?.s !== 'async') {
        return (results ?? []).map(normalizeExecutionResult);
    }

    const elementId = first.id;
    if (typeof elementId !== 'string' || elementId.length == 0) {
        return [{ s: 'err', v: 'Async execution bridge did not return a polling identifier.' }];
    }

    return await new Promise<ScriptExecutionResult[]>((resolve) => {
        let completed = false;

        const finish = (value: ScriptExecutionResult[]) => {
            if (completed) {
                return;
            }

            completed = true;
            resolve(value);
        };

        const poll = () => {
            if (completed) {
                return;
            }

            invokeBrowserCall<any[]>(
                runtime,
                browserHost.tabs.executeScript,
                browserHost.tabs,
                tabId,
                {
                    code: '(function(){var e=document.getElementById("' + elementId + '");if(!e)return null;var j=e.getAttribute("data-r");if(!j)return null;e.remove();try{return JSON.parse(j)}catch(x){return{s:"err",v:"Bridge parse error"}}})()',
                    matchAboutBlank: true,
                    ...(typeof frameId === 'number' ? { frameId } : {}),
                },
            ).then((value) => {
                const result = normalizeExecutionResult((value ?? [])[0]);
                if (result.s === 'err' && result.v === 'Script execution failed.') {
                    setTimeout(poll, 50);
                    return;
                }

                if (result.v === 'null' && result.s === 'ok') {
                    setTimeout(poll, 50);
                    return;
                }

                finish([result]);
            }).catch(() => {
                finish([{ s: 'err', v: 'Poll failed' }]);
            });
        };

        setTimeout(poll, 10);
        setTimeout(() => {
            if (completed) {
                return;
            }

            void invokeBrowserCall<any[]>(
                runtime,
                browserHost.tabs.executeScript,
                browserHost.tabs,
                tabId,
                {
                    code: '(function(){var e=document.getElementById("' + elementId + '");if(e)e.remove()})()',
                    matchAboutBlank: true,
                    ...(typeof frameId === 'number' ? { frameId } : {}),
                },
            ).catch(() => undefined);

            finish([{ s: 'err', v: 'Async eval timeout (30s)' }]);
        }, 30000);
    });
}

function mapFrameExecutionValues(results: ScriptExecutionResult[]): unknown[] {
    return (results ?? []).map((result) => result?.s === 'ok'
        ? result.v
        : { __error: result?.v ?? 'Script execution failed.' });
}

function normalizeExecutionResult(value: unknown): ScriptExecutionResult {
    if (typeof value === 'object' && value !== null) {
        const record = value as Record<string, unknown>;
        if (typeof record.s === 'string' && typeof record.v === 'string') {
            return {
                s: record.s,
                v: record.v,
                id: typeof record.id === 'string' ? record.id : undefined,
            };
        }
    }

    return {
        s: 'err',
        v: 'Script execution failed.',
    };
}

function describeFrameExecutionError(error: unknown): string {
    if (error instanceof Error && error.message.trim().length > 0) {
        return error.message;
    }

    if (typeof error === 'string' && error.trim().length > 0) {
        return error;
    }

    return 'Script execution failed.';
}