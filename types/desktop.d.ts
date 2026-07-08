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
        /** Shell version (CARGO_PKG_VERSION). Optional: shells < 0.6.0 omit it. */
        appVersion?: string;
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
    /**
     * Manual updater controls (desktop shell >= 0.6.0). Optional because older
     * shells lack them — feature-detect before calling. `check` reports whether
     * a newer signed build is available without installing; `install` downloads
     * + verifies + stages it (progress arrives via `onProgress`); `relaunch`
     * restarts into the freshly installed version.
     */
    updater?: {
      check: () => Promise<{
        available: boolean;
        version?: string;
        currentVersion: string;
        notes?: string | null;
      }>;
      install: () => Promise<{ ok: boolean; error?: string }>;
      relaunch: () => Promise<void>;
      /**
       * Subscribe to updater lifecycle events pushed from the Rust side:
       * `{ phase: 'available'|'downloading'|'installing'|'ready'|'error',
       *    version?, currentVersion?, progress?, notes?, error? }`.
       */
      onProgress: (
        listener: (event: {
          phase: 'available' | 'downloading' | 'installing' | 'ready' | 'error';
          version?: string;
          currentVersion?: string;
          progress?: number;
          notes?: string | null;
          error?: string;
        }) => void,
      ) => () => void;
    };
    /**
     * Hard reload (desktop shell >= 0.6.0). Clears the native WebView browsing
     * data (cache/storage) and re-navigates to the trusted remote origin —
     * the Ctrl+F5 equivalent. Optional; feature-detect before calling.
     */
    reload?: {
      hard: () => Promise<void>;
    };
    /**
     * Native HMAC signer for app-only video playback (desktop shell >= 0.4.0).
     * Optional because v0.3 shells lack it — feature-detect before calling.
     * Signs "{nonce}|{videoId}|{userId}|tauri|v1" with a build-time secret that
     * never leaves the Rust side; rejects on invalid input or rate flood.
     */
    attestation?: {
      signVideoChallenge(
        nonce: string,
        videoId: string,
        userId: string,
      ): Promise<{ signature: string; platform: 'tauri'; keyId: string; appVersion: string }>;
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
