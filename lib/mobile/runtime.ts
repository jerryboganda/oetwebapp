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

/**
 * Marks whether the soft keyboard is covering the viewport. The bottom nav is
 * hidden while this is true, so a missed plugin event would leave the learner
 * with no navigation at all — which is why `setViewportMetrics` re-derives it.
 */
function setKeyboardVisible(visible: boolean) {
  if (!isBrowser()) {
    return;
  }

  document.documentElement.dataset.keyboardVisible = visible ? 'true' : 'false';
}

/**
 * A viewport shortfall this large can only be the soft keyboard. Smaller
 * differences are URL-bar and scroll noise and must not hide the navigation.
 */
const KEYBOARD_VISIBLE_THRESHOLD_PX = 120;

/**
 * Tallest viewport height observed without a keyboard, and the last keyboard
 * state the Capacitor Keyboard plugin reported. Native IME behavior differs by
 * mode and ALL must read as "keyboard open":
 *  - adjustPan (Android default under edge-to-edge): the window pans, NO
 *    viewport metric changes — only the plugin state can see the keyboard.
 *  - native WebView resize (Keyboard plugin resizeOnFullScreen path):
 *    innerHeight and visualViewport.height shrink TOGETHER, so the offset
 *    reads 0 — the baseline-shrinkage comparison catches it.
 *  - overlay modes (iOS body resize, mobile browsers): innerHeight stays full
 *    while visualViewport.height shrinks — the offset test catches it.
 * The plugin state outranks the metrics tests (see setViewportMetrics).
 */
let keyboardFreeBaselineHeight = 0;
let pluginKeyboardVisible = false;

/**
 * The DOM focus fallback, third round of the 13 Sep 2026 Practice Spelling
 * defect. Both prior signals can be silently absent on the Android shell: the
 * plugin's show/hide events only fire from WindowInsetsAnimation callbacks
 * (OEM-dependent — they never fired on the reporting device), and under
 * adjustPan/adjustUnspecified with edge-to-edge the window PANS, so no viewport
 * metric ever changes. `focusin`/`focusout` on a text entry are the only events
 * guaranteed to exist in every WebView and every soft-input mode, so while a
 * text entry holds focus we treat the IME as open unless hard evidence says
 * otherwise. Evidence of open: the viewport shrinks past the threshold while
 * focused (resize modes). Evidence of closed: focus leaves the entry, the
 * plugin reports a hide, or the viewport returns to the keyboard-free baseline
 * after having shrunk (resize-mode back-button IME dismiss keeps DOM focus).
 * `orientationchange` drops the whole conclusion for the same reason it resets
 * the baseline — rotation invalidates every height comparison at once.
 */
let textEntryKeyboardVisible = false;
let viewportShrankWhileTextEntryFocused = false;

function clearTextEntryKeyboardEvidence() {
  textEntryKeyboardVisible = false;
  viewportShrankWhileTextEntryFocused = false;
}

function resetKeyboardBaseline() {
  keyboardFreeBaselineHeight = 0;
  clearTextEntryKeyboardEvidence();
}

function isTextEntryElement(target: EventTarget | null): target is HTMLElement {
  if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) return true;
  return target instanceof HTMLElement && target.isContentEditable === true;
}

function isTextEntryFocused(): boolean {
  return isTextEntryElement(document.activeElement);
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

  if (viewportHeight > keyboardFreeBaselineHeight) {
    keyboardFreeBaselineHeight = viewportHeight;
  }
  // The plugin's keyboard state is AUTHORITATIVE on native shells. Under
  // adjustPan (the Android default with edge-to-edge) NOTHING changes in the
  // viewport metrics while the IME opens — innerHeight, visualViewport.height
  // and the baseline all stay identical and the window merely pans up — so
  // only the plugin can see the keyboard there. This was the second half of
  // the 13 Sep 2026 Practice Spelling defect: the plugin flag was folded into
  // the shrinkage prong, every resize-triggered metrics pass re-derived
  // "no keyboard", and the bottom nav resurrected mid-screen over the
  // spelling input in the Android app while mobile web behaved fine.
  // Metrics can still SET the flag (missed show events, plugin-less browsers,
  // native WebView resize) but must never CLEAR it while the plugin says the
  // IME is open. A focused text entry plus below-baseline shrinkage only
  // counts as a keyboard with focus (split-screen resize cannot hide the nav).
  if (textEntryKeyboardVisible) {
    const baselineShortfall = keyboardFreeBaselineHeight - viewportHeight;
    if (baselineShortfall > KEYBOARD_VISIBLE_THRESHOLD_PX) {
      // Resize mode: the shrinking viewport is hard evidence the IME is up.
      viewportShrankWhileTextEntryFocused = true;
    } else if (viewportShrankWhileTextEntryFocused) {
      // Resize mode close: the viewport returned to the keyboard-free baseline
      // while the field kept DOM focus (e.g. back-button IME dismiss). The
      // keyboard is demonstrably gone, so stop holding the nav hidden.
      clearTextEntryKeyboardEvidence();
    }
  }
  const keyboardVisible =
    pluginKeyboardVisible
    || textEntryKeyboardVisible
    || keyboardOffset > KEYBOARD_VISIBLE_THRESHOLD_PX
    || (keyboardFreeBaselineHeight - viewportHeight > KEYBOARD_VISIBLE_THRESHOLD_PX
      && isTextEntryFocused());
  // Re-derived on every metrics pass (resize, orientation change, visualViewport
  // resize/scroll) so the state self-corrects for plugin-less surfaces: a
  // keyboardWillHide that never arrives clears as soon as the viewport returns
  // to the baseline, and a keyboardWillShow that never arrives is caught by
  // the offset/shrinkage tests.
  setKeyboardVisible(keyboardVisible);
}

function scheduleViewportMetrics() {
  if (!isBrowser()) {
    return;
  }

  window.requestAnimationFrame(() => {
    setViewportMetrics();
  });
}

interface NativeSafeAreaInsets {
  top: number;
  right: number;
  bottom: number;
  left: number;
}

/**
 * Placeholder used ONLY until MainActivity's real push lands (see below).
 * app/globals.css's :root default is `env(safe-area-inset-*)`, which Android's
 * WebView never populates — MainActivity's native push is the sole source of
 * truth there, and on a cold launch it can land after this module first runs,
 * or (per the "stuck until the app is backgrounded" bug report) not land at
 * all during the session. Without this, that whole window renders at a hard
 * 0 — header under the status bar, bottom nav flush to the edge — instead of
 * merely approximate. Portrait left/right stay 0 (matches reality on virtually
 * every device); only top/bottom, the two edges phones actually cut into, get
 * a non-zero guess.
 * ponytail: hardcoded approximation, not a per-device measurement. Upgrade
 * path if a wrong guess ever visibly clips on some device/nav-bar combo: read
 * WindowInsets natively before first frame instead of guessing in JS.
 */
const ANDROID_FALLBACK_SAFE_AREA_INSETS: NativeSafeAreaInsets = { top: 24, right: 0, bottom: 16, left: 0 };

/**
 * Android WebView has no `env(safe-area-inset-*)` support, so MainActivity pushes
 * the real insets in as CSS custom properties and mirrors them onto
 * `window.__oetSafeAreaInsets`. That push happens natively while the WebView is
 * still on its initial document, and this app loads its page from a remote URL —
 * so on a cold launch the document that received the values is discarded and the
 * header ends up under the status bar until the next inset change. Re-apply the
 * mirrored values here, once the app has hydrated and on every resume.
 * See android/app/src/main/java/com/oetwithdrhesham/app/MainActivity.java.
 *
 * When the native push hasn't landed yet (window.__oetSafeAreaInsets still
 * undefined) fall back to ANDROID_FALLBACK_SAFE_AREA_INSETS on Android only —
 * iOS's WKWebView resolves env() correctly on its own and needs no help. This
 * never overwrites a real pushed value (checked first, every call) and is
 * itself always overwritten once MainActivity's later push does land, since
 * both write the same inline custom properties on the same element.
 */
export function setSafeAreaInsets() {
  if (!isBrowser()) {
    return;
  }

  const insets = (window as unknown as { __oetSafeAreaInsets?: NativeSafeAreaInsets }).__oetSafeAreaInsets
    ?? (Capacitor.getPlatform() === 'android' ? ANDROID_FALLBACK_SAFE_AREA_INSETS : undefined);
  if (!insets) {
    return;
  }

  const root = document.documentElement.style;
  root.setProperty('--safe-area-inset-top', `${insets.top}px`);
  root.setProperty('--safe-area-inset-right', `${insets.right}px`);
  root.setProperty('--safe-area-inset-bottom', `${insets.bottom}px`);
  root.setProperty('--safe-area-inset-left', `${insets.left}px`);
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
    // Keep this in sync with capacitor.config.ts's StatusBar.overlaysWebView —
    // both must request the same edge-to-edge overlay state, otherwise this
    // runtime call (fired on every resume/color-scheme change) would flip
    // MainActivity's WindowCompat.setDecorFitsSystemWindows(window, false) back
    // to fits-system-windows and silently break the safe-area inset bridge.
    StatusBar.setOverlaysWebView({ overlay: true }),
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
  setSafeAreaInsets();
  document.documentElement.dataset.runtimeKind = 'capacitor-native';
  // Distinct from data-runtime-kind (which the layout bootstrap script also
  // stamps): proves THIS function ran on the device, not just that a native
  // Capacitor bridge was injected. The runtime diagnostics overlay reads it.
  document.documentElement.dataset.mobileRuntimeActive = 'true';
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
  const orientationChangeHandler = () => {
    // Rotation swaps width/height wholesale: drop the baseline so the shorter
    // landscape height is not read as an open keyboard. The next metrics pass
    // re-establishes it at the new orientation's keyboard-free height.
    resetKeyboardBaseline();
    scheduleViewportMetrics();
  };
  window.addEventListener('resize', resizeHandler);
  window.addEventListener('orientationchange', orientationChangeHandler);
  cleanup.push(() => window.removeEventListener('resize', resizeHandler));
  cleanup.push(() => window.removeEventListener('orientationchange', orientationChangeHandler));

  if (window.visualViewport) {
    const visualViewportResize = () => scheduleViewportMetrics();
    window.visualViewport.addEventListener('resize', visualViewportResize);
    window.visualViewport.addEventListener('scroll', visualViewportResize);
    cleanup.push(() => window.visualViewport?.removeEventListener('resize', visualViewportResize));
    cleanup.push(() => window.visualViewport?.removeEventListener('scroll', visualViewportResize));
  }

  // DOM focus is the last-resort keyboard signal (see the block comment on
  // textEntryKeyboardVisible): it works in every soft-input mode and on every
  // device, including the shells where the plugin events and the viewport
  // metrics are both silent. Registered outside the Keyboard try-block so a
  // missing/failing plugin never takes this path down with it.
  const focusInHandler = (event: FocusEvent) => {
    if (!isTextEntryElement(event.target)) {
      return;
    }
    viewportShrankWhileTextEntryFocused = false;
    textEntryKeyboardVisible = true;
    // Synchronous: the nav must be gone before the IME finishes animating in.
    setKeyboardVisible(true);
  };
  const focusOutHandler = () => {
    if (!textEntryKeyboardVisible) {
      return;
    }
    // Focus may hop straight to another field in multi-field forms; judge
    // only after it has settled.
    window.requestAnimationFrame(() => {
      if (!isTextEntryFocused()) {
        clearTextEntryKeyboardEvidence();
        setViewportMetrics();
      }
    });
  };
  document.addEventListener('focusin', focusInHandler);
  document.addEventListener('focusout', focusOutHandler);
  cleanup.push(() => document.removeEventListener('focusin', focusInHandler));
  cleanup.push(() => document.removeEventListener('focusout', focusOutHandler));

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
    // Each keyboard transition is handled in both its will* and did* forms.
    // Android emits the pair almost simultaneously, but which of the two is
    // delivered varies by API level, and the nav's hide rule must not depend on
    // that. The raw `event.keyboardHeight` is deliberately NOT written to
    // `--app-keyboard-offset`: with KeyboardResize.Body the layout viewport does
    // not change, so applying a keyboard height as a bottom offset threw the
    // fixed bottom nav into the middle of the screen (issue report 11 Sep 2026).
    // The plugin's state is ALSO mirrored into pluginKeyboardVisible so the
    // metrics re-derivation cannot clobber it while the IME transition runs
    // (see setViewportMetrics — report 13 Sep 2026).
    const keyboardWillShow = await Keyboard.addListener('keyboardWillShow', () => {
      pluginKeyboardVisible = true;
      setKeyboardVisible(true);
      scheduleViewportMetrics();
    });
    const keyboardDidShow = await Keyboard.addListener('keyboardDidShow', () => {
      pluginKeyboardVisible = true;
      setKeyboardVisible(true);
      scheduleViewportMetrics();
    });

    const keyboardWillHide = await Keyboard.addListener('keyboardWillHide', () => {
      pluginKeyboardVisible = false;
      // A plugin-reported hide is hard evidence the IME is closing, even if
      // the field keeps DOM focus (back-button dismiss) — release the
      // focus-based hold so the nav returns.
      clearTextEntryKeyboardEvidence();
      setKeyboardVisible(false);
      document.documentElement.style.setProperty('--app-keyboard-offset', '0px');
      scheduleViewportMetrics();
    });
    const keyboardDidHide = await Keyboard.addListener('keyboardDidHide', () => {
      pluginKeyboardVisible = false;
      clearTextEntryKeyboardEvidence();
      setKeyboardVisible(false);
      document.documentElement.style.setProperty('--app-keyboard-offset', '0px');
      scheduleViewportMetrics();
    });

    cleanup.push(() => keyboardWillShow.remove());
    cleanup.push(() => keyboardDidShow.remove());
    cleanup.push(() => keyboardWillHide.remove());
    cleanup.push(() => keyboardDidHide.remove());
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
        setSafeAreaInsets();
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