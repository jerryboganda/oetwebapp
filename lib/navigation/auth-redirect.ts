/**
 * Single-flight hard navigation for authentication state changes.
 *
 * Several independent subsystems can discover the same auth-state flip at the
 * same time — e.g. ten dashboard queries whose bearer is rejected at once, or
 * a SignalR `session_revoked` push landing mid-refresh. Each of them used to
 * call `window.location.assign/replace` on its own, so one logical event
 * produced N full WebView document loads in a burst (the repeated-reload
 * storm seen on slower devices, where the navigations interleave instead of
 * the first unload winning the race).
 *
 * This module collapses those duplicates: the first caller in a document
 * lifecycle performs the navigation; later callers in the same document are
 * no-ops. The flag is intentionally plain in-memory state, not a debounce or
 * timeout — a hard navigation unloads the document, so "once per document"
 * is exactly the right scope, and a fresh document (the navigation target)
 * starts with a clean flag.
 */

/** The target already navigated to in this document, if any. */
let navigatedTarget: string | null = null;

/**
 * Navigate once. Returns true when this call performed the navigation,
 * false when one already happened in this document lifecycle.
 */
export function navigateAuthOnce(url: string, replace = true): boolean {
  if (typeof window === 'undefined') {
    return false;
  }

  if (navigatedTarget !== null) {
    return false;
  }

  navigatedTarget = url;

  if (replace) {
    window.location.replace(url);
  } else {
    window.location.assign(url);
  }

  return true;
}

/** Test-only reset for the per-document single-flight flag. */
export function resetAuthNavigationForTests(): void {
  navigatedTarget = null;
}
