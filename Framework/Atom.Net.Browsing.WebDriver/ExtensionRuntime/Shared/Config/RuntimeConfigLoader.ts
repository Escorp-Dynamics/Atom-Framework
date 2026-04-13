import type { RuntimeConfig } from './RuntimeConfig';
import { validateRuntimeConfig } from './RuntimeConfigValidator';

export interface RuntimeConfigSource {
    loadText(): Promise<string>;
}

export async function loadRuntimeConfig(source: RuntimeConfigSource): Promise<RuntimeConfig> {
    const raw = await source.loadText();
    let parsed: unknown;

    try {
        parsed = JSON.parse(raw);
    } catch {
        throw new Error('Конфигурация runtime содержит неверный JSON');
    }

    return validateRuntimeConfig(parsed);
}