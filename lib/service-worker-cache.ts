/**
 * Asks the page's service worker to drop every cached API response.
 *
 * `public/sw.js` answers the `CLEAR_AUTH_CACHE` message by emptying its API
 * cache. Offline, that cache is keyed by URL only (the Authorization header is
 * ignored), so without this a shared device would keep serving the previous
 * account's cached API bodies to the next learner. Called from
 * `clearStoredSession`, the single choke point every sign-out and every
 * session loss goes through. Fire-and-forget: it must never block or fail a
 * sign-out, and it is a no-op where no worker exists (native shells, SSR).
 */
export function clearServiceWorkerAuthCache(): void {
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) {
    return;
  }

  const message = { type: 'CLEAR_AUTH_CACHE' };
  try {
    const container = navigator.serviceWorker;
    if (container.controller) {
      container.controller.postMessage(message);
      return;
    }

    // First load after install: the worker is active but has not claimed this
    // page yet, so there is no controller to message.
    void container
      .getRegistration()
      .then((registration) => {
        registration?.active?.postMessage(message);
      })
      .catch(() => undefined);
  } catch {
    // A failed cache purge must never break sign-out.
  }
}
