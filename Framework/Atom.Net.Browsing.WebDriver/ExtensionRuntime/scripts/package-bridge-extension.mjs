import crx3 from "crx3";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const [sourceDir, keyPath, crxPath] = process.argv.slice(2);

if (!sourceDir || !keyPath || !crxPath) {
    console.error("usage: node package-bridge-extension.mjs <sourceDir> <keyPath> <crxPath>");
    process.exit(1);
}

const stagingDir = await fs.mkdtemp(path.join(os.tmpdir(), "atom-webdriver-crx-"));

try {
    await copyDirectory(sourceDir, stagingDir);
    await removeIfExists(path.join(stagingDir, ".extension_key.der"));
    await removeIfExists(path.join(stagingDir, ".extension_key.pem"));

    const info = await crx3([stagingDir], {
        keyPath,
        crxPath,
        zipPath: path.join(path.dirname(crxPath), "atom-webdriver-extension.zip")
    });

    const manifest = JSON.parse(await fs.readFile(path.join(sourceDir, "manifest.json"), "utf8"));
    process.stdout.write(JSON.stringify({
        extensionId: info.appId,
        version: manifest.version,
        crxPath
    }));
}
finally {
    await fs.rm(stagingDir, { recursive: true, force: true });
}

async function copyDirectory(sourceDir, destinationDir) {
    await fs.mkdir(destinationDir, { recursive: true });
    const entries = await fs.readdir(sourceDir, { withFileTypes: true });
    for (const entry of entries) {
        const sourcePath = path.join(sourceDir, entry.name);
        const destinationPath = path.join(destinationDir, entry.name);
        if (entry.isDirectory()) {
            await copyDirectory(sourcePath, destinationPath);
            continue;
        }

        if (!entry.isSymbolicLink())
            await fs.copyFile(sourcePath, destinationPath);
    }
}

async function removeIfExists(filePath) {
    await fs.rm(filePath, { force: true });
}
