import { copyFileSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const currentFilePath = fileURLToPath(import.meta.url);
const scriptsDirectory = path.dirname(currentFilePath);
const extensionRuntimeDirectory = path.resolve(scriptsDirectory, '..');
const projectDirectory = path.resolve(extensionRuntimeDirectory, '..');
const repositoryRoot = path.resolve(projectDirectory, '..', '..');
const workingLayoutDirectory = path.join(projectDirectory, 'obj', 'ExtensionWorkingLayout');

const chromeWorkingDirectory = resolveOption('--chrome-working-dir')
    ? path.resolve(resolveOption('--chrome-working-dir'))
    : path.join(workingLayoutDirectory, 'Extension');
const firefoxWorkingDirectory = resolveOption('--firefox-working-dir')
    ? path.resolve(resolveOption('--firefox-working-dir'))
    : path.join(workingLayoutDirectory, 'Extension.Firefox');

const chromeManifestPath = path.join(extensionRuntimeDirectory, 'Packaging', 'Chrome', 'manifest.json');
const firefoxManifestPath = path.join(extensionRuntimeDirectory, 'Packaging', 'Firefox', 'manifest.json');

const referenceExtensionDirectory = path.join(
    repositoryRoot,
    'Reference',
    'WebDriver.Reference',
    'Framework',
    'Atom.Net.Browsing.WebDriver',
    'Extension',
);

const referenceIconsDirectory = path.join(referenceExtensionDirectory, 'icons');
const generatedDirectory = resolveOption('--generated-dir')
    ? path.resolve(resolveOption('--generated-dir'))
    : path.join(projectDirectory, 'obj', 'ExtensionRuntime', 'generated');
const backgroundRuntimePath = path.join(generatedDirectory, 'background.runtime.js');
const contentRuntimePath = path.join(generatedDirectory, 'content.js');

function ensureDirectory(directoryPath) {
    mkdirSync(directoryPath, { recursive: true });
}

function syncManifest(sourcePath, workingDirectory) {
    copyFileSync(sourcePath, path.join(workingDirectory, 'manifest.json'));
}

function syncReferenceBaseline(workingDirectory) {
    const iconsWorkingDirectory = path.join(workingDirectory, 'icons');
    ensureDirectory(iconsWorkingDirectory);

    for (const iconName of readdirSync(referenceIconsDirectory)) {
        copyFileSync(
            path.join(referenceIconsDirectory, iconName),
            path.join(iconsWorkingDirectory, iconName),
        );
    }
}

function syncGeneratedRuntime(workingDirectory) {
    copyFileSync(backgroundRuntimePath, path.join(workingDirectory, 'background.runtime.js'));
    copyFileSync(contentRuntimePath, path.join(workingDirectory, 'content.js'));
}

function syncWorkingDirectory(manifestPath, workingDirectory) {
    ensureDirectory(workingDirectory);
    rmSync(path.join(workingDirectory, 'background.js'), { force: true });
    syncManifest(manifestPath, workingDirectory);
    syncReferenceBaseline(workingDirectory);
    syncGeneratedRuntime(workingDirectory);
    console.info(`[extension-runtime] Синхронизирован рабочий каталог ${workingDirectory}`);
}

syncWorkingDirectory(chromeManifestPath, chromeWorkingDirectory);
syncWorkingDirectory(firefoxManifestPath, firefoxWorkingDirectory);

function resolveOption(name) {
    const index = process.argv.indexOf(name);
    if (index < 0 || index === process.argv.length - 1) {
        return null;
    }

    return process.argv[index + 1];
}