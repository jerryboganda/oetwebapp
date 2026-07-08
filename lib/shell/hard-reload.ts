import { getAppRuntimeKind } from '@/lib/runtime-signals';

/**
 * Ctrl+F5 equivalent for every runtime: drop caches and re-fetch everything
 * (including server-driven settings) from the network.
 *
 *  - desktop (Tauri): native `clear_all_browsing_data` + re-navigate to the
 *    trusted origin via the bridge; falls back to a web reload on old shells.
 *  - mobile (Capacitor): the native WebView honours HTTP caching, so a changed
 *    URL query bypasses it and forces a fresh document fetch.
 *  - web: unregister the service worker and delete its caches, then reload.
 */
export async function hardReload(): Promise<void> {
  if (typeof window === 'undefined') return;

  const kind = getAppRuntimeKind();

  if (kind === 'desktop') {
    const hard = window.desktopBridge?.reload?.hard;
    if (hard) {
      try {
        await hard();
        return;
      } catch {
        // Older/failed native path — fall through to the web reload below.
      }
    }
  }

  if (kind === 'capacitor-native') {
    const url = new URL(window.location.href);
    url.searchParams.set('_r', String(Date.now()));
    window.location.replace(url.toString());
    return;
  }

  await clearWebCaches();
  window.location.reload();
}

async function clearWebCaches(): Promise<void> {
  if (typeof navigator !== 'undefined' && 'serviceWorker' in navigator) {
    try {
      const registrations = await navigator.serviceWorker.getRegistrations();
      await Promise.all(registrations.map((registration) => registration.unregister()));
    } catch {
      // Non-fatal — proceed to cache clear + reload.
    }
  }

  if (typeof caches !== 'undefined') {
    try {
      const keys = await caches.keys();
      await Promise.all(keys.filter((key) => key.startsWith('oet-')).map((key) => caches.delete(key)));
    } catch {
      // Non-fatal — proceed to reload.
    }
  }
}
