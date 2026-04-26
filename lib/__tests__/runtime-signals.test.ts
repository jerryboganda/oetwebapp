import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { getAppRuntimeKind, getRuntimeBootstrapScript } from '../runtime-signals';

describe('getAppRuntimeKind', () => {
  let originalDataset: Record<string, string | undefined>;

  beforeEach(() => {
    // Snapshot dataset values we may mutate.
    originalDataset = {
      runtimeKind: document.documentElement.dataset.runtimeKind,
      desktopNative: document.documentElement.dataset.desktopNative,
      capacitorNative: document.documentElement.dataset.capacitorNative,
    };
  });

  afterEach(() => {
    // Restore dataset.
    for (const [key, value] of Object.entries(originalDataset)) {
      if (value === undefined) {
        delete document.documentElement.dataset[key];
      } else {
        document.documentElement.dataset[key] = value;
      }
    }
    // Clean window globals possibly added during tests.
    delete (window as Record<string, unknown>).desktopBridge;
    delete (window as Record<string, unknown>).Capacitor;
  });

  it('returns the documentElement.dataset.runtimeKind when valid', () => {
    document.documentElement.dataset.runtimeKind = 'desktop';
    expect(getAppRuntimeKind()).toBe('desktop');

    document.documentElement.dataset.runtimeKind = 'capacitor-native';
    expect(getAppRuntimeKind()).toBe('capacitor-native');

    document.documentElement.dataset.runtimeKind = 'web';
    expect(getAppRuntimeKind()).toBe('web');
  });

  it('ignores an invalid runtimeKind and falls through to data-* flags', () => {
    document.documentElement.dataset.runtimeKind = 'totally-bogus';
    document.documentElement.dataset.desktopNative = 'true';
    expect(getAppRuntimeKind()).toBe('desktop');
  });

  it('falls back to data-capacitor-native flag when present', () => {
    delete document.documentElement.dataset.runtimeKind;
    delete document.documentElement.dataset.desktopNative;
    document.documentElement.dataset.capacitorNative = 'true';
    expect(getAppRuntimeKind()).toBe('capacitor-native');
  });

  it('returns "desktop" when window.desktopBridge is set and dataset is empty', () => {
    delete document.documentElement.dataset.runtimeKind;
    delete document.documentElement.dataset.desktopNative;
    delete document.documentElement.dataset.capacitorNative;
    (window as Record<string, unknown>).desktopBridge = { platform: 'win32' };
    expect(getAppRuntimeKind()).toBe('desktop');
  });

  it('returns "capacitor-native" when Capacitor.isNativePlatform() returns true', () => {
    delete document.documentElement.dataset.runtimeKind;
    delete document.documentElement.dataset.desktopNative;
    delete document.documentElement.dataset.capacitorNative;
    (window as Record<string, unknown>).Capacitor = {
      isNativePlatform: () => true,
      getPlatform: () => 'ios',
    };
    expect(getAppRuntimeKind()).toBe('capacitor-native');
  });

  it('returns "web" by default', () => {
    delete document.documentElement.dataset.runtimeKind;
    delete document.documentElement.dataset.desktopNative;
    delete document.documentElement.dataset.capacitorNative;
    expect(getAppRuntimeKind()).toBe('web');
  });

  it('does not classify as capacitor-native when isNativePlatform returns false', () => {
    delete document.documentElement.dataset.runtimeKind;
    (window as Record<string, unknown>).Capacitor = {
      isNativePlatform: () => false,
    };
    expect(getAppRuntimeKind()).toBe('web');
  });
});

describe('getRuntimeBootstrapScript', () => {
  it('returns a self-invoking function string', () => {
    const script = getRuntimeBootstrapScript();
    expect(script).toMatch(/^\(\(\)\s*=>/);
    expect(script.trim().endsWith('})();')).toBe(true);
  });

  it('writes runtimeKind=desktop when desktopBridge is detected', () => {
    const fakeWindow = {
      desktopBridge: { platform: 'win32' },
    } as unknown as Window;
    const fakeRoot = {
      dataset: {} as Record<string, string>,
    };
    const fakeDocument = { documentElement: fakeRoot } as unknown as Document;

    const script = getRuntimeBootstrapScript();
    new Function('window', 'document', `${script}`)(fakeWindow, fakeDocument);

    expect(fakeRoot.dataset.runtimeKind).toBe('desktop');
    expect(fakeRoot.dataset.desktopNative).toBe('true');
    expect(fakeRoot.dataset.desktopPlatform).toBe('win32');
  });

  it('writes runtimeKind=capacitor-native when Capacitor.isNativePlatform() is true', () => {
    const fakeWindow = {
      Capacitor: {
        isNativePlatform: () => true,
        getPlatform: () => 'android',
      },
    } as unknown as Window;
    const fakeRoot = { dataset: {} as Record<string, string> };
    const fakeDocument = { documentElement: fakeRoot } as unknown as Document;

    const script = getRuntimeBootstrapScript();
    new Function('window', 'document', `${script}`)(fakeWindow, fakeDocument);

    expect(fakeRoot.dataset.runtimeKind).toBe('capacitor-native');
    expect(fakeRoot.dataset.capacitorNative).toBe('true');
    expect(fakeRoot.dataset.capacitorPlatform).toBe('android');
  });

  it('falls back to platform="native" when getPlatform is missing', () => {
    const fakeWindow = {
      Capacitor: { isNativePlatform: () => true },
    } as unknown as Window;
    const fakeRoot = { dataset: {} as Record<string, string> };
    const fakeDocument = { documentElement: fakeRoot } as unknown as Document;

    const script = getRuntimeBootstrapScript();
    new Function('window', 'document', `${script}`)(fakeWindow, fakeDocument);

    expect(fakeRoot.dataset.capacitorPlatform).toBe('native');
  });

  it('writes runtimeKind=web when neither bridge is present', () => {
    const fakeWindow = {} as unknown as Window;
    const fakeRoot = { dataset: {} as Record<string, string> };
    const fakeDocument = { documentElement: fakeRoot } as unknown as Document;

    const script = getRuntimeBootstrapScript();
    new Function('window', 'document', `${script}`)(fakeWindow, fakeDocument);

    expect(fakeRoot.dataset.runtimeKind).toBe('web');
    expect(fakeRoot.dataset.desktopNative).toBeUndefined();
    expect(fakeRoot.dataset.capacitorNative).toBeUndefined();
  });
});
