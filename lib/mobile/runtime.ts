'use client';

import { Capacitor } from '@capacitor/core';

type AppModule = typeof import('@capacitor/app');
type KeyboardModule = typeof import('@capacitor/keyboard');
type NetworkModule = typeof import('@capacitor/network');
type SplashScreenModule = typeof import('@capacitor/splash-screen');
type StatusBarModule = typeof import('@capacitor/status-bar');

let appModulePromise: Promise<AppModule> | null = null;
let keyboardModulePromise: Promise<KeyboardModule> | null = null;
let networkModulePromise: Promise<NetworkModule> | null = null;
let splashScreenModulePromise: Promise<SplashScreenModule> | null = null;
let statusBarModulePromise: Promise<StatusBarModule> | null = null;

function loadAppModule(): Promise<AppModule> {
  appModulePromise ??= import('@capacitor/app');
  return appModulePromise;
}

function loadKeyboardModule(): Promise<KeyboardModule> {
  keyboardModulePromise ??= import('@capacitor/keyboard');
  return keyboardModulePromise;
}

function loadNetworkModule(): Promise<NetworkModule> {
  networkModulePromise ??= import('@capacitor/network');
  return networkModulePromise;
}

function loadSplashScreenModule(): Promise<SplashScreenModule> {
  splashScreenModulePromise ??= import('@capacitor/splash-screen');
  return splashScreenModulePromise;
}

function loadStatusBarModule(): Promise<StatusBarModule> {
  statusBarModulePromise ??= import('@capacitor/status-bar');
  return statusBarModulePromise;
}

export interface MobileRuntimeHandlers {
  onResume?: () => void;
  onPause?: () => void;
  onNetworkChange?: (connected: boolean) => void;
  onBackButton?: () => void;
}
function isBrowser() {
  return typeof window !== 'undefined';
}

function getPreferredColorScheme() {
  if (!isBrowser()) {
    return 'light' as const;
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function setViewportMetrics() {
  if (!isBrowser()) {
    return;
  }

  const viewport = window.visualViewport;
  const viewportHeight = viewport?.height ?? window.innerHeight;
  const keyboardOffset = Math.max(0, window.innerHeight - viewportHeight);

  document.documentElement.style.setProperty('--app-viewport-height', `${viewportHeight}px`);
  document.documentElement.style.setProperty('--app-keyboard-offset', `${keyboardOffset}px`);
}

function scheduleViewportMetrics() {
  if (!isBrowser()) {
    return;
  }

  window.requestAnimationFrame(() => {
    setViewportMetrics();
  });
}

async function syncNativeChrome() {
  if (!isBrowser() || !Capacitor.isNativePlatform()) {
    return;
  }

  let statusBarModule: StatusBarModule;
  try {
    statusBarModule = await loadStatusBarModule();
  } catch {
    return;
  }

  const { StatusBar, Style } = statusBarModule;
  const isDark = getPreferredColorScheme() === 'dark';

  document.documentElement.dataset.colorScheme = isDark ? 'dark' : 'light';
  document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';

  await Promise.allSettled([
    StatusBar.setOverlaysWebView({ overlay: false }),
    StatusBar.setStyle({ style: isDark ? Style.Light : Style.Dark }),
    StatusBar.setBackgroundColor({ color: isDark ? '#07111d' : '#f7f5ef' }),
  ]);
}

function syncOnlineState(connected: boolean, onNetworkChange?: (connected: boolean) => void) {
  if (!isBrowser()) {
    return;
  }

  document.documentElement.dataset.networkConnected = connected ? 'true' : 'false';
  window.dispatchEvent(new Event(connected ? 'online' : 'offline'));
  onNetworkChange?.(connected);
}

export async function initializeMobileRuntime(handlers: MobileRuntimeHandlers = {}): Promise<() => void> {
  if (!isBrowser()) {
    return () => undefined;
  }

  if (window.desktopBridge || document.documentElement.dataset.desktopNative === 'true') {
    return () => undefined;
  }

  // Plain web browser (not a native shell): do NOT stamp capacitor-native.
  // Without this guard the web app is mislabelled as 'capacitor-native'
  // (leaving data-runtime-kind wrong for getAppRuntimeKind), which surfaced
  // shell-only UI like the update toolbar on the website.
  if (!Capacitor.isNativePlatform()) {
    return () => undefined;
  }

  setViewportMetrics();
  document.documentElement.dataset.runtimeKind = 'capacitor-native';
  document.documentElement.dataset.colorScheme = getPreferredColorScheme();
  document.documentElement.dataset.capacitorPlatform = Capacitor.getPlatform();
  document.documentElement.dataset.capacitorNative = String(Capacitor.isNativePlatform());
  document.documentElement.dataset.appActive = 'true';
  document.documentElement.dataset.windowFocused = 'true';
  document.documentElement.dataset.windowVisible = 'true';
  document.documentElement.dataset.windowMinimized = 'false';
  document.documentElement.dataset.windowMaximized = 'false';
  document.documentElement.dataset.windowFullscreen = 'false';
  document.documentElement.style.colorScheme = getPreferredColorScheme();

  const cleanup: Array<() => Promise<void> | void> = [];

  const resizeHandler = () => scheduleViewportMetrics();
  window.addEventListener('resize', resizeHandler);
  window.addEventListener('orientationchange', resizeHandler);
  cleanup.push(() => window.removeEventListener('resize', resizeHandler));
  cleanup.push(() => window.removeEventListener('orientationchange', resizeHandler));

  if (window.visualViewport) {
    const visualViewportResize = () => scheduleViewportMetrics();
    window.visualViewport.addEventListener('resize', visualViewportResize);
    window.visualViewport.addEventListener('scroll', visualViewportResize);
    cleanup.push(() => window.visualViewport?.removeEventListener('resize', visualViewportResize));
    cleanup.push(() => window.visualViewport?.removeEventListener('scroll', visualViewportResize));
  }

  const colorSchemeQuery = window.matchMedia('(prefers-color-scheme: dark)');
  const colorSchemeListener = () => {
    document.documentElement.dataset.colorScheme = colorSchemeQuery.matches ? 'dark' : 'light';
    document.documentElement.style.colorScheme = colorSchemeQuery.matches ? 'dark' : 'light';
    void syncNativeChrome();
  };
  colorSchemeQuery.addEventListener('change', colorSchemeListener);
  cleanup.push(() => colorSchemeQuery.removeEventListener('change', colorSchemeListener));

  try {
    const { Keyboard } = await loadKeyboardModule();
    const keyboardWillShow = await Keyboard.addListener('keyboardWillShow', (event) => {
      document.documentElement.dataset.keyboardVisible = 'true';
      document.documentElement.style.setProperty('--app-keyboard-offset', `${event.keyboardHeight}px`);
      scheduleViewportMetrics();
    });

    const keyboardWillHide = await Keyboard.addListener('keyboardWillHide', () => {
      document.documentElement.dataset.keyboardVisible = 'false';
      document.documentElement.style.setProperty('--app-keyboard-offset', '0px');
      scheduleViewportMetrics();
    });

    cleanup.push(() => keyboardWillShow.remove());
    cleanup.push(() => keyboardWillHide.remove());
  } catch {
    // Keyboard plugin is optional on web and some test environments.
  }

  try {
    await Promise.allSettled([
      syncNativeChrome(),
      loadSplashScreenModule().then(({ SplashScreen }) => SplashScreen.hide()),
    ]);
  } catch {
    // Ignore shell setup failures in unsupported environments.
  }

  try {
    const { Network } = await loadNetworkModule();
    const networkStatus = await Network.getStatus();
    syncOnlineState(networkStatus.connected, handlers.onNetworkChange);

    const networkListener = await Network.addListener('networkStatusChange', (status) => {
      syncOnlineState(status.connected, handlers.onNetworkChange);
    });

    cleanup.push(() => networkListener.remove());
  } catch {
    const onlineHandler = () => syncOnlineState(true, handlers.onNetworkChange);
    const offlineHandler = () => syncOnlineState(false, handlers.onNetworkChange);

    window.addEventListener('online', onlineHandler);
    window.addEventListener('offline', offlineHandler);
    cleanup.push(() => window.removeEventListener('online', onlineHandler));
    cleanup.push(() => window.removeEventListener('offline', offlineHandler));
  }

  const currentAppModulePromise = loadAppModule();

  try {
    const { App } = await currentAppModulePromise;
    const appStateListener = await App.addListener('appStateChange', (state) => {
      document.documentElement.dataset.appActive = state.isActive ? 'true' : 'false';
      document.documentElement.dataset.windowFocused = state.isActive ? 'true' : 'false';
      document.documentElement.dataset.windowVisible = state.isActive ? 'true' : 'false';
      document.documentElement.dataset.windowMinimized = state.isActive ? 'false' : 'true';
      if (state.isActive) {
        scheduleViewportMetrics();
        void syncNativeChrome();
        handlers.onResume?.();
      } else {
        handlers.onPause?.();
      }
    });

    cleanup.push(() => appStateListener.remove());
  } catch {
    // If the plugin is unavailable we only rely on browser focus state.
  }

  try {
    const { App } = await currentAppModulePromise;
    const backButtonListener = await App.addListener('backButton', async ({ canGoBack }) => {
      if (canGoBack && window.history.length > 1) {
        window.history.back();
        return;
      }

      handlers.onBackButton?.();
      await App.exitApp();
    });

    cleanup.push(() => backButtonListener.remove());
  } catch {
    // Android-only back button handling is optional.
  }

  return () => {
    cleanup.forEach((teardown) => {
      try {
        void teardown();
      } catch {
        // Ignore teardown failures.
      }
    });
  };
}