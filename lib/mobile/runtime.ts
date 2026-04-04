'use client';

import { Capacitor } from '@capacitor/core';
import { App } from '@capacitor/app';
import { Browser } from '@capacitor/browser';
import { Device } from '@capacitor/device';
import { Keyboard } from '@capacitor/keyboard';
import { Network } from '@capacitor/network';
import { SplashScreen } from '@capacitor/splash-screen';
import { StatusBar, Style } from '@capacitor/status-bar';

export interface MobileRuntimeHandlers {
  onResume?: () => void;
  onPause?: () => void;
  onNetworkChange?: (connected: boolean) => void;
  onBackButton?: () => void;
}

export interface MobileRuntimeSnapshot {
  platform: string;
  isNative: boolean;
  isAndroid: boolean;
  isIOS: boolean;
  isWeb: boolean;
}

function isBrowser() {
  return typeof window !== 'undefined';
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

function syncOnlineState(connected: boolean, onNetworkChange?: (connected: boolean) => void) {
  if (!isBrowser()) {
    return;
  }

  document.documentElement.dataset.networkConnected = connected ? 'true' : 'false';
  window.dispatchEvent(new Event(connected ? 'online' : 'offline'));
  onNetworkChange?.(connected);
}

export async function getMobileRuntimeSnapshot(): Promise<MobileRuntimeSnapshot> {
  const platform = Capacitor.getPlatform();
  const isNative = Capacitor.isNativePlatform();
  const device = isNative ? await Device.getInfo().catch(() => null) : null;

  return {
    platform: device?.platform ?? platform,
    isNative,
    isAndroid: (device?.platform ?? platform) === 'android',
    isIOS: (device?.platform ?? platform) === 'ios',
    isWeb: !isNative,
  };
}

export async function openExternalUrl(url: string): Promise<void> {
  if (!isBrowser()) {
    return;
  }

  if (Capacitor.isNativePlatform()) {
    await Browser.open({ url });
    return;
  }

  window.open(url, '_blank', 'noopener,noreferrer');
}

export async function initializeMobileRuntime(handlers: MobileRuntimeHandlers = {}): Promise<() => void> {
  if (!isBrowser()) {
    return () => undefined;
  }

  setViewportMetrics();
  document.documentElement.dataset.capacitorPlatform = Capacitor.getPlatform();
  document.documentElement.dataset.capacitorNative = String(Capacitor.isNativePlatform());

  const cleanup: Array<() => Promise<void> | void> = [];

  const resizeHandler = () => setViewportMetrics();
  window.addEventListener('resize', resizeHandler);
  window.addEventListener('orientationchange', resizeHandler);
  cleanup.push(() => window.removeEventListener('resize', resizeHandler));
  cleanup.push(() => window.removeEventListener('orientationchange', resizeHandler));

  try {
    const keyboardWillShow = await Keyboard.addListener('keyboardWillShow', (event) => {
      document.documentElement.style.setProperty('--app-keyboard-offset', `${event.keyboardHeight}px`);
      setViewportMetrics();
    });

    const keyboardWillHide = await Keyboard.addListener('keyboardWillHide', () => {
      document.documentElement.style.setProperty('--app-keyboard-offset', '0px');
      setViewportMetrics();
    });

    cleanup.push(() => keyboardWillShow.remove());
    cleanup.push(() => keyboardWillHide.remove());
  } catch {
    // Keyboard plugin is optional on web and some test environments.
  }

  try {
    await Promise.allSettled([
      StatusBar.setOverlaysWebView({ overlay: false }),
      StatusBar.setStyle({ style: Style.Dark }),
      SplashScreen.hide(),
    ]);
  } catch {
    // Ignore shell setup failures in unsupported environments.
  }

  try {
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

  try {
    const appStateListener = await App.addListener('appStateChange', (state) => {
      document.documentElement.dataset.appActive = state.isActive ? 'true' : 'false';
      if (state.isActive) {
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