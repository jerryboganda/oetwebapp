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
  }

  interface Window {
    desktopBridge?: DesktopBridge;
  }
}
