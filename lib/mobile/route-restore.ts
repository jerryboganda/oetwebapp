'use client';

/**
 * Mobile route persistence — resilience against true WebView reloads.
 *
 * On Android/iOS the OS may kill the WebView renderer (or the whole process)
 * while the app is backgrounded. When the learner returns, Capacitor reloads
 * its start URL — which, in remote-server mode, is the capacitor-web
 * trampoline that redirects to the site root. All React state is gone and the
 * learner lands on `/` instead of where they were (e.g. mid-checkout).
 *
 * We cannot prevent the kill, but we can make recovery invisible: remember the
 * last meaningful route while the app is alive, and silently navigate back to
 * it once after a cold boot through the trampoline.
 *
 * Storage choice: localStorage. It is disk-backed in both WKWebView and the
 * Android WebView, so it survives renderer AND full process death (unlike any
 * in-memory store), and needs no native plugin round-trip.
 */

const LAST_ROUTE_KEY = 'oet.mobile.lastRoute';
const RESTORE_FLAG_KEY = 'oet.mobile.routeRestore.done';

/** Routes that must never be remembered or restored. */
const NON_RESTORABLE_PREFIXES = ['/sign-in', '/register', '/forgot-password', '/reset-password'];

function isRestorable(route: string): boolean {
  if (!route.startsWith('/')) return false;
  if (route === '/') return false;
  if (route.startsWith('//')) return false;
  return !NON_RESTORABLE_PREFIXES.some((prefix) => route === prefix || route.startsWith(`${prefix}/`) || route.startsWith(`${prefix}?`));
}

export function rememberCurrentRoute(): void {
  if (typeof window === 'undefined') return;
  try {
    const route = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    if (!isRestorable(route)) return;
    window.localStorage.setItem(LAST_ROUTE_KEY, route);
  } catch {
    // Storage unavailable (private mode, quota) — restoration is best-effort.
  }
}

/**
 * Returns the route to restore after a cold boot, or null. Restores at most
 * once per document load and only when we actually landed on the trampoline
 * destination (`/`) rather than a deep link or normal navigation.
 */
export function consumeRestorableRoute(): string | null {
  if (typeof window === 'undefined') return null;
  try {
    if (window.sessionStorage.getItem(RESTORE_FLAG_KEY) === '1') {
      return null;
    }
    window.sessionStorage.setItem(RESTORE_FLAG_KEY, '1');

    // Only rescue a trampoline/root landing. If the OS relaunched us into a
    // deep link or the user reopened a specific URL, honour that instead.
    if (window.location.pathname !== '/') {
      return null;
    }

    const saved = window.localStorage.getItem(LAST_ROUTE_KEY);
    if (!saved || !isRestorable(saved)) {
      return null;
    }

    window.localStorage.removeItem(LAST_ROUTE_KEY);
    return saved;
  } catch {
    return null;
  }
}
