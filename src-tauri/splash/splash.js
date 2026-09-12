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
  const unsupported = document.getElementById('unsupported');
  const unsupportedDetail = document.getElementById('unsupported-detail');
  const recheck = document.getElementById('recheck');
  const continueAnyway = document.getElementById('continue-anyway');

  let probing = false;

  function showLoading(message) {
    if (status) status.textContent = message || 'Connecting…';
    if (loading) loading.hidden = false;
    if (offline) offline.hidden = true;
    if (unsupported) unsupported.hidden = true;
  }

  function showOffline(reason) {
    if (loading) loading.hidden = true;
    if (offline) offline.hidden = false;
    if (unsupported) unsupported.hidden = true;
    if (detail) detail.textContent = reason || '';
    if (retry) retry.disabled = false;
  }

  function showUnsupported(reason) {
    if (loading) loading.hidden = true;
    if (offline) offline.hidden = true;
    if (unsupported) unsupported.hidden = false;
    if (unsupportedDetail) unsupportedDetail.textContent = reason || '';
  }

  // A stylesheet-presence check cannot detect @property: engines that lack
  // support drop the unknown at-rule silently, so a successful parse proves
  // nothing. The only reliable signal is that a registered typed custom
  // property actually resolves its initial-value through var().
  function supportsAtProperty() {
    let styleEl = null;
    let probeEl = null;

    try {
      styleEl = document.createElement('style');
      styleEl.textContent =
        '@property --oet-probe { syntax: "<color>"; inherits: false; initial-value: rgb(1, 2, 3); }'
        + ' .oet-at-property-probe { color: var(--oet-probe); }';
      document.head.appendChild(styleEl);

      probeEl = document.createElement('div');
      probeEl.className = 'oet-at-property-probe';
      document.body.appendChild(probeEl);

      return getComputedStyle(probeEl).color === 'rgb(1, 2, 3)';
    } catch (err) {
      return false;
    } finally {
      if (probeEl && probeEl.parentNode) probeEl.parentNode.removeChild(probeEl);
      if (styleEl && styleEl.parentNode) styleEl.parentNode.removeChild(styleEl);
    }
  }

  // The remote content is a Next.js 16 + Tailwind v4 build, which requires
  // Safari 16.4+. macOS ships that engine with Safari rather than with the OS
  // version, so a supported macOS release can still be too old. Detect it and
  // explain the fix instead of navigating into a half-rendered UI. Plain
  // ES2017 on purpose: the probe runs before any modern content is loaded, so
  // it must not throw on the very engines it is meant to catch.
  function detectUnsupportedEngine() {
    const missing = [];

    try {
      if (typeof CSS === 'undefined' || typeof CSS.supports !== 'function') {
        return 'This WebKit build does not support CSS feature detection.';
      }
      if (!CSS.supports('color', 'oklch(0 0 0)')) missing.push('wide-gamut colours');
      if (!CSS.supports('color', 'color-mix(in oklab, red, blue)')) missing.push('colour mixing');
      if (!supportsAtProperty()) missing.push('typed CSS custom properties');
      if (typeof globalThis.crypto === 'undefined' || typeof globalThis.crypto.randomUUID !== 'function') {
        missing.push('secure random identifiers');
      }
      if (typeof globalThis.structuredClone !== 'function') missing.push('structured cloning');
      if (typeof [].at !== 'function') missing.push('array helpers');
    } catch (err) {
      return 'Engine feature detection could not run.';
    }

    if (missing.length === 0) return null;
    return 'Missing support for: ' + missing.join(', ') + '.';
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
      showLoading('Loading OET with Dr. Hesham…');
      window.location.replace(REMOTE);
    } catch (err) {
      clearTimeout(timer);
      probing = false;
      const message = err && err.name === 'AbortError' ? 'The server took too long to respond.' : 'Could not reach the OET with Dr. Hesham servers.';
      showOffline(message);
    }
  }

  function run() {
    const reason = detectUnsupportedEngine();
    if (reason) {
      showUnsupported(reason);
      return;
    }
    probe();
  }

  if (retry) retry.addEventListener('click', probe);
  if (recheck) recheck.addEventListener('click', run);
  if (continueAnyway) {
    // Explicit override: a misdetected engine must never permanently block the
    // app. Degraded rendering is the user's informed choice at this point.
    continueAnyway.addEventListener('click', () => {
      if (unsupported) unsupported.hidden = true;
      probe();
    });
  }
  // Auto-retry when the OS reports the network is back.
  window.addEventListener('online', () => { if (offline && !offline.hidden) probe(); });

  run();
})();
