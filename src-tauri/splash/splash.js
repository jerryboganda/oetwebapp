// Remote-only thin-client bootstrap. Probes the live web app and navigates to
// it; if the network is down, shows an offline/retry screen instead of a blank
// window. The remote URL is injected by the Rust shell as globalThis.__OET_REMOTE__
// (see src-tauri/src/lib.rs::bridge_script).
(() => {
  'use strict';

  const REMOTE = (globalThis.__OET_REMOTE__ || 'https://app.oetwithdrhesham.co.uk').replace(/\/+$/, '');
  const PROBE_URL = REMOTE + '/api/health';
  const PROBE_TIMEOUT_MS = 8000;

  const loading = document.getElementById('loading');
  const offline = document.getElementById('offline');
  const status = document.getElementById('status');
  const detail = document.getElementById('offline-detail');
  const retry = document.getElementById('retry');

  let probing = false;

  function showLoading(message) {
    if (status) status.textContent = message || 'Connecting…';
    if (loading) loading.hidden = false;
    if (offline) offline.hidden = true;
  }

  function showOffline(reason) {
    if (loading) loading.hidden = true;
    if (offline) offline.hidden = false;
    if (detail) detail.textContent = reason || '';
    if (retry) retry.disabled = false;
  }

  async function probe() {
    if (probing) return;
    probing = true;
    if (retry) retry.disabled = true;
    showLoading('Connecting…');

    if (typeof navigator !== 'undefined' && navigator.onLine === false) {
      probing = false;
      showOffline('No network connection detected.');
      return;
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), PROBE_TIMEOUT_MS);
    try {
      // no-cors: a reachable server resolves (opaque response); only a real
      // network failure rejects — exactly the reachability signal we need.
      await fetch(PROBE_URL, { method: 'GET', cache: 'no-store', mode: 'no-cors', signal: controller.signal });
      clearTimeout(timer);
      showLoading('Loading OET with Dr Hesham…');
      window.location.replace(REMOTE);
    } catch (err) {
      clearTimeout(timer);
      probing = false;
      const message = err && err.name === 'AbortError' ? 'The server took too long to respond.' : 'Could not reach the OET with Dr Hesham servers.';
      showOffline(message);
    }
  }

  if (retry) retry.addEventListener('click', probe);
  // Auto-retry when the OS reports the network is back.
  window.addEventListener('online', () => { if (offline && !offline.hidden) probe(); });

  probe();
})();
