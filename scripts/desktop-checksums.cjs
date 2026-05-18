const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const ARTIFACT_PATTERN = /\.(exe|dmg|zip|appimage|deb|rpm|snap|yml|yaml|blockmap)$/i;

function toPortablePath(filePath) {
  return filePath.split(path.sep).join('/');
}

function hashFile(filePath) {
  const hash = crypto.createHash('sha256');
  hash.update(fs.readFileSync(filePath));
  return hash.digest('hex');
}

function listArtifacts(directory) {
  if (!fs.existsSync(directory)) {
    throw new Error(`Desktop artifact directory does not exist: ${directory}`);
  }

  return fs
    .readdirSync(directory, { withFileTypes: true })
    .filter((entry) => entry.isFile() && ARTIFACT_PATTERN.test(entry.name))
    .map((entry) => entry.name)
    .sort((left, right) => left.localeCompare(right));
}

function generateChecksums(directory, checksumPath) {
  const artifacts = listArtifacts(directory)
    .filter((name) => path.resolve(directory, name) !== path.resolve(checksumPath));

  if (artifacts.length === 0) {
    throw new Error(`No desktop release artifacts found in ${directory}.`);
  }

  const lines = artifacts.map((name) => `${hashFile(path.join(directory, name))}  ${toPortablePath(name)}`);
  fs.writeFileSync(checksumPath, `${lines.join('\n')}\n`, 'utf8');
  console.log(`[desktop-checksums] wrote ${checksumPath} (${artifacts.length} artifacts)`);
}

function parseChecksumLine(line, lineNumber) {
  const match = line.match(/^([a-fA-F0-9]{64})\s{2,}(.+)$/);
  if (!match) {
    throw new Error(`Invalid checksum line ${lineNumber}: ${line}`);
  }

  return {
    expectedHash: match[1].toLowerCase(),
    relativePath: match[2].trim(),
  };
}

function verifyChecksums(directory, checksumPath) {
  if (!fs.existsSync(checksumPath)) {
    throw new Error(`Checksum file does not exist: ${checksumPath}`);
  }

  const entries = fs
    .readFileSync(checksumPath, 'utf8')
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter(Boolean)
    .map(parseChecksumLine);

  if (entries.length === 0) {
    throw new Error(`Checksum file is empty: ${checksumPath}`);
  }

  for (const entry of entries) {
    const artifactPath = path.resolve(directory, entry.relativePath);
    if (!artifactPath.startsWith(path.resolve(directory))) {
      throw new Error(`Checksum entry escapes artifact directory: ${entry.relativePath}`);
    }

    if (!fs.existsSync(artifactPath)) {
      throw new Error(`Checksum entry is missing from artifact directory: ${entry.relativePath}`);
    }

    const actualHash = hashFile(artifactPath);
    if (actualHash !== entry.expectedHash) {
      throw new Error(`Checksum mismatch for ${entry.relativePath}. Expected ${entry.expectedHash}, got ${actualHash}.`);
    }
  }

  console.log(`[desktop-checksums] verified ${entries.length} artifacts from ${checksumPath}`);
}

function main() {
  const command = process.argv[2] || 'generate';
  const directory = path.resolve(process.argv[3] || path.join(__dirname, '..', 'dist', 'desktop'));
  const checksumPath = path.resolve(process.argv[4] || path.join(directory, 'desktop-checksums.sha256'));

  if (command === 'generate') {
    generateChecksums(directory, checksumPath);
    return;
  }

  if (command === 'verify') {
    verifyChecksums(directory, checksumPath);
    return;
  }

  throw new Error(`Unknown command "${command}". Use "generate" or "verify".`);
}

main();
