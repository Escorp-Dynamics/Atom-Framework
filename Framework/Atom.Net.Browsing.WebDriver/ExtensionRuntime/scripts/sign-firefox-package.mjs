#!/usr/bin/env node

import { spawn } from 'node:child_process';
import { mkdir, readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import Client, { JwtApiAuth } from '../node_modules/web-ext/lib/util/submit-addon.js';

function parseArguments(argv) {
    const options = {
        sourceDir: '',
        signedDir: '',
        channel: 'unlisted',
        approvalTimeout: 900000,
        requestTimeout: 900000,
        extensionRuntimeDir: process.cwd(),
    };

    for (let index = 0; index < argv.length; index++) {
        const argument = argv[index];
        switch (argument) {
            case '--source-dir':
                options.sourceDir = argv[++index] ?? '';
                break;
            case '--signed-dir':
                options.signedDir = argv[++index] ?? '';
                break;
            case '--channel':
                options.channel = argv[++index] ?? '';
                break;
            case '--approval-timeout':
                options.approvalTimeout = Number.parseInt(argv[++index] ?? '', 10);
                break;
            case '--request-timeout':
                options.requestTimeout = Number.parseInt(argv[++index] ?? '', 10);
                break;
            case '--extension-runtime-dir':
                options.extensionRuntimeDir = argv[++index] ?? '';
                break;
            default:
                throw new Error(`Unknown argument: ${argument}`);
        }
    }

    if (!options.sourceDir) {
        throw new Error('Missing --source-dir');
    }

    if (!options.signedDir) {
        throw new Error('Missing --signed-dir');
    }

    if (!options.channel) {
        throw new Error('Missing --channel');
    }

    if (!Number.isFinite(options.approvalTimeout) || options.approvalTimeout < 0) {
        throw new Error('Invalid --approval-timeout');
    }

    if (!Number.isFinite(options.requestTimeout) || options.requestTimeout <= 0) {
        throw new Error('Invalid --request-timeout');
    }

    options.sourceDir = resolve(options.sourceDir);
    options.signedDir = resolve(options.signedDir);
    options.extensionRuntimeDir = resolve(options.extensionRuntimeDir);

    return options;
}

async function loadManifest(sourceDir) {
    const manifestPath = join(sourceDir, 'manifest.json');
    const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
    const addonId = manifest.browser_specific_settings?.gecko?.id;
    const version = manifest.version;

    if (!addonId) {
        throw new Error(`Firefox addon id not found in ${manifestPath}`);
    }

    if (!version) {
        throw new Error(`Extension version not found in ${manifestPath}`);
    }

    return {
        addonId,
        version,
    };
}

function getVersionDetailUrl(baseUrl, addonId, version) {
    return new URL(`addons/addon/${encodeURIComponent(addonId)}/versions/v${version}/`, baseUrl);
}

async function runWebExtSubmit(options) {
    return await new Promise((resolvePromise) => {
        let output = '';

        const child = spawn(
            'npm',
            [
                'exec',
                'web-ext',
                'sign',
                '--',
                '--source-dir', options.sourceDir,
                '--artifacts-dir', options.signedDir,
                '--channel', options.channel,
                '--approval-timeout', '0',
                '--timeout', String(options.requestTimeout),
            ],
            {
                cwd: options.extensionRuntimeDir,
                env: process.env,
                stdio: ['ignore', 'pipe', 'pipe'],
            });

        const onStdout = (chunk) => {
            output += chunk.toString();
        };

        const onStderr = (chunk) => {
            output += chunk.toString();
        };

        child.stdout.on('data', onStdout);
        child.stderr.on('data', onStderr);

        child.on('close', (code) => {
            resolvePromise({
                exitCode: code ?? 1,
                output,
            });
        });

        child.on('error', (error) => {
            resolvePromise({
                exitCode: 1,
                output: `${output}${error}`,
            });
        });
    });
}

async function tryGetVersionDetail(client, addonId, version) {
    const detailUrl = getVersionDetailUrl(new URL('https://addons.mozilla.org/api/v5/'), addonId, version);

    try {
        return await client.fetchJson(detailUrl, 'GET', undefined, 'Version lookup failed');
    }
    catch (error) {
        const message = String(error);
        if (message.includes('404')) {
            return null;
        }

        throw error;
    }
}

async function waitForSignedFile(client, addonId, version, approvalTimeout) {
    const detailUrl = getVersionDetailUrl(new URL('https://addons.mozilla.org/api/v5/'), addonId, version);
    const editUrl = `https://addons.mozilla.org/en-US/developers/addon/${encodeURIComponent(addonId)}/versions/v${version}`;

    const fileUrl = await client.waitRetry(
        (detailResponseData) => {
            const file = detailResponseData.file;
            if (file?.status === 'public' && file.url) {
                return file.url;
            }

            return null;
        },
        detailUrl,
        1000,
        approvalTimeout,
        'Approval',
        editUrl);

    return new URL(fileUrl);
}

async function main() {
    const options = parseArguments(process.argv.slice(2));
    const apiKey = process.env.WEB_EXT_API_KEY;
    const apiSecret = process.env.WEB_EXT_API_SECRET;

    if (!apiKey) {
        throw new Error('Missing WEB_EXT_API_KEY');
    }

    if (!apiSecret) {
        throw new Error('Missing WEB_EXT_API_SECRET');
    }

    const manifest = await loadManifest(options.sourceDir);
    await mkdir(options.signedDir, { recursive: true });

    const client = new Client({
        apiAuth: new JwtApiAuth({
            apiKey,
            apiSecret,
        }),
        baseUrl: new URL('https://addons.mozilla.org/api/v5/'),
        approvalCheckTimeout: options.approvalTimeout,
        downloadDir: options.signedDir,
        userAgentString: 'atom-webdriver-sign-helper',
    });

    const submission = await runWebExtSubmit(options);
    const versionDetail = await tryGetVersionDetail(client, manifest.addonId, manifest.version);
    const duplicateUpload = submission.output.includes('This upload has already been submitted.');

    if (versionDetail === null) {
        if (submission.output.trim()) {
            process.stderr.write(`${submission.output.trim()}\n`);
        }

        throw new Error(submission.output.trim() || `AMO version ${manifest.version} was not created for ${manifest.addonId}`);
    }

    if (submission.exitCode !== 0 && !duplicateUpload) {
        if (submission.output.trim()) {
            process.stderr.write(`${submission.output.trim()}\n`);
        }

        throw new Error(submission.output.trim());
    }

    if (duplicateUpload) {
        console.log(`AMO upload for ${manifest.addonId} ${manifest.version} was already submitted, resuming signed XPI download`);
    }
    else if (submission.output.trim()) {
        process.stdout.write(submission.output);
    }

    const fileUrl = await waitForSignedFile(client, manifest.addonId, manifest.version, options.approvalTimeout);
    const downloadResult = await client.downloadSignedFile(fileUrl, manifest.addonId);
    const downloadedFile = downloadResult.downloadedFiles?.[0];
    const downloadedPath = downloadedFile ? join(options.signedDir, downloadedFile) : '';

    if (!downloadedPath) {
        throw new Error(`Signed Firefox XPI not found in ${options.signedDir}`);
    }

    console.log(`Signed Firefox XPI: ${downloadedPath}`);
}

await main();