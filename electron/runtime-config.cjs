const fs = require('fs');
const path = require('path');

const RUNTIME_CONFIG_FILE = 'desktop-runtime-config.json';

function normalizeAbsoluteHttpUrl(value) {
  if (typeof value !== 'string') {
    return null;
  }

  const trimmedValue = value.trim();
  if (!trimmedValue) {
    return null;
  }

  try {
    const parsedUrl = new URL(trimmedValue);
    if (parsedUrl.protocol !== 'http:' && parsedUrl.protocol !== 'https:') {
      return null;
    }

    return parsedUrl.toString().replace(/\/$/, '');
  } catch {
    return null;
  }
}

function isLoopbackHostname(hostname) {
  return hostname === 'localhost'
    || hostname === '127.0.0.1'
    || hostname === '::1'
    || hostname === '[::1]';
}

function isLoopbackHttpUrl(value) {
  const normalizedUrl = normalizeAbsoluteHttpUrl(value);
  if (!normalizedUrl) {
    return false;
  }

  try {
    return isLoopbackHostname(new URL(normalizedUrl).hostname);
  } catch {
    return false;
  }
}

function selectConfiguredDesktopApiBaseUrl(environment = process.env, options = {}) {
  const allowLoopback = options.allowLoopback === true;
  const candidates = [
    environment.PUBLIC_API_BASE_URL,
    environment.API_PROXY_TARGET_URL,
    environment.NEXT_PUBLIC_API_BASE_URL,
  ];

  for (const candidate of candidates) {
    const normalizedUrl = normalizeAbsoluteHttpUrl(candidate);
    if (!normalizedUrl) {
      continue;
    }

    if (!allowLoopback && isLoopbackHttpUrl(normalizedUrl)) {
      continue;
    }

    return normalizedUrl;
  }

  return null;
}

function createDesktopRuntimeConfig(environment = process.env, options = {}) {
  const publicApiBaseUrl = selectConfiguredDesktopApiBaseUrl(environment, options);

  if (!publicApiBaseUrl) {
    return {};
  }

  return { publicApiBaseUrl };
}

function getDesktopRuntimeConfigPath({ resourcesPath, appPath, isPackaged }) {
  const basePath = isPackaged ? resourcesPath : appPath;
  return path.join(basePath, RUNTIME_CONFIG_FILE);
}

function getDesktopRuntimeConfigOverridePath({ userDataPath }) {
  if (!userDataPath) {
    return null;
  }

  return path.join(userDataPath, RUNTIME_CONFIG_FILE);
}

function readRuntimeConfigFile(runtimeConfigPath, logger) {
  if (!runtimeConfigPath || !fs.existsSync(runtimeConfigPath)) {
    return {};
  }

  try {
    const rawContent = fs.readFileSync(runtimeConfigPath, 'utf8').replace(/^\uFEFF/, '');
    return JSON.parse(rawContent);
  } catch (error) {
    logger?.warn?.('[electron] failed to load desktop runtime config', {
      runtimeConfigPath,
      error,
    });
    return {};
  }
}

function loadDesktopRuntimeConfig(options) {
  const packagedConfig = readRuntimeConfigFile(getDesktopRuntimeConfigPath(options), options.logger);
  const overrideConfig = readRuntimeConfigFile(getDesktopRuntimeConfigOverridePath(options), options.logger);
  return { ...packagedConfig, ...overrideConfig };
}

function resolveDesktopApiBaseUrl(environment = process.env, runtimeConfig = {}, options = {}) {
  return selectConfiguredDesktopApiBaseUrl(environment, options)
    || (
      options.allowLoopback !== true && isLoopbackHttpUrl(runtimeConfig.publicApiBaseUrl)
        ? null
        : normalizeAbsoluteHttpUrl(runtimeConfig.publicApiBaseUrl)
    )
    || null;
}

module.exports = {
  RUNTIME_CONFIG_FILE,
  createDesktopRuntimeConfig,
  getDesktopRuntimeConfigPath,
  getDesktopRuntimeConfigOverridePath,
  isLoopbackHttpUrl,
  loadDesktopRuntimeConfig,
  normalizeAbsoluteHttpUrl,
  resolveDesktopApiBaseUrl,
  selectConfiguredDesktopApiBaseUrl,
};
