'use client';

import { useEffect } from 'react';
import { getAppRuntimeKind } from '@/lib/runtime-signals';

type RuntimeStateSnapshot = {
  focused: boolean;
  visible: boolean;
  minimized: boolean;
  maximized: boolean;
  fullScreen: boolean;
};

function isBrowser() {
  return typeof window !== 'undefined' && typeof document !== 'undefined';
}

function setBooleanDataAttribute(name: string, value: boolean) {
  document.documentElement.dataset[name] = value ? 'true' : 'false';
}

function writeRuntimeState(snapshot: RuntimeStateSnapshot, includeAppActive: boolean) {
  setBooleanDataAttribute('windowFocused', snapshot.focused);
  setBooleanDataAttribute('windowVisible', snapshot.visible);
  setBooleanDataAttribute('windowMinimized', snapshot.minimized);
  setBooleanDataAttribute('windowMaximized', snapshot.maximized);
  setBooleanDataAttribute('windowFullscreen', snapshot.fullScreen);

  if (includeAppActive) {
    setBooleanDataAttribute('appActive', snapshot.visible && snapshot.focused && !snapshot.minimized);
  }
}

function readDocumentSnapshot(): RuntimeStateSnapshot {
  return {
    focused: document.hasFocus(),
    visible: document.visibilityState !== 'hidden',
    minimized: document.visibilityState === 'hidden',
    maximized: false,
    fullScreen: document.fullscreenElement !== null,
  };
}

export function RuntimeLifecycleBridge() {
  useEffect(() => {
    if (!isBrowser()) {
      return;
    }

    const runtimeKind = getAppRuntimeKind();
    const includeAppActive = runtimeKind !== 'capacitor-native';
    let latestSnapshot = '';

    const syncState = (snapshot: RuntimeStateSnapshot) => {
      const nextState = {
        focused: snapshot.focused,
        visible: snapshot.visible,
        minimized: snapshot.minimized,
        maximized: snapshot.maximized,
        fullScreen: snapshot.fullScreen,
      };

      const nextSignature = JSON.stringify(nextState);
      if (nextSignature === latestSnapshot) {
        return;
      }

      latestSnapshot = nextSignature;
      writeRuntimeState(nextState, includeAppActive);
    };

    const syncFromDocument = () => {
      syncState(readDocumentSnapshot());
    };

    syncFromDocument();

    const handleVisibilityChange = () => {
      syncFromDocument();
    };

    const handleFocus = () => {
      syncFromDocument();
    };

    const handleBlur = () => {
      syncFromDocument();
    };

    const handleFullscreenChange = () => {
      syncFromDocument();
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    document.addEventListener('fullscreenchange', handleFullscreenChange);
    window.addEventListener('focus', handleFocus);
    window.addEventListener('blur', handleBlur);
    window.addEventListener('pageshow', handleVisibilityChange);
    window.addEventListener('pagehide', handleVisibilityChange);

    let removeDesktopListener: (() => void) | undefined;

    if (runtimeKind === 'desktop' && window.desktopBridge?.runtime) {
      const runtimeInfoPromise = window.desktopBridge.runtime.info?.();

      if (runtimeInfoPromise) {
        void runtimeInfoPromise.then((runtimeInfo) => {
          if (runtimeInfo?.windowState) {
            syncState({
              focused: runtimeInfo.windowState.isFocused,
              visible: runtimeInfo.windowState.isVisible,
              minimized: runtimeInfo.windowState.isMinimized,
              maximized: runtimeInfo.windowState.isMaximized,
              fullScreen: runtimeInfo.windowState.isFullScreen,
            });
          }
        }).catch(() => {
          // If the main process is still starting, the document snapshot remains authoritative.
        });
      }

      if (window.desktopBridge.runtime.onWindowStateChange) {
        removeDesktopListener = window.desktopBridge.runtime.onWindowStateChange((windowState) => {
          syncState({
            focused: windowState.isFocused,
            visible: windowState.isVisible,
            minimized: windowState.isMinimized,
            maximized: windowState.isMaximized,
            fullScreen: windowState.isFullScreen,
          });
        });
      }
    }

    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
      document.removeEventListener('fullscreenchange', handleFullscreenChange);
      window.removeEventListener('focus', handleFocus);
      window.removeEventListener('blur', handleBlur);
      window.removeEventListener('pageshow', handleVisibilityChange);
      window.removeEventListener('pagehide', handleVisibilityChange);
      removeDesktopListener?.();
    };
  }, []);

  return null;
}