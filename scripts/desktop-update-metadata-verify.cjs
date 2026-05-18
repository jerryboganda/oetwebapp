const fs = require('fs');
const path = require('path');

function readMetadataFiles(directory, allowMissing) {
  if (!fs.existsSync(directory)) {
    if (allowMissing) {
      return [];
    }

    throw new Error(`Desktop artifact directory does not exist: ${directory}`);
  }

  return fs
    .readdirSync(directory, { withFileTypes: true })
    .filter((entry) => entry.isFile() && /^latest.*\.ya?ml$/i.test(entry.name))
    .map((entry) => path.join(directory, entry.name))
    .sort((left, right) => left.localeCompare(right));
}

function parseScalarValues(content, key) {
  const values = [];
  const pattern = new RegExp(`^\\s*${key}:\\s*["']?([^"'\\r\\n#]+)["']?\\s*$`, 'gim');
  let match = pattern.exec(content);
  while (match) {
    values.push(match[1].trim());
    match = pattern.exec(content);
  }
  return values;
}

function assertSafeArtifactReference(value, metadataPath, artifactDirectory) {
  if (/^https?:\/\//i.test(value)) {
    const parsed = new URL(value);
    if (parsed.protocol !== 'https:') {
      throw new Error(`${path.basename(metadataPath)} contains a non-HTTPS update URL: ${value}`);
    }

    if (['localhost', '127.0.0.1', '::1'].includes(parsed.hostname.toLowerCase())) {
      throw new Error(`${path.basename(metadataPath)} contains a loopback update URL: ${value}`);
    }

    return;
  }

  const relativePath = value.replace(/\\/g, '/');
  if (relativePath.startsWith('/') || relativePath.includes('../')) {
    throw new Error(`${path.basename(metadataPath)} contains an unsafe artifact path: ${value}`);
  }

  const artifactPath = path.resolve(artifactDirectory, relativePath);
  if (!artifactPath.startsWith(path.resolve(artifactDirectory))) {
    throw new Error(`${path.basename(metadataPath)} contains an artifact path outside the release directory: ${value}`);
  }

  if (!fs.existsSync(artifactPath)) {
    throw new Error(`${path.basename(metadataPath)} references a missing artifact: ${value}`);
  }
}

function verifyMetadataFile(metadataPath, artifactDirectory) {
  const content = fs.readFileSync(metadataPath, 'utf8');
  const version = parseScalarValues(content, 'version')[0];
  const sha512Values = parseScalarValues(content, 'sha512');
  const pathValues = parseScalarValues(content, 'path');
  const urlValues = parseScalarValues(content, 'url');
  const references = [...pathValues, ...urlValues];

  if (!version) {
    throw new Error(`${path.basename(metadataPath)} is missing version.`);
  }

  if (sha512Values.length === 0) {
    throw new Error(`${path.basename(metadataPath)} is missing sha512.`);
  }

  for (const value of sha512Values) {
    if (!/^[A-Za-z0-9+/=]{80,}$/.test(value)) {
      throw new Error(`${path.basename(metadataPath)} has an invalid sha512 value.`);
    }
  }

  if (references.length === 0) {
    throw new Error(`${path.basename(metadataPath)} does not reference any artifact path or URL.`);
  }

  for (const reference of references) {
    assertSafeArtifactReference(reference, metadataPath, artifactDirectory);
  }
}

function main() {
  const artifactDirectory = path.resolve(process.argv[2] || path.join(__dirname, '..', 'dist', 'desktop'));
  const allowMissing = process.argv.includes('--allow-missing') || process.env.ELECTRON_PUBLISH_MODE === 'never';
  const metadataFiles = readMetadataFiles(artifactDirectory, allowMissing);

  if (metadataFiles.length === 0) {
    if (allowMissing) {
      console.log('[desktop-update-metadata] no update metadata found; allowed for non-publishing builds');
      return;
    }

    throw new Error(`No Electron update metadata found in ${artifactDirectory}. Expected latest*.yml for a public desktop release.`);
  }

  for (const metadataPath of metadataFiles) {
    verifyMetadataFile(metadataPath, artifactDirectory);
  }

  console.log(`[desktop-update-metadata] verified ${metadataFiles.length} metadata file(s)`);
}

main();
