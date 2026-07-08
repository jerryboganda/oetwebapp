// window.desktopBridge for the Tauri shell — implements the same contract
// (types/desktop.d.ts) the renderer already expects. Injected via
// initialization_script, so it exists before any app JS runs. Frontend
// consumers need zero changes.
(() => {
  'use strict';
  if (window.desktopBridge) return;

  const internals = () => window.__TAURI_INTERNALS__;
  const invoke = (cmd, args) => {
    const t = internals();
    if (!t || typeof t.invoke !== 'function') {
      return Promise.reject(new Error('Tauri IPC unavailable for this origin.'));
    }
    return t.invoke(cmd, args);
  };

  const meta = globalThis.__OET_DESKTOP__ || { platform: 'win32', tauri: '2' };
  const chromeVersion = (navigator.userAgent.match(/Chrom(?:e|ium)\/([0-9.]+)/) || [])[1] || '';

  const toBase64 = (bytes) => {
    let binary = '';
    const CHUNK = 0x8000;
    for (let i = 0; i < bytes.length; i += CHUNK) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK));
    }
    return btoa(binary);
  };
  const fromBase64 = (b64) => {
    const binary = atob(b64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
    return bytes.buffer;
  };
  const chunkToBytes = (chunk) => {
    if (chunk instanceof ArrayBuffer) return new Uint8Array(chunk);
    if (ArrayBuffer.isView(chunk)) return new Uint8Array(chunk.buffer, chunk.byteOffset, chunk.byteLength);
    return new Uint8Array(0);
  };

  window.desktopBridge = {
    platform: meta.platform,
    versions: {
      // chrome/node preserved for contract parity; flavor/tauri are additive.
      chrome: chromeVersion,
      node: '',
      tauri: meta.tauri,
      flavor: 'tauri',
    },
    openExternal: (url) => invoke('open_external', { url }),
    runtime: {
      info: () => invoke('runtime_info'),
      onWindowStateChange: (listener) => {
        const handler = (event) => listener(event.detail);
        window.addEventListener('desktop:window-state-changed', handler);
        return () => window.removeEventListener('desktop:window-state-changed', handler);
      },
    },
    updater: {
      // Manual update flow. check() reports availability without installing;
      // install() downloads + verifies + stages (progress via onProgress);
      // relaunch() restarts into the new version.
      check: () => invoke('updater_check'),
      install: () => invoke('updater_install'),
      relaunch: () => invoke('app_relaunch'),
      onProgress: (listener) => {
        const handler = (event) => listener(event.detail);
        window.addEventListener('desktop:update-progress', handler);
        window.addEventListener('desktop:update-available', handler);
        return () => {
          window.removeEventListener('desktop:update-progress', handler);
          window.removeEventListener('desktop:update-available', handler);
        };
      },
    },
    reload: {
      // Ctrl+F5 equivalent: native clear browsing data + re-navigate to origin.
      hard: () => invoke('hard_reload'),
    },
    secureSecrets: {
      get: (namespace, key) => invoke('secret_get', { namespace, key }),
      set: (namespace, key, value) => invoke('secret_set', { namespace, key, value }),
      delete: (namespace, key) => invoke('secret_delete', { namespace, key }),
      status: () => invoke('secret_status'),
    },
    offlineCache: {
      store: (key, data) => invoke('offline_cache_store', { key, data }),
      get: (key) => invoke('offline_cache_get', { key }),
      delete: (key) => invoke('offline_cache_delete', { key }),
      list: () => invoke('offline_cache_list'),
      clear: () => invoke('offline_cache_clear'),
    },
    notifications: {
      show: (title, body, route) => invoke('show_notification', { title, body, route }),
    },
    fileInfo: {
      getDroppedFileInfo: (filePath) => invoke('get_dropped_file_info', { filePath }),
    },
    print: {
      // WebView2 shows the system print dialog for window.print(); WKWebView
      // parity is tracked in the migration plan (with_webview print op).
      printPage: () => {
        try {
          window.print();
          return Promise.resolve({ ok: true });
        } catch (err) {
          return Promise.resolve({ ok: false, error: (err && err.message) || 'PRINT_FAILED' });
        }
      },
    },
    attestation: {
      // Native HMAC signer for app-only video playback. Tauri 2 invoke maps
      // camelCase JS arg keys onto the snake_case Rust params (same convention
      // as filePath/sessionId above). Returns { signature, platform: 'tauri',
      // keyId, appVersion }; the secret never leaves the Rust side.
      signVideoChallenge: (nonce, videoId, userId) =>
        invoke('sign_video_challenge', { nonce, videoId, userId }),
    },
    speakingAudio: {
      start: (sessionId, mimeType) => invoke('speaking_audio_start', { sessionId, mimeType }),
      stop: (sessionId, chunks) => {
        const list = Array.isArray(chunks) ? chunks : [];
        const chunksBase64 = list.map((c) => toBase64(chunkToBytes(c)));
        return invoke('speaking_audio_stop', { sessionId, chunksBase64 });
      },
      getBlob: (sessionId) =>
        invoke('speaking_audio_get_blob', { sessionId }).then((res) => {
          if (res && res.ok && typeof res.dataBase64 === 'string') {
            const { dataBase64, ...rest } = res;
            return { ...rest, data: fromBase64(dataBase64) };
          }
          return res;
        }),
      discard: (sessionId) => invoke('speaking_audio_discard', { sessionId }),
      getPlatform: () => meta.platform,
    },
  };
})();
