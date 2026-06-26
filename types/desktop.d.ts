export {};

declare global {
  interface CapacitorGlobal {
    isNativePlatform?: () => boolean;
    getPlatform?: () => string;
  }

  interface DesktopBridge {
    platform: NodeJS.Platform;
    versions: {
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
    speakingAudio: {
      start: (sessionId: string, mimeType?: string) => Promise<{
        ok: boolean;
        sessionId: string;
        mimeType: string;
        mode: string;
      }>;
      stop: (sessionId: string, chunks?: Array<ArrayBuffer | ArrayBufferView>) => Promise<{
        ok: boolean;
        sessionId?: string;
        sizeBytes?: number;
        mimeType?: string;
        filePath?: string;
        durationMs?: number;
        error?: string;
      }>;
      getBlob: (sessionId: string) => Promise<{
        ok: boolean;
        sessionId?: string;
        mimeType?: string;
        sizeBytes?: number;
        data?: ArrayBuffer;
        error?: string;
      }>;
      discard: (sessionId: string) => Promise<{ ok: boolean; sessionId: string }>;
      getPlatform: () => NodeJS.Platform;
    };
  }

  interface Window {
    desktopBridge?: DesktopBridge;
    Capacitor?: CapacitorGlobal;
  }
}
