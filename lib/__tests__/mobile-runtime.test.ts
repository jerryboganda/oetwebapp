const mobileMocks = vi.hoisted(() => {
  const createHandle = () => ({ remove: vi.fn() });
  const handles = {
    appState: createHandle(),
    backButton: createHandle(),
    keyboardWillShow: createHandle(),
    keyboardDidShow: createHandle(),
    keyboardWillHide: createHandle(),
    keyboardDidHide: createHandle(),
    network: createHandle(),
  };

  return {
    native: false,
    // Controllable so the safe-area tests can pin both the Android fallback and
    // the iOS/web "leave env() alone" branch through initializeMobileRuntime().
    platform: 'android' as 'android' | 'ios' | 'web',
    factories: {
      app: vi.fn(),
      keyboard: vi.fn(),
      network: vi.fn(),
      splashScreen: vi.fn(),
      statusBar: vi.fn(),
    },
    app: {
      addListener: vi.fn(async (eventName: string) =>
        eventName === 'appStateChange' ? handles.appState : handles.backButton,
      ),
      exitApp: vi.fn(async () => undefined),
    },
    keyboard: {
      addListener: vi.fn(async (eventName: string) => {
        if (eventName === 'keyboardWillShow') return handles.keyboardWillShow;
        if (eventName === 'keyboardDidShow') return handles.keyboardDidShow;
        if (eventName === 'keyboardWillHide') return handles.keyboardWillHide;
        return handles.keyboardDidHide;
      }),
    },
    network: {
      getStatus: vi.fn(async () => ({ connected: true, connectionType: 'wifi' as const })),
      addListener: vi.fn(async () => handles.network),
    },
    splashScreen: {
      hide: vi.fn(async () => undefined),
    },
    statusBar: {
      setOverlaysWebView: vi.fn(async () => undefined),
      setStyle: vi.fn(async () => undefined),
      setBackgroundColor: vi.fn(async () => undefined),
    },
    handles,
  };
});

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: () => mobileMocks.native,
    getPlatform: () => mobileMocks.platform,
  },
}));

vi.mock('@capacitor/app', () => {
  mobileMocks.factories.app();
  return { App: mobileMocks.app };
});

vi.mock('@capacitor/keyboard', () => {
  mobileMocks.factories.keyboard();
  return { Keyboard: mobileMocks.keyboard };
});

vi.mock('@capacitor/network', () => {
  mobileMocks.factories.network();
  return { Network: mobileMocks.network };
});

vi.mock('@capacitor/splash-screen', () => {
  mobileMocks.factories.splashScreen();
  return { SplashScreen: mobileMocks.splashScreen };
});

vi.mock('@capacitor/status-bar', () => {
  mobileMocks.factories.statusBar();
  return {
    StatusBar: mobileMocks.statusBar,
    Style: { Dark: 'DARK', Light: 'LIGHT' },
  };
});

import { initializeMobileRuntime } from '@/lib/mobile/runtime';

describe('mobile runtime', () => {
  beforeEach(() => {
    mobileMocks.native = false;
    mobileMocks.platform = 'android';
    vi.clearAllMocks();
  });

  afterEach(() => {
    delete window.desktopBridge;
    delete document.documentElement.dataset.runtimeKind;
    delete document.documentElement.dataset.mobileRuntimeActive;
    delete document.documentElement.dataset.desktopNative;
    delete document.documentElement.dataset.capacitorNative;
    delete document.documentElement.dataset.capacitorPlatform;
    delete document.documentElement.dataset.appActive;
    delete document.documentElement.dataset.windowFocused;
    delete document.documentElement.dataset.windowVisible;
    delete document.documentElement.dataset.windowMinimized;
    delete document.documentElement.dataset.windowMaximized;
    delete document.documentElement.dataset.windowFullscreen;
    delete document.documentElement.dataset.colorScheme;
    delete document.documentElement.dataset.networkConnected;
    delete document.documentElement.dataset.keyboardVisible;
    document.documentElement.style.removeProperty('--app-viewport-height');
    document.documentElement.style.removeProperty('--app-keyboard-offset');
    document.documentElement.style.removeProperty('--safe-area-inset-top');
    document.documentElement.style.removeProperty('--safe-area-inset-right');
    document.documentElement.style.removeProperty('--safe-area-inset-bottom');
    document.documentElement.style.removeProperty('--safe-area-inset-left');
    document.documentElement.style.removeProperty('color-scheme');
    delete (window as unknown as { __oetSafeAreaInsets?: unknown }).__oetSafeAreaInsets;
  });

  it('does not overwrite desktop runtime signals', async () => {
    window.desktopBridge = {
      platform: 'win32',
      versions: {
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
      speakingAudio: {
        start: async (sessionId: string) => ({ ok: true, sessionId, mimeType: 'audio/webm', mode: 'ipc' }),
        stop: async (sessionId: string) => ({ ok: true, sessionId }),
        getBlob: async (sessionId: string) => ({ ok: true, sessionId }),
        discard: async (sessionId: string) => ({ ok: true, sessionId }),
        getPlatform: () => 'win32' as NodeJS.Platform,
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
      offlineCache: {
        store: async (key) => ({ success: true, key }),
        get: async () => null,
        delete: async (key) => ({ success: true, key }),
        list: async () => [],
        clear: async () => ({ success: true, cleared: 0 }),
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

  it('does not stamp capacitor-native on a plain web browser', async () => {
    // No desktopBridge and Capacitor.isNativePlatform() === false in jsdom, so
    // the web app must NOT be mislabelled as a native shell (regression: the
    // update toolbar was appearing on the website).
    const cleanup = await initializeMobileRuntime();

    expect(document.documentElement.dataset.runtimeKind).toBeUndefined();
    expect(document.documentElement.dataset.capacitorNative).toBeUndefined();
    expect(document.documentElement.dataset.appActive).toBeUndefined();
    expect(mobileMocks.factories.app).not.toHaveBeenCalled();
    expect(mobileMocks.factories.keyboard).not.toHaveBeenCalled();
    expect(mobileMocks.factories.network).not.toHaveBeenCalled();
    expect(mobileMocks.factories.splashScreen).not.toHaveBeenCalled();
    expect(mobileMocks.factories.statusBar).not.toHaveBeenCalled();
    expect(mobileMocks.app.addListener).not.toHaveBeenCalled();
    expect(mobileMocks.keyboard.addListener).not.toHaveBeenCalled();
    expect(mobileMocks.network.getStatus).not.toHaveBeenCalled();
    expect(mobileMocks.splashScreen.hide).not.toHaveBeenCalled();
    expect(mobileMocks.statusBar.setStyle).not.toHaveBeenCalled();

    cleanup();
  });

  it('initializes native plugins once and removes registered listeners during cleanup', async () => {
    mobileMocks.native = true;

    const cleanup = await initializeMobileRuntime();

    expect(mobileMocks.factories.app).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.keyboard).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.network).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.splashScreen).toHaveBeenCalledTimes(1);
    expect(mobileMocks.factories.statusBar).toHaveBeenCalledTimes(1);
    expect(mobileMocks.keyboard.addListener).toHaveBeenCalledTimes(4);
    expect(mobileMocks.network.getStatus).toHaveBeenCalledTimes(1);
    expect(mobileMocks.network.addListener).toHaveBeenCalledTimes(1);
    expect(mobileMocks.app.addListener).toHaveBeenCalledTimes(2);
    expect(mobileMocks.splashScreen.hide).toHaveBeenCalledTimes(1);
    expect(mobileMocks.statusBar.setStyle).toHaveBeenCalledTimes(1);
    expect(document.documentElement.dataset.runtimeKind).toBe('capacitor-native');
    // Distinct marker proving initializeMobileRuntime itself ran (the layout
    // bootstrap script stamps runtimeKind independently of this function).
    expect(document.documentElement.dataset.mobileRuntimeActive).toBe('true');
    expect(document.documentElement.dataset.capacitorPlatform).toBe('android');

    cleanup();

    expect(mobileMocks.handles.keyboardWillShow.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.keyboardDidShow.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.keyboardWillHide.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.keyboardDidHide.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.network.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.appState.remove).toHaveBeenCalledTimes(1);
    expect(mobileMocks.handles.backButton.remove).toHaveBeenCalledTimes(1);
  });

  it('re-applies the native safe-area insets on init and again on resume', async () => {
    // Regression: on a cold launch the native bridge writes the insets onto the
    // WebView's initial document, which the remote page then replaces — so the
    // header sat under the status bar until the next inset change (i.e. until
    // the app was backgrounded and reopened). The runtime must re-apply the
    // mirrored values itself.
    mobileMocks.native = true;
    (window as unknown as { __oetSafeAreaInsets?: unknown }).__oetSafeAreaInsets = {
      top: 24,
      right: 0,
      bottom: 48,
      left: 0,
    };

    const cleanup = await initializeMobileRuntime();
    const root = document.documentElement.style;
    expect(root.getPropertyValue('--safe-area-inset-top')).toBe('24px');
    expect(root.getPropertyValue('--safe-area-inset-right')).toBe('0px');
    expect(root.getPropertyValue('--safe-area-inset-bottom')).toBe('48px');
    expect(root.getPropertyValue('--safe-area-inset-left')).toBe('0px');

    // Simulate the lost document, then a resume onto the live one.
    root.removeProperty('--safe-area-inset-top');
    const appStateCall = mobileMocks.app.addListener.mock.calls.find(
      (call) => call[0] === 'appStateChange',
    ) as unknown as [string, (state: { isActive: boolean }) => Promise<void> | void] | undefined;
    expect(appStateCall).toBeDefined();
    await appStateCall![1]({ isActive: true });

    expect(root.getPropertyValue('--safe-area-inset-top')).toBe('24px');

    cleanup();
  });

  // Android's WebView never resolves env(safe-area-inset-*), so with no native
  // push the runtime must install ANDROID_FALLBACK_SAFE_AREA_INSETS instead of
  // leaving the header flush under the status bar (the 10 Sep 2026 cold-launch
  // report). lib/mobile/__tests__/runtime-safe-area.test.ts pins
  // setSafeAreaInsets() itself; this pins that initializeMobileRuntime()
  // actually reaches it — the wiring that file cannot see.
  it('applies the Android safe-area placeholder when no native values were pushed', async () => {
    mobileMocks.native = true;
    mobileMocks.platform = 'android';

    const cleanup = await initializeMobileRuntime();

    expect(document.documentElement.style.getPropertyValue('--safe-area-inset-top')).toBe('24px');
    expect(document.documentElement.style.getPropertyValue('--safe-area-inset-bottom')).toBe('16px');

    cleanup();
  });

  it('writes no safe-area insets on iOS/web, where env() already resolves', async () => {
    mobileMocks.native = true;
    mobileMocks.platform = 'ios';

    const cleanup = await initializeMobileRuntime();

    expect(document.documentElement.style.getPropertyValue('--safe-area-inset-top')).toBe('');
    expect(document.documentElement.style.getPropertyValue('--safe-area-inset-bottom')).toBe('');

    cleanup();
  });

  // Issue report 11 Sep 2026 — the bottom nav is hidden purely by this flag, so
  // it has to be correct in both directions and recover on its own.
  describe('keyboard visibility flag', () => {
    const originalVisualViewport = Object.getOwnPropertyDescriptor(window, 'visualViewport');
    const flushFrame = () => new Promise((resolve) => { requestAnimationFrame(() => resolve(null)); });
    // The 14 Sep 2026 IME slide-down latch keeps the nav hidden ~250ms after a
    // plugin hide event (the keypad is still visually closing); reveal
    // assertions poll past the latch instead of guessing the timer jitter.
    const awaitReveal = async () => {
      await flushFrame();
      for (let i = 0; i < 60; i++) {
        if (document.documentElement.dataset.keyboardVisible === 'false') return;
        await new Promise((resolve) => { setTimeout(resolve, 25); });
      }
    };

    const setViewportHeight = (height: number) => {
      Object.defineProperty(window, 'visualViewport', {
        configurable: true,
        value: { height, addEventListener: vi.fn(), removeEventListener: vi.fn() },
      });
    };

    const listenerFor = (eventName: string) => {
      const call = mobileMocks.keyboard.addListener.mock.calls.find(
        (entry) => entry[0] === eventName,
      ) as unknown as [string, () => void] | undefined;
      expect(call).toBeDefined();
      return call![1];
    };

    afterEach(() => {
      if (originalVisualViewport) Object.defineProperty(window, 'visualViewport', originalVisualViewport);
      else delete (window as unknown as { visualViewport?: unknown }).visualViewport;
    });

    it('sets the flag on show and clears it on hide, for both the will* and did* forms', async () => {
      mobileMocks.native = true;
      const cleanup = await initializeMobileRuntime();

      // Android may deliver only one of each pair, so either must work alone.
      setViewportHeight(window.innerHeight - 320);
      listenerFor('keyboardWillShow')();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      setViewportHeight(window.innerHeight);
      listenerFor('keyboardDidHide')();
      // The reveal latch holds the flag through the visual IME slide-down…
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');
      await awaitReveal();
      expect(document.documentElement.dataset.keyboardVisible).toBe('false');

      mobileMocks.handles.keyboardWillShow.remove.mockClear();
      setViewportHeight(window.innerHeight - 320);
      listenerFor('keyboardDidShow')();
      await flushFrame();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      setViewportHeight(window.innerHeight);
      listenerFor('keyboardWillHide')();
      await awaitReveal();
      expect(document.documentElement.dataset.keyboardVisible).toBe('false');

      cleanup();
    });

    it('self-heals from viewport metrics when the keyboard events never arrive', async () => {
      mobileMocks.native = true;
      const cleanup = await initializeMobileRuntime();

      // Regression: a keyboardWillHide that is never delivered used to leave the
      // nav hidden for the rest of the session (the "stale offset" symptom).
      setViewportHeight(window.innerHeight - 320);
      window.dispatchEvent(new Event('resize'));
      await flushFrame();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      setViewportHeight(window.innerHeight);
      window.dispatchEvent(new Event('resize'));
      await flushFrame();
      expect(document.documentElement.dataset.keyboardVisible).toBe('false');

      cleanup();
    });

    it('ignores small viewport shortfalls so scroll noise cannot hide the nav', async () => {
      mobileMocks.native = true;
      const cleanup = await initializeMobileRuntime();

      setViewportHeight(window.innerHeight - 40);
      window.dispatchEvent(new Event('resize'));
      await flushFrame();
      expect(document.documentElement.dataset.keyboardVisible).toBe('false');

      cleanup();
    });

    // Report 13 Sep 2026 — the surviving Android Recalls > Practice Spelling
    // defect. On the Android shell the Keyboard plugin's resizeOnFullScreen
    // path resizes the WebView ITSELF when the IME opens: innerHeight and
    // visualViewport.height shrink together, the offset test reads 0, and every
    // resize-triggered metrics pass re-derived "no keyboard" — un-hiding the
    // bottom nav so it floated above the keyboard over the spelling input. The
    // baseline-shrinkage test must keep the nav hidden in that mode.
    describe('native WebView resize (Android IME)', () => {
      const originalInnerHeight = Object.getOwnPropertyDescriptor(window, 'innerHeight');

      const setWindowHeight = (height: number) => {
        Object.defineProperty(window, 'innerHeight', { configurable: true, value: height });
      };

      const focusTextEntry = () => {
        const input = document.createElement('input');
        document.body.appendChild(input);
        input.focus();
        return input;
      };

      afterEach(() => {
        if (originalInnerHeight) {
          Object.defineProperty(window, 'innerHeight', originalInnerHeight);
        } else {
          // jsdom defines innerHeight; fall back to the standard 1024 if not.
          setWindowHeight(1024);
        }
        document.body.innerHTML = '';
      });

      it('keeps the nav hidden when the WebView shrinks under the keyboard while an input has focus', async () => {
        mobileMocks.native = true;
        const cleanup = await initializeMobileRuntime();
        const input = focusTextEntry();
        const fullHeight = window.innerHeight;

        // Keyboard opens: the plugin resizes the WebView, so BOTH heights drop
        // by the keyboard height and the offset between them is 0.
        setWindowHeight(fullHeight - 320);
        setViewportHeight(fullHeight - 320);
        window.dispatchEvent(new Event('resize'));
        await flushFrame();
        expect(document.documentElement.dataset.keyboardVisible).toBe('true');

        // Keyboard closes: the WebView is restored to the full height.
        setWindowHeight(fullHeight);
        setViewportHeight(fullHeight);
        window.dispatchEvent(new Event('resize'));
        await flushFrame();
        expect(document.documentElement.dataset.keyboardVisible).toBe('false');

        input.remove();
        cleanup();
      });

      it('does not treat a non-keyboard viewport shrink without a focused input as a keyboard', async () => {
        mobileMocks.native = true;
        const cleanup = await initializeMobileRuntime();
        const fullHeight = window.innerHeight;

        // Split-screen / window resize with no text entry focused: the nav must
        // stay visible even though the viewport shrank beyond the threshold.
        setWindowHeight(fullHeight - 320);
        setViewportHeight(fullHeight - 320);
        window.dispatchEvent(new Event('resize'));
        await flushFrame();
        expect(document.documentElement.dataset.keyboardVisible).toBe('false');

        cleanup();
      });

    it('re-baselines on rotation so the shorter landscape height is not read as a keyboard', async () => {
        mobileMocks.native = true;
        const cleanup = await initializeMobileRuntime();
        focusTextEntry();

        // Rotate portrait (tall) -> landscape (short) while the keyboard is
        // closed. Without the orientation baseline reset the landscape height
        // would read as a >120px keyboard shortfall and hide the nav forever.
        window.dispatchEvent(new Event('orientationchange'));
        setWindowHeight(400);
        setViewportHeight(400);
        window.dispatchEvent(new Event('resize'));
        await flushFrame();
        expect(document.documentElement.dataset.keyboardVisible).toBe('false');

        cleanup();
      });
    });

    // Second half of the 13 Sep 2026 Practice Spelling device report. Under
    // adjustPan (the Android default with edge-to-edge) the window pans up and
    // NO viewport metric changes while the IME opens — the metrics re-derivation
    // kept resurrecting the bottom nav mid-screen over the spelling input while
    // mobile web was fine. The plugin's keyboard state is authoritative: metrics
    // may raise the flag but can never clear it while the plugin says the IME
    // is open.
    it('keeps the nav hidden through resize churn while the plugin reports the keyboard (adjustPan)', async () => {
      mobileMocks.native = true;
      const cleanup = await initializeMobileRuntime();

      // Pan mode: heights never change, only the plugin event signals the IME.
      setViewportHeight(window.innerHeight);
      listenerFor('keyboardWillShow')();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      // The pan still produces resize/visualViewport churn after the show
      // event — the re-derivation must not resurrect the nav.
      window.dispatchEvent(new Event('resize'));
      await flushFrame();
      window.dispatchEvent(new Event('resize'));
      await flushFrame();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      // Keyboard closes: the plugin hide event is trusted, metrics agree — but
      // only after the slide-down latch expires (nav must not flash over the
      // closing keypad).
      listenerFor('keyboardDidHide')();
      await flushFrame();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');
      await awaitReveal();
      expect(document.documentElement.dataset.keyboardVisible).toBe('false');

      cleanup();
    });

    // Device report 14 Sep 2026: tapping Check moved focus off the input, the
    // plugin fired keyboardWillHide/DidHide and the viewport snapped back to
    // full height while the keypad was still visually sliding away — the nav
    // revealed at the restored bottom, i.e. exactly where the keypad still
    // was. The reveal latch must hold the nav hidden through that window and
    // an immediate re-show must cancel it.
    it('holds the nav through the slide-down latch after a hide, and a re-show cancels the latch', async () => {
      mobileMocks.native = true;
      const cleanup = await initializeMobileRuntime();

      setViewportHeight(window.innerHeight - 320);
      listenerFor('keyboardWillShow')();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      listenerFor('keyboardWillHide')();
      // The device restores the WebView height as the IME starts closing.
      setViewportHeight(window.innerHeight);
      await flushFrame();
      // Still inside the 250ms latch: no mid-screen flash over the keypad.
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      // User re-focuses / keyboard re-opens: the latch must not delay opening.
      setViewportHeight(window.innerHeight - 320);
      listenerFor('keyboardDidShow')();
      expect(document.documentElement.dataset.keyboardVisible).toBe('true');

      listenerFor('keyboardDidHide')();
      setViewportHeight(window.innerHeight);
      await awaitReveal();
      expect(document.documentElement.dataset.keyboardVisible).toBe('false');

      cleanup();
    });

    // Third round of the 13 Sep 2026 Practice Spelling device report. The
    // reporting device fires NO plugin keyboard events and, under pan mode,
    // NO viewport metric ever changes — both shipped rounds kept re-deriving
    // "no keyboard" and the bottom nav floated over the spelling input. DOM
    // focus is the only signal guaranteed in that environment, so focus alone
    // must hide the nav and blur alone must bring it back.
    describe('text-entry focus fallback (no plugin events, no viewport change)', () => {
      const flushFrame = () => new Promise((resolve) => { requestAnimationFrame(() => resolve(null)); });

      it('hides the nav on focus alone and restores it on blur', async () => {
        mobileMocks.native = true;
        const cleanup = await initializeMobileRuntime();

        const input = document.createElement('input');
        document.body.appendChild(input);
        input.focus();
        expect(document.documentElement.dataset.keyboardVisible).toBe('true');

        // Resize churn must not resurrect the nav while the field is focused.
        window.dispatchEvent(new Event('resize'));
        await flushFrame();
        expect(document.documentElement.dataset.keyboardVisible).toBe('true');

        // Tapping elsewhere moves focus off the entry; the nav returns.
        const button = document.createElement('button');
        document.body.appendChild(button);
        button.focus();
        await flushFrame();
        expect(document.documentElement.dataset.keyboardVisible).toBe('false');

        input.remove();
        button.remove();
        cleanup();
      });

      it('keeps the nav hidden when focus hops between text fields', async () => {
        mobileMocks.native = true;
        const cleanup = await initializeMobileRuntime();

        const first = document.createElement('input');
        const second = document.createElement('textarea');
        document.body.append(first, second);
        first.focus();
        expect(document.documentElement.dataset.keyboardVisible).toBe('true');

        second.focus();
        await flushFrame();
        await flushFrame();
        expect(document.documentElement.dataset.keyboardVisible).toBe('true');

        first.remove();
        second.remove();
        cleanup();
      });

      it('returns the nav after the slide-down latch when the plugin reports a hide even if the field kept focus', async () => {
        mobileMocks.native = true;
        const cleanup = await initializeMobileRuntime();

        const input = document.createElement('input');
        document.body.appendChild(input);
        input.focus();
        expect(document.documentElement.dataset.keyboardVisible).toBe('true');

        // Back-button IME dismiss keeps DOM focus; the plugin's report of the
        // close is hard evidence and must release the hold once the latch
        // (which hides the keypad's visual slide-down) has expired.
        listenerFor('keyboardDidHide')();
        await awaitReveal();
        expect(document.documentElement.dataset.keyboardVisible).toBe('false');

        input.remove();
        cleanup();
      });
    });
  });
});