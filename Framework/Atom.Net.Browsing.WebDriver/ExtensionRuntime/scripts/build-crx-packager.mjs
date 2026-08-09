import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';

// Собирает самодостаточный упаковщик CRX: package-bridge-extension.mjs импортирует npm-пакет
// crx3, а в output/NuGet рядом со скриптом нет node_modules. Без бандла запуск падает с
// ERR_MODULE_NOT_FOUND, упаковка Chromium CRX молча срывается и managed-delivery остаётся без
// пакета. Бандл инлайнит crx3 и его зависимости, поэтому node запускает скрипт без node_modules.
const currentFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(currentFilePath);
const extensionRuntimeDirectory = path.resolve(scriptsDirectory, '..');
const projectDirectory = path.resolve(extensionRuntimeDirectory, '..');
const defaultGeneratedDirectory = path.join(projectDirectory, 'obj', 'ExtensionRuntime', 'generated');
const generatedDirectory = resolveOption('--generated-dir')
    ? path.resolve(resolveOption('--generated-dir'))
    : defaultGeneratedDirectory;

mkdirSync(generatedDirectory, { recursive: true });

await build({
    entryPoints: [path.join(scriptsDirectory, 'package-bridge-extension.mjs')],
    outfile: path.join(generatedDirectory, 'package-bridge-extension.mjs'),
    bundle: true,
    platform: 'node',
    format: 'esm',
    target: ['node22'],
    legalComments: 'none',
    charset: 'utf8',
    // Зависимости crx3 частично CommonJS: даём им работающий require в ESM-бандле.
    banner: {
        js: "import { createRequire as __atomCreateRequire } from 'node:module'; const require = __atomCreateRequire(import.meta.url);",
    },
});

console.info('[extension-runtime] Собран самодостаточный упаковщик generated/package-bridge-extension.mjs');

function resolveOption(name) {
    const index = process.argv.indexOf(name);
    if (index < 0 || index === process.argv.length - 1) {
        return null;
    }

    return process.argv[index + 1];
}
