const fs = require('fs');
const path = require('path');
const { app, safeStorage } = require('electron');

const VAULT_VERSION = 1;
const VAULT_DIR_NAME = 'secure-storage';
const VAULT_FILE_NAME = 'desktop-secrets.json';

function normalizeSegment(value, label) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`${label} must be a non-empty string.`);
  }

  return value.trim();
}

function getVaultDirectory() {
  const directoryPath = path.join(app.getPath('userData'), VAULT_DIR_NAME);
  fs.mkdirSync(directoryPath, { recursive: true, mode: 0o700 });
  return directoryPath;
}

function getVaultPath() {
  return path.join(getVaultDirectory(), VAULT_FILE_NAME);
}

function buildEntryId(namespace, key) {
  return `${normalizeSegment(namespace, 'Secret namespace')}::${normalizeSegment(key, 'Secret key')}`;
}

function readVault() {
  const vaultPath = getVaultPath();

  if (!fs.existsSync(vaultPath)) {
    return {
      version: VAULT_VERSION,
      secrets: {},
    };
  }

  const raw = fs.readFileSync(vaultPath, 'utf8');
  const parsed = JSON.parse(raw);

  if (!parsed || typeof parsed !== 'object' || typeof parsed.secrets !== 'object' || parsed.secrets === null) {
    throw new Error('The desktop secret vault is malformed.');
  }

  return {
    version: parsed.version || VAULT_VERSION,
    secrets: parsed.secrets,
  };
}

function writeVault(vault) {
  const vaultPath = getVaultPath();
  const tempPath = `${vaultPath}.tmp`;
  const payload = JSON.stringify(vault, null, 2);

  fs.writeFileSync(tempPath, payload, { encoding: 'utf8', mode: 0o600 });
  fs.renameSync(tempPath, vaultPath);
}

function getStorageBackend() {
  if (typeof safeStorage.getSelectedStorageBackend === 'function') {
    return safeStorage.getSelectedStorageBackend();
  }

  return process.platform === 'linux' ? 'unknown' : 'native';
}

function createSecureSecretStore({ logger = console } = {}) {
  function getStatus() {
    const available = safeStorage.isEncryptionAvailable();
    const backend = getStorageBackend();
    const usingWeakBackend = backend === 'basic_text';
    const allowWeakBackend = process.env.ELECTRON_ALLOW_BASIC_TEXT_SECRET_STORAGE === 'true';
    const ready = available && (!usingWeakBackend || allowWeakBackend);

    return {
      available,
      backend,
      usingWeakBackend,
      allowWeakBackend,
      ready,
      vaultPath: getVaultPath(),
    };
  }

  function assertReady() {
    const status = getStatus();

    if (!status.available) {
      throw new Error('OS-backed secret storage is unavailable on this system.');
    }

    if (status.usingWeakBackend && !status.allowWeakBackend) {
      throw new Error('Electron safeStorage is using the weak basic_text backend. Refusing to store secrets until a secure keyring is available.');
    }

    return status;
  }

  function encryptSecret(value) {
    if (typeof value !== 'string') {
      throw new Error('Secret values must be strings.');
    }

    return safeStorage.encryptString(value).toString('base64');
  }

  function decryptSecret(value) {
    if (typeof value !== 'string' || value.trim() === '') {
      return null;
    }

    const encrypted = Buffer.from(value, 'base64');
    return safeStorage.decryptString(encrypted);
  }

  function getSecret({ namespace = 'default', key }) {
    assertReady();

    const vault = readVault();
    const entry = vault.secrets[buildEntryId(namespace, key)];

    if (!entry) {
      return null;
    }

    return decryptSecret(entry.value);
  }

  function setSecret({ namespace = 'default', key, value }) {
    const status = assertReady();
    const vault = readVault();
    const entryId = buildEntryId(namespace, key);

    vault.secrets[entryId] = {
      namespace: normalizeSegment(namespace, 'Secret namespace'),
      key: normalizeSegment(key, 'Secret key'),
      value: encryptSecret(value),
      updatedAt: new Date().toISOString(),
    };

    writeVault(vault);
    logger.info('[electron] stored secret in OS-backed desktop vault', {
      namespace,
      key,
      backend: status.backend,
    });
    return true;
  }

  function deleteSecret({ namespace = 'default', key }) {
    assertReady();

    const vault = readVault();
    const entryId = buildEntryId(namespace, key);
    const existed = Object.prototype.hasOwnProperty.call(vault.secrets, entryId);

    if (existed) {
      delete vault.secrets[entryId];
      writeVault(vault);
    }

    return existed;
  }

  return {
    deleteSecret,
    getSecret,
    getStatus,
    setSecret,
  };
}

module.exports = {
  createSecureSecretStore,
};
