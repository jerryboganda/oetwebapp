export {};

declare global {
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
      }>;
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
  }

  interface Window {
    desktopBridge?: DesktopBridge;
  }
}
