import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';

const runtimeConfig = require('../runtime-config.cjs') as {
  RUNTIME_CONFIG_FILE: string;
  createDesktopRuntimeConfig: (environment?: NodeJS.ProcessEnv, options?: { allowLoopback?: boolean }) => Record<string, unknown>;
  getDesktopRuntimeConfigOverridePath: (options: { userDataPath?: string | null }) => string | null;
  isLoopbackHttpUrl: (value: unknown) => boolean;
  loadDesktopRuntimeConfig: (options: {
    resourcesPath: string;
    appPath: string;
    userDataPath?: string;
    isPackaged: boolean;
    logger?: { warn?: (message: string, details: unknown) => void };
  }) => Record<string, unknown>;
  normalizeAbsoluteHttpUrl: (value: unknown) => string | null;
  resolveDesktopApiBaseUrl: (
    environment?: NodeJS.ProcessEnv,
    runtimeConfig?: Record<string, unknown>,
    options?: { allowLoopback?: boolean }
  ) => string | null;
};

const temporaryDirectories: string[] = [];

afterEach(async () => {
  while (temporaryDirectories.length > 0) {
    const directory = temporaryDirectories.pop();
    if (!directory) {
      continue;
    }

    await rm(directory, { recursive: true, force: true });
  }
});

describe('desktop runtime config helpers', () => {
  it('normalizes only absolute http urls', () => {
    expect(runtimeConfig.normalizeAbsoluteHttpUrl('https://api.example.com/')).toBe('https://api.example.com');
    expect(runtimeConfig.normalizeAbsoluteHttpUrl('http://127.0.0.1:5198/')).toBe('http://127.0.0.1:5198');
    expect(runtimeConfig.normalizeAbsoluteHttpUrl('/api/backend')).toBeNull();
    expect(runtimeConfig.normalizeAbsoluteHttpUrl('')).toBeNull();
    expect(runtimeConfig.normalizeAbsoluteHttpUrl(undefined)).toBeNull();
  });

  it('detects loopback api targets', () => {
    expect(runtimeConfig.isLoopbackHttpUrl('http://127.0.0.1:5198')).toBe(true);
    expect(runtimeConfig.isLoopbackHttpUrl('http://localhost:5198')).toBe(true);
    expect(runtimeConfig.isLoopbackHttpUrl('https://api.example.com')).toBe(false);
  });

  it('creates a packaged runtime config only for absolute api urls', () => {
    expect(
      runtimeConfig.createDesktopRuntimeConfig({
        NEXT_PUBLIC_API_BASE_URL: '/api/backend',
      } as NodeJS.ProcessEnv),
    ).toEqual({});

    expect(
      runtimeConfig.createDesktopRuntimeConfig({
        PUBLIC_API_BASE_URL: 'https://api.example.com',
      } as NodeJS.ProcessEnv),
    ).toEqual({
      publicApiBaseUrl: 'https://api.example.com',
    });
  });

  it('does not persist loopback api targets for packaged builds unless explicitly allowed', () => {
    expect(
      runtimeConfig.createDesktopRuntimeConfig({
        PUBLIC_API_BASE_URL: 'http://127.0.0.1:5198',
      } as NodeJS.ProcessEnv),
    ).toEqual({});

    expect(
      runtimeConfig.createDesktopRuntimeConfig(
        {
          PUBLIC_API_BASE_URL: 'http://127.0.0.1:5198',
        } as NodeJS.ProcessEnv,
        { allowLoopback: true },
      ),
    ).toEqual({
      publicApiBaseUrl: 'http://127.0.0.1:5198',
    });
  });

  it('prefers explicit environment overrides over persisted packaged config', () => {
    const resolvedUrl = runtimeConfig.resolveDesktopApiBaseUrl(
      {
        API_PROXY_TARGET_URL: 'https://override.example.com',
        NEXT_PUBLIC_API_BASE_URL: '/api/backend',
      } as NodeJS.ProcessEnv,
      {
        publicApiBaseUrl: 'https://persisted.example.com',
      },
    );

    expect(resolvedUrl).toBe('https://override.example.com');
  });

  it('falls back to persisted packaged config when runtime env is relative', () => {
    const resolvedUrl = runtimeConfig.resolveDesktopApiBaseUrl(
      {
        NEXT_PUBLIC_API_BASE_URL: '/api/backend',
      } as NodeJS.ProcessEnv,
      {
        publicApiBaseUrl: 'https://api.example.com',
      },
    );

    expect(resolvedUrl).toBe('https://api.example.com');
  });

  it('ignores persisted loopback config for packaged resolution unless explicitly allowed', () => {
    expect(
      runtimeConfig.resolveDesktopApiBaseUrl(
        {} as NodeJS.ProcessEnv,
        { publicApiBaseUrl: 'http://127.0.0.1:5198' },
      ),
    ).toBeNull();

    expect(
      runtimeConfig.resolveDesktopApiBaseUrl(
        {} as NodeJS.ProcessEnv,
        { publicApiBaseUrl: 'http://127.0.0.1:5198' },
        { allowLoopback: true },
      ),
    ).toBe('http://127.0.0.1:5198');
  });

  it('loads the packaged runtime config from resources when present', async () => {
    const resourcesPath = await mkdtemp(path.join(os.tmpdir(), 'oet-runtime-config-'));
    temporaryDirectories.push(resourcesPath);

    await writeFile(
      path.join(resourcesPath, runtimeConfig.RUNTIME_CONFIG_FILE),
      JSON.stringify({ publicApiBaseUrl: 'https://api.example.com' }),
      'utf8',
    );

    const loadedConfig = runtimeConfig.loadDesktopRuntimeConfig({
      resourcesPath,
      appPath: path.join(resourcesPath, 'app'),
      isPackaged: true,
      logger: console,
    });

    expect(loadedConfig).toEqual({ publicApiBaseUrl: 'https://api.example.com' });
  });

  it('prefers the user-data override config over packaged resources', async () => {
    const resourcesPath = await mkdtemp(path.join(os.tmpdir(), 'oet-runtime-config-resources-'));
    const userDataPath = await mkdtemp(path.join(os.tmpdir(), 'oet-runtime-config-user-data-'));
    temporaryDirectories.push(resourcesPath, userDataPath);

    await writeFile(
      path.join(resourcesPath, runtimeConfig.RUNTIME_CONFIG_FILE),
      JSON.stringify({ publicApiBaseUrl: 'https://packaged.example.com' }),
      'utf8',
    );

    const overridePath = runtimeConfig.getDesktopRuntimeConfigOverridePath({ userDataPath });
    expect(overridePath).toBe(path.join(userDataPath, runtimeConfig.RUNTIME_CONFIG_FILE));

    await writeFile(
      overridePath!,
      JSON.stringify({ publicApiBaseUrl: 'https://override.example.com' }),
      'utf8',
    );

    const loadedConfig = runtimeConfig.loadDesktopRuntimeConfig({
      resourcesPath,
      appPath: path.join(resourcesPath, 'app'),
      userDataPath,
      isPackaged: true,
      logger: console,
    });

    expect(loadedConfig).toEqual({ publicApiBaseUrl: 'https://override.example.com' });
  });

  it('returns an empty config and warns when persisted json is invalid', async () => {
    const resourcesPath = await mkdtemp(path.join(os.tmpdir(), 'oet-runtime-config-invalid-'));
    temporaryDirectories.push(resourcesPath);

    await writeFile(
      path.join(resourcesPath, runtimeConfig.RUNTIME_CONFIG_FILE),
      '{not-valid-json',
      'utf8',
    );

    const warn = vi.fn();
    const loadedConfig = runtimeConfig.loadDesktopRuntimeConfig({
      resourcesPath,
      appPath: path.join(resourcesPath, 'app'),
      isPackaged: true,
      logger: { warn },
    });

    expect(loadedConfig).toEqual({});
    expect(warn).toHaveBeenCalledOnce();
  });

  it('loads runtime config json even when the file starts with a utf-8 bom', async () => {
    const resourcesPath = await mkdtemp(path.join(os.tmpdir(), 'oet-runtime-config-bom-'));
    temporaryDirectories.push(resourcesPath);

    await writeFile(
      path.join(resourcesPath, runtimeConfig.RUNTIME_CONFIG_FILE),
      '\uFEFF{"publicApiBaseUrl":"https://api.example.com"}',
      'utf8',
    );

    const loadedConfig = runtimeConfig.loadDesktopRuntimeConfig({
      resourcesPath,
      appPath: path.join(resourcesPath, 'app'),
      isPackaged: true,
      logger: console,
    });

    expect(loadedConfig).toEqual({ publicApiBaseUrl: 'https://api.example.com' });
  });
});
