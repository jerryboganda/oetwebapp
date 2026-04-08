export type AppRuntimeKind = 'web' | 'desktop' | 'capacitor-native';

export interface DesktopWindowStateSnapshot {
  isFocused: boolean;
  isVisible: boolean;
  isMinimized: boolean;
  isMaximized: boolean;
  isFullScreen: boolean;
}

export interface DesktopRuntimeInfo {
  isPackaged: boolean;
  activeBackendUrl: string | null;
  ignoredPackagedLoopbackApiTarget: string | null;
  windowState: DesktopWindowStateSnapshot;
}

type NativeCapacitorBridge = {
  isNativePlatform?: () => boolean;
  getPlatform?: () => string;
};

type RuntimeWindow = Window & {
  desktopBridge?: {
    platform: NodeJS.Platform;
    runtime?: {
      info?: () => Promise<DesktopRuntimeInfo>;
      onWindowStateChange?: (listener: (windowState: DesktopWindowStateSnapshot) => void) => () => void;
    };
  };
  Capacitor?: NativeCapacitorBridge;
};

function isRuntimeKind(value: string | null | undefined): value is AppRuntimeKind {
  return value === 'web' || value === 'desktop' || value === 'capacitor-native';
}

export function getAppRuntimeKind(): AppRuntimeKind {
  if (typeof document !== 'undefined') {
    const runtimeKind = document.documentElement.dataset.runtimeKind;
    if (isRuntimeKind(runtimeKind)) {
      return runtimeKind;
    }

    if (document.documentElement.dataset.desktopNative === 'true') {
      return 'desktop';
    }

    if (document.documentElement.dataset.capacitorNative === 'true') {
      return 'capacitor-native';
    }
  }

  if (typeof window !== 'undefined') {
    const runtimeWindow = window as RuntimeWindow;

    if (runtimeWindow.desktopBridge) {
      return 'desktop';
    }

    if (runtimeWindow.Capacitor?.isNativePlatform?.()) {
      return 'capacitor-native';
    }
  }

  return 'web';
}

export function getRuntimeBootstrapScript() {
  return `(() => {
  const root = document.documentElement;
  const desktopBridge = window.desktopBridge;
  const capacitor = window.Capacitor;

  if (desktopBridge) {
    root.dataset.runtimeKind = 'desktop';
    root.dataset.desktopNative = 'true';
    root.dataset.desktopPlatform = desktopBridge.platform;
    return;
  }

  if (capacitor?.isNativePlatform?.()) {
    root.dataset.runtimeKind = 'capacitor-native';
    root.dataset.capacitorNative = 'true';
    root.dataset.capacitorPlatform = capacitor.getPlatform?.() ?? 'native';
    return;
  }

  root.dataset.runtimeKind = 'web';
})();`;
}