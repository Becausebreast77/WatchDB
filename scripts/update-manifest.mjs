import { readFile, writeFile } from 'node:fs/promises';

const [version, checksum, timestamp] = process.argv.slice(2);
const repository = process.env.GITHUB_REPOSITORY;
const targetAbi = '10.11.11.0';
const manifestPath = new URL('../manifest.json', import.meta.url);

if (!/^\d+\.\d+\.\d+\.\d+$/.test(version ?? '')) {
    throw new Error('The release version must use four numeric segments, for example 0.1.0.0.');
}

if (!/^[A-Fa-f0-9]{32}$/.test(checksum ?? '')) {
    throw new Error('The release checksum must be a 32-character MD5 value.');
}

if (!repository || !/^[^/]+\/[^/]+$/.test(repository)) {
    throw new Error('GITHUB_REPOSITORY must contain the owner and repository name.');
}

if (!timestamp || Number.isNaN(Date.parse(timestamp))) {
    throw new Error('The release timestamp must be an ISO-8601 date.');
}

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
if (!Array.isArray(manifest) || manifest.length !== 1 || manifest[0].guid !== '6c096401-05c2-4eb2-895a-f45f24a79f44') {
    throw new Error('The manifest does not contain the expected WatchDB entry.');
}

const release = {
    version,
    changelog: 'Initial public release of WatchDB Smart Organizer.',
    targetAbi,
    sourceUrl: `https://github.com/${repository}/releases/download/v${version}/WatchDB-${version}.zip`,
    checksum: checksum.toUpperCase(),
    timestamp,
};

manifest[0].versions = [release, ...(manifest[0].versions ?? []).filter(item => item.version !== version)];
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
