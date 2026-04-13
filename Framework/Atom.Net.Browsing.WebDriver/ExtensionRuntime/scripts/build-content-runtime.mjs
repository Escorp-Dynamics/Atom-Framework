import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';

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
    entryPoints: [path.join(extensionRuntimeDirectory, 'content.runtime.ts')],
    outfile: path.join(generatedDirectory, 'content.js'),
    bundle: true,
    format: 'iife',
    platform: 'browser',
    target: ['es2022'],
    legalComments: 'none',
    charset: 'utf8',
});

console.info('[extension-runtime] Собран content runtime generated/content.js');

function resolveOption(name) {
    const index = process.argv.indexOf(name);
    if (index < 0 || index === process.argv.length - 1) {
        return null;
    }

    return process.argv[index + 1];
}