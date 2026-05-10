export {};

declare global {
  interface CapacitorGlobal {
    isNativePlatform?: () => boolean;
    getPlatform?: () => string;
  }

  interface DesktopBridge {
    platform: NodeJS.Platform;
    versions: {
      electron: string;
      chrome: string;
      node: string;
    };
    openExternal: (url: string) => Promise<boolean>;
    runtime: {
      info: () => Promise<{
        isPackaged: boolean;
        activeBackendUrl: string | null;
        ignoredPackagedLoopbackApiTarget: string | null;
        windowState?: {
          isFocused: boolean;
          isVisible: boolean;
          isMinimized: boolean;
          isMaximized: boolean;
          isFullScreen: boolean;
        };
      }>;
      onWindowStateChange?: (
        callback: (windowState: {
          isFocused: boolean;
          isVisible: boolean;
          isMinimized: boolean;
          isMaximized: boolean;
          isFullScreen: boolean;
        }) => void,
      ) => () => void;
    };
    secureSecrets: {
      get: (namespace: string, key: string) => Promise<string | null>;
      set: (namespace: string, key: string, value: string) => Promise<boolean>;
      delete: (namespace: string, key: string) => Promise<boolean>;
      status: () => Promise<{
        available: boolean;
        backend: string;
        usingWeakBackend: boolean;
        allowWeakBackend: boolean;
        ready: boolean;
        vaultPath: string;
      }>;
    };
    offlineCache: {
      store: (key: string, data: unknown) => Promise<{ success: true; key: string }>;
      get: (key: string) => Promise<{ cachedAt: number; data: unknown } | null>;
      delete: (key: string) => Promise<{ success: true; key: string }>;
      list: () => Promise<Array<{ key: string; sizeBytes: number; modifiedAt: number }>>;
      clear: () => Promise<{ success: true; cleared: number }>;
    };
    notifications: {
      show: (title: string, body: string, route?: string) => Promise<{ ok: boolean }>;
    };
    fileInfo: {
      getDroppedFileInfo: (filePath: string) => Promise<
        | { ok: true; name: string; size: number; path: string; lastModified: number }
        | { ok: false; error: string }
      >;
    };
    print: {
      printPage: (options?: Record<string, unknown>) => Promise<{ ok: boolean; error?: string }>;
    };
  }

  interface Window {
    desktopBridge?: DesktopBridge;
    Capacitor?: CapacitorGlobal;
  }
}
