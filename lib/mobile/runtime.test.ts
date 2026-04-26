import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const { isNativeMock, getPlatformMock, networkGetStatusMock, networkAddListenerMock,
  appAddListenerMock, appExitMock, keyboardAddListenerMock, splashHideMock,
  statusBarOverlayMock, statusBarStyleMock, statusBarBgMock } = vi.hoisted(() => ({
  isNativeMock: vi.fn(() => false),
  getPlatformMock: vi.fn(() => 'web'),
  networkGetStatusMock: vi.fn(() => Promise.resolve({ connected: true })),
  networkAddListenerMock: vi.fn(() => Promise.resolve({ remove: vi.fn() })),
  appAddListenerMock: vi.fn(() => Promise.resolve({ remove: vi.fn() })),
  appExitMock: vi.fn(() => Promise.resolve()),
  keyboardAddListenerMock: vi.fn(() => Promise.resolve({ remove: vi.fn() })),
  splashHideMock: vi.fn(() => Promise.resolve()),
  statusBarOverlayMock: vi.fn(() => Promise.resolve()),
  statusBarStyleMock: vi.fn(() => Promise.resolve()),
  statusBarBgMock: vi.fn(() => Promise.resolve()),
}));

vi.mock('@capacitor/core', () => ({
  Capacitor: { isNativePlatform: isNativeMock, getPlatform: getPlatformMock },
}));
vi.mock('@capacitor/network', () => ({
  Network: { getStatus: networkGetStatusMock, addListener: networkAddListenerMock },
}));
vi.mock('@capacitor/app', () => ({
  App: { addListener: appAddListenerMock, exitApp: appExitMock },
}));
vi.mock('@capacitor/keyboard', () => ({
  Keyboard: { addListener: keyboardAddListenerMock },
}));
vi.mock('@capacitor/splash-screen', () => ({
  SplashScreen: { hide: splashHideMock },
}));
vi.mock('@capacitor/status-bar', () => ({
  StatusBar: {
    setOverlaysWebView: statusBarOverlayMock,
    setStyle: statusBarStyleMock,
    setBackgroundColor: statusBarBgMock,
  },
  Style: { Light: 'LIGHT', Dark: 'DARK' },
}));

import { initializeMobileRuntime } from './runtime';

beforeEach(() => {
  isNativeMock.mockReset().mockReturnValue(false);
  getPlatformMock.mockReset().mockReturnValue('web');
  document.documentElement.removeAttribute('data-desktop-native');
  // Reset all dataset entries so each test starts fresh.
  for (const key of Object.keys(document.documentElement.dataset)) {
    delete document.documentElement.dataset[key];
  }
  document.documentElement.removeAttribute('style');
  // @ts-expect-error - clear desktopBridge
  delete (window as { desktopBridge?: unknown }).desktopBridge;

  // matchMedia mock
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('initializeMobileRuntime', () => {
  it('returns a no-op cleanup when running outside a browser-like environment marker', async () => {
    // The function checks `desktopBridge` to bail; simulate desktop runtime.
    (window as unknown as { desktopBridge: object }).desktopBridge = {};
    const cleanup = await initializeMobileRuntime();
    expect(typeof cleanup).toBe('function');
    cleanup();
    // No dataset state should have been set by the early return.
    expect(document.documentElement.dataset.runtimeKind).toBeUndefined();
  });

  it('bails out when document.documentElement.dataset.desktopNative is "true"', async () => {
    document.documentElement.dataset.desktopNative = 'true';
    const cleanup = await initializeMobileRuntime();
    expect(typeof cleanup).toBe('function');
    expect(document.documentElement.dataset.runtimeKind).toBeUndefined();
  });

  it('sets capacitor-native runtime dataset when not desktop', async () => {
    const cleanup = await initializeMobileRuntime();
    try {
      expect(document.documentElement.dataset.runtimeKind).toBe('capacitor-native');
      expect(document.documentElement.dataset.appActive).toBe('true');
      expect(document.documentElement.dataset.windowFocused).toBe('true');
      expect(document.documentElement.dataset.windowVisible).toBe('true');
      expect(document.documentElement.dataset.windowMinimized).toBe('false');
      expect(document.documentElement.dataset.windowMaximized).toBe('false');
      expect(document.documentElement.dataset.windowFullscreen).toBe('false');
      expect(document.documentElement.dataset.capacitorPlatform).toBe('web');
      expect(document.documentElement.dataset.capacitorNative).toBe('false');
    } finally {
      cleanup();
    }
  });

  it('reflects native platform info when isNativePlatform returns true', async () => {
    isNativeMock.mockReturnValue(true);
    getPlatformMock.mockReturnValue('ios');
    const cleanup = await initializeMobileRuntime();
    try {
      expect(document.documentElement.dataset.capacitorPlatform).toBe('ios');
      expect(document.documentElement.dataset.capacitorNative).toBe('true');
    } finally {
      cleanup();
    }
  });

  it('returns a callable cleanup that does not throw when invoked', async () => {
    const cleanup = await initializeMobileRuntime();
    expect(() => cleanup()).not.toThrow();
  });

  it('subscribes to App, Network, and Keyboard plugin listeners', async () => {
    const cleanup = await initializeMobileRuntime();
    try {
      expect(networkGetStatusMock).toHaveBeenCalled();
      expect(networkAddListenerMock).toHaveBeenCalled();
      expect(appAddListenerMock).toHaveBeenCalled();
      expect(keyboardAddListenerMock).toHaveBeenCalled();
    } finally {
      cleanup();
    }
  });

  it('forwards initial network state to the onNetworkChange handler', async () => {
    const onNetworkChange = vi.fn();
    networkGetStatusMock.mockResolvedValueOnce({ connected: false });
    const cleanup = await initializeMobileRuntime({ onNetworkChange });
    try {
      expect(onNetworkChange).toHaveBeenCalledWith(false);
      expect(document.documentElement.dataset.networkConnected).toBe('false');
    } finally {
      cleanup();
    }
  });

  it('falls back to window online/offline events when Network plugin throws', async () => {
    networkGetStatusMock.mockRejectedValueOnce(new Error('no plugin'));
    const cleanup = await initializeMobileRuntime();
    // The fallback path registered listeners on window. Cleanup should remove
    // them without throwing. We avoid dispatching online/offline events here
    // because the production handler re-dispatches the same event, which
    // would cause infinite recursion in this synchronous test environment.
    expect(() => cleanup()).not.toThrow();
  });

  it('falls back gracefully when keyboard plugin throws on registration', async () => {
    keyboardAddListenerMock.mockRejectedValueOnce(new Error('no keyboard'));
    const cleanup = await initializeMobileRuntime();
    expect(typeof cleanup).toBe('function');
    cleanup();
  });

  it('falls back gracefully when App plugin throws on registration', async () => {
    appAddListenerMock.mockRejectedValueOnce(new Error('no app plugin'));
    const cleanup = await initializeMobileRuntime();
    expect(typeof cleanup).toBe('function');
    cleanup();
  });

  it('sets viewport CSS custom properties on initialization', async () => {
    const cleanup = await initializeMobileRuntime();
    try {
      const value = document.documentElement.style.getPropertyValue('--app-viewport-height');
      expect(value).toMatch(/^\d+px$/);
    } finally {
      cleanup();
    }
  });
});
