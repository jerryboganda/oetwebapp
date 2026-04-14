import { initializeMobileRuntime } from '@/lib/mobile/runtime';

describe('mobile runtime', () => {
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
  });

  it('does not overwrite desktop runtime signals', async () => {
    window.desktopBridge = {
      platform: 'win32',
      versions: {
        electron: '41.0.0',
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
});