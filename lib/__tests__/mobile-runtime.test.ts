const mobileMocks = vi.hoisted(() => {
  const createHandle = () => ({ remove: vi.fn() });
  const handles = {
    appState: createHandle(),
    backButton: createHandle(),
    keyboardShow: createHandle(),
    keyboardHide: createHandle(),
    network: createHandle(),
  };

  return {
    native: false,
    factories: {
      app: vi.fn(),
      keyboard: vi.fn(),
      network: vi.fn(),
      splashScreen: vi.fn(),
      statusBar: vi.fn(),
    },
    app: {
      addListener: vi.fn(async (eventName: string) =>
        eventName === 'appStateChange' ? handles.appState : handles.backButton,
      ),
      exitApp: vi.fn(async () => undefined),
    },
    keyboard: {
      addListener: vi.fn(async (eventName: string) =>
        eventName === 'keyboardWillShow' ? handles.keyboardShow : handles.keyboardHide,
      ),
    },
    network: {
      getStatus: vi.fn(async () => ({ connected: true, connectionType: 'wifi' as const })),
      addListener: vi.fn(async () => handles.network),
    },
    splashScreen: {
      hide: vi.fn(async () => undefined),
    },
    statusBar: {
      setOverlaysWebView: vi.fn(async () => undefined),
      setStyle: vi.fn(async () => undefined),
      setBackgroundColor: vi.fn(async () => undefined),
    },
    handles,
  };
});

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: () => mobileMocks.native,
    getPlatform: () => 'android',
  },
}));

vi.mock('@capacitor/app', () => {
  mobileMocks.factories.app();
  return { App: mobileMocks.app };
});

vi.mock('@capacitor/keyboard', () => {
  mobileMocks.factories.keyboard();
  return { Keyboard: mobileMocks.keyboard };
});

vi.mock('@capacitor/network', () => {
  mobileMocks.factories.network();
  return { Network: mobileMocks.network };
});

vi.mock('@capacitor/splash-screen', () => {
  mobileMocks.factories.splashScreen();
  return { SplashScreen: mobileMocks.splashScreen };
});

vi.mock('@capacitor/status-bar', () => {
  mobileMocks.factories.statusBar();
  return {
    StatusBar: mobileMocks.statusBar,
    Style: { Dark: 'DARK', Light: 'LIGHT' },
  };
});

import { initializeMobileRuntime } from '@/lib/mobile/runtime';

describe('mobile runtime', () => {
  beforeEach(() => {
    mobileMocks.native = false;
    vi.clearAllMocks();
  });

  afterEach(() => {
    delete window.desktopBridge;
    delete document.documentElement.dataset.runtimeKind;
    delete document.documentElement.dataset.desktopNative;
    delete document.documentElement.dataset.capacitorNative;
    delete document.documentElement.dataset.capacitorPlatform;
    delete document.documentElement.dataset.appActive;
    delete document.documentElement.dataset.windowFocused;
    delete document.documentElement.dataset.windowVisible;
    delete document.documentElement.dataset.windowMinimized;
    delete document.documentElement.dataset.windowMaximized;
    delete document.documentElement.dataset.windowFullscreen;
    delete document.documentElement.dataset.colorScheme;
    delete document.documentElement.dataset.networkConnected;
    delete document.documentElement.dataset.keyboardVisible;
    document.documentElement.style.removeProperty('--app-viewport-height');
    document.documentElement.style.removeProperty('--app-keyboard-offset');
    document.documentElement.style.removeProperty('color-scheme');
  });

  it('does not overwrite desktop runtime signals', async () => {
    window.desktopBridge = {
      platform: 'win32',
      versions: {
        chrome: '133.0.0',
        node: '22.0.0',
      },
      openExternal: async () => true,
      runtime: {
        info: async () => ({
          isPackaged: false,
          activeBackendUrl: null,
          ignoredPackagedLoopbackApiTarget: null,
          windowState: {
            isFocused: true,
            isVisible: true,
            isMinimized: false,
            isMaximized: false,
            isFullScreen: false,
          },
        }),
        onWindowStateChange: () => () => undefined,
      },
      speakingAudio: {
        start: async (sessionId: string) => ({ ok: true, sessionId, mimeType: 'audio/webm', mode: 'ipc' }),
        stop: async (sessionId: string) => ({ ok: true, sessionId }),
        getBlob: async (sessionId: string) => ({ ok: true, sessionId }),
        discard: async (sessionId: string) => ({ ok: true, sessionId }),
        getPlatform: () => 'win32' as NodeJS.Platform,
      },
      secureSecrets: {
        get: async () => null,
        set: async () => true,
        delete: async () => false,
        status: async () => ({
          available: true,
          backend: 'basic_text',
          usingWeakBackend: true,
          allowWeakBackend: true,
          ready: true,
          vaultPath: 'C:/tmp/desktop-secrets.json',
        }),
      },
      offlineCache: {
        store: async (key) => ({ success: true, key }),
        get: async () => null,
        delete: async (key) => ({ success: true, key }),
        list: async () => [],
        clear: async () => ({ success: true, cleared: 0 }),
      },
      notifications: {
        show: async () => ({ ok: true }),
      },
      fileInfo: {
        getDroppedFileInfo: async () => ({ ok: false, error: 'NOT_A_FILE' as const }),
      },
      print: {
        printPage: async () => ({ ok: true }),
      },
    };

    const cleanup = await initializeMobileRuntime();

    expect(document.documentElement.dataset.runtimeKind).toBeUndefined();
    expect(document.documentElement.dataset.capacitorNative).toBeUndefined();
    expect(document.documentElement.dataset.appActive).toBeUndefined();

    cleanup();
  });

  it('does not stamp capacitor-native on a plain web browser', async () => {
    // No desktopBridge and Capacitor.isNativePlatform() === false in jsdom, so
    // the web app must NOT be mislabelled as a native shell (regression: the
    // update toolbar was appearing on the website).
    const cleanup = await initializeMobileRuntime();

    expect(document.documentElement.dataset.runtimeKind).toBeUndefined();
    expect(document.documentElement.dataset.capacitorNative).toBeUndefined();
    expect(document.documentElement.dataset.appActive).toBeUndefined();
    expect(mobileMocks.factories.app).not.toHaveBeenCalled();
    expect(mobileMocks.factories.keyboard).not.toHaveBeenCalled();
    expect(mobileMocks.factories.network).not.toHaveBeenCalled();
    expect(mobileMocks.factories.splashScreen).not.toHaveBeenCalled();
    expect(mobileMocks.factories.statusBar).not.toHaveBeenCalled();
    expect(mobileMocks.app.addListener).not.toHaveBeenCalled();
    expect(mobileMocks.keyboard.addListener).not.toHaveBeenCalled();
    expect(mobileMocks.network.getStatus).not.toHaveBeenCalled();
    expect(mobileMocks.splashScreen.hide).not.toHaveBeenCalled();
    expect(mobileMocks.statusBar.setStyle).not.toHaveBeenCalled();

    cleanup();
  });

  it('initializes native plugins once and removes registered listeners during cleanup', async () => {
    mobileMocks.native = true;

    const cleanup = await initializeMobileRuntime();

    expect(mobileMocks.factories.app).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.keyboard).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.network).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.splashScreen).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.statusBar).toHaveBeenCalledTimes(1);
    expect(mobileMocks.keyboard.addListener).toHaveBeenCalledTimes(2);
    expect(mobileMocks.network.getStatus).toHaveBeenCalledTimes(1);
    expect(mobileMocks.network.addListener).toHaveBeenCalledTimes(1);
    expect(mobileMocks.app.addListener).toHaveBeenCalledTimes(2);
    expect(mobileMocks.splashScreen.hide).toHaveBeenCalledTimes(1);
    expect(mobileMocks.statusBar.setStyle).toHaveBeenCalledTimes(1);
    expect(document.documentElement.dataset.runtimeKind).toBe('capacitor-native');
    expect(document.documentElement.dataset.capacitorPlatform).toBe('android');

    cleanup();

    expect(mobileMocks.handles.keyboardShow.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.keyboardHide.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.network.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.appState.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.backButton.remove).toHaveBeenCalledTimes(1);
  });
});