import { spawnSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const currentFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(currentFilePath);
const extensionRuntimeDirectory = path.resolve(scriptsDirectory, '..');
const testsDirectory = path.join(extensionRuntimeDirectory, 'Tests');
const testFiles = readdirSync(testsDirectory, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.test.mjs'))
    .map((entry) => path.join('Tests', entry.name))
    .sort();

if (testFiles.length === 0) {
    throw new Error(`[extension-runtime] Не найдены test-файлы в ${testsDirectory}`);
}

// Node 22 иногда ломает IPC-протокол node:test при запуске transport-resilience вместе
// с другими file-worker-ами (ERR_TEST_FAILURE: unable to deserialize cloned data).
// Отдельный invocation на файл сохраняет полный набор тестов и исключает этот flaky путь.
for (const testFile of testFiles) {
    console.info(`[extension-runtime] Запуск ${testFile}`);
    const result = spawnSync(
        process.execPath,
        ['--import', 'tsx', '--test', testFile],
        {
            cwd: extensionRuntimeDirectory,
            stdio: 'inherit',
        },
    );

    if (result.error !== undefined) {
        throw result.error;
    }

    if (result.status !== 0) {
        process.exitCode = result.status ?? 1;
        break;
    }
}
