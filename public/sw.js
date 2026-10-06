/// <reference lib="webworker" />

/**
 * OET Prep — Service Worker (L15 Offline/PWA Support)
 *
 * Strategy:
 *  - HTML pages are NEVER cached. Navigations go to the network; if the network
 *    fails, a small built-in "offline — retrying" page is shown. Serving a cached
 *    page from an earlier build loads that build's old JavaScript against today's
 *    API — that is how users ended up on a weeks-old pre-rebrand sign-in page
 *    whose sign-in failed with "Failed to fetch" (incident 2026-09-27).
 *  - CACHE_FIRST only for content-hashed, immutable build assets (/_next/static/)
 *    and font files — a hashed URL can never be stale.
 *  - NETWORK_FIRST for every other static file (icons, manifest, images, .json),
 *    with the cache as an offline fallback only.
 *  - NETWORK_FIRST for API GETs; offline fallback serves a cached body only if it
 *    is less than API_FALLBACK_MAX_AGE_MS old.
 *
 * Bump CACHE_VERSION whenever a deploy must invalidate previously-cached data:
 * `activate` deletes every `oet-*` cache that is not one of the current names.
 * v6 purges the v5 page cache that held pre-rebrand HTML.
 * v7 purges cached Writing draft bodies (letter text) now that drafts bypass the SW.
 * v8 purges the SignalR long-poll responses that v7 wrote into the API cache (one
 * entry per poll, keyed by a unique `_=` URL) now that hub traffic bypasses the SW.
 */

const CACHE_VERSION = 'oet-v8';
const STATIC_CACHE = `${CACHE_VERSION}-static`;
const API_CACHE = `${CACHE_VERSION}-api`;
const CURRENT_CACHES = [STATIC_CACHE, API_CACHE];

// Non-HTML only: an HTML "app shell" would be a snapshot of one build.
const PRECACHE_URLS = ['/manifest.json', '/icon-192.png'];

const IMMUTABLE_ASSET = /^\/_next\/static\//;
const FONT_FILE = /\.(woff2?|ttf|eot|otf)$/i;
const STATIC_EXTENSIONS = /\.(js|css|woff2?|ttf|eot|otf|svg|png|jpg|jpeg|webp|gif|ico|json)$/i;
const API_PATH = /\/v1\//;
const API_FALLBACK_MAX_AGE_MS = 24 * 60 * 60 * 1000;

// Video Library streaming must NEVER touch SW caches:
//  - HLS playlists/segments/keys stream from the Bunny CDN with short-lived
//    signed tokens — caching them would persist expired-token URLs (and video
//    bytes) in Cache Storage.
//  - Playback-session / attestation API responses embed signed URLs; caching
//    one would let an expired session "replay" from cache while offline.
const STREAMING_MEDIA = /\.(m3u8|ts|m4s|mp4|key|vtt)(\?|$)/i;
const VIDEO_PLAYBACK_API = /\/v1\/video-library\/(attestation|playback-sessions)|\/v1\/video-library\/videos\/[^/]+\/playback-session/i;
const MEDIA_CDN_HOST = /\.b-cdn\.net$/i;

// Exam media must never be served from Cache Storage either:
//  - /v1/media/{id}/content is the bearer-authenticated route every Listening
//    and Reading asset streams through. When a paper's audio is replaced, the
//    asset id changes but a cached body for the OLD id can still satisfy a
//    stale page, so a candidate keeps hearing the withdrawn recording.
//  - /v1/listening/audio/{sha}.wav is anonymous and ships no Cache-Control and
//    no Vary, so a cached copy would replay indefinitely.
// Both are large one-play-only bodies; caching them is a quota cost with no
// offline benefit, because an attempt cannot be scored offline anyway.
const EXAM_MEDIA_API = /^\/v1\/(media\/[^/]+\/content|listening\/audio\/)/i;

// Writing drafts must always come from the server: a cached GET (up to 24 h
// old) could restore older text and a stale version over newer work. Unanchored
// because the browser reaches the API through the /api/backend proxy.
const WRITING_DRAFT_API = /\/v1\/writing\/drafts\//i;

// SignalR hubs (/v1/notifications/hub, /v1/ai-assistant/hub, /hubs/writing-today, ...)
// are long-lived and stream long-poll responses. Every poll carries a unique `_=` URL,
// so letting networkFirstApi cache them wrote one new Cache Storage entry per poll per
// open tab, unbounded until the next CACHE_VERSION bump, and an SSE fallback would be
// cached as an unbounded stream. They must never be intercepted. Matched on the path
// segment, so `/v1/.../hub` and `/hubs/...` are covered on any origin.
const SIGNALR_HUB = /\/hubs?(\/|$)/i;

// The public catalogue and the AI-package catalogue carry admin-edited package
// copy and live prices. A saved edit must reach the next fetch, so these are
// never cached or served from Cache Storage while offline. Unanchored for the
// same /api/backend proxy reason as above.
const CATALOG_API = /\/v1\/(catalog\/pricing|billing\/ai-packages)/i;

// ---------- Install ----------
self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(STATIC_CACHE)
      .then((cache) => cache.addAll(PRECACHE_URLS))
      .catch(() => undefined) // a missing icon must never block the update
      .then(() => self.skipWaiting())
  );
});

// ---------- Activate ----------
self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(
          keys
            .filter((k) => k.startsWith('oet-') && !CURRENT_CACHES.includes(k))
            .map((k) => caches.delete(k))
        )
      )
      .then(() => self.clients.claim())
  );
});

// ---------- Fetch ----------
self.addEventListener('fetch', (event) => {
  const { request } = event;

  // Only handle GET requests
  if (request.method !== 'GET') return;

  const url = new URL(request.url);

  // Skip chrome-extension, blob, etc.
  if (!url.protocol.startsWith('http')) return;

  // Streaming media + playback-session endpoints bypass the SW entirely
  // (no interception, no caching) — see constants above.
  if (
    MEDIA_CDN_HOST.test(url.hostname) ||
    STREAMING_MEDIA.test(url.pathname) ||
    VIDEO_PLAYBACK_API.test(url.pathname) ||
    EXAM_MEDIA_API.test(url.pathname) ||
    WRITING_DRAFT_API.test(url.pathname) ||
    SIGNALR_HUB.test(url.pathname) ||
    CATALOG_API.test(url.pathname)
  ) {
    return;
  }

  // Navigation requests (HTML pages) → network only, built-in offline page on failure.
  if (request.mode === 'navigate' || request.headers.get('accept')?.includes('text/html')) {
    event.respondWith(networkOnlyPage(request));
    return;
  }

  // API requests → Network First (never serve stale auth-gated data without validation)
  if (API_PATH.test(url.pathname)) {
    event.respondWith(networkFirstApi(request, API_CACHE));
    return;
  }

  // Only same-origin static files are cached.
  if (url.origin !== self.location.origin) return;

  // Content-hashed build assets and fonts → Cache First (immutable).
  if (IMMUTABLE_ASSET.test(url.pathname) || FONT_FILE.test(url.pathname)) {
    event.respondWith(cacheFirst(request, STATIC_CACHE));
    return;
  }

  // Other static files (icons, manifest, images, .json) → Network First.
  if (STATIC_EXTENSIONS.test(url.pathname)) {
    event.respondWith(networkFirst(request, STATIC_CACHE));
  }
});

// ---------- Strategies ----------

async function cacheFirst(request, cacheName) {
  const cached = await caches.match(request);
  if (cached) return cached;

  try {
    const response = await fetch(request);
    if (response.ok) {
      const cache = await caches.open(cacheName);
      cache.put(request, response.clone());
    }
    return response;
  } catch {
    return new Response('Offline', { status: 503, statusText: 'Service Unavailable' });
  }
}

async function networkFirst(request, cacheName) {
  try {
    const response = await fetch(request);
    if (response.ok) {
      const cache = await caches.open(cacheName);
      cache.put(request, response.clone());
    }
    return response;
  } catch {
    const cached = await caches.match(request);
    return cached || new Response('Offline', { status: 503, statusText: 'Service Unavailable' });
  }
}

async function networkOnlyPage(request) {
  try {
    return await fetch(request);
  } catch {
    return offlinePage();
  }
}

function offlinePage() {
  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Offline · OET with Dr Ahmed Hesham</title>
<style>body{font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0;background:#f7f7fb;color:#1f2937}main{max-width:26rem;padding:2rem;text-align:center}button{margin-top:1rem;padding:.6rem 1.2rem;border:0;border-radius:.6rem;background:#4f46e5;color:#fff;font-size:1rem;cursor:pointer}</style>
</head><body><main><h1>You're offline</h1><p>We couldn't reach OET with Dr Ahmed Hesham. This page will reload automatically when your connection is back.</p><button onclick="location.reload()">Try again</button></main>
<script>addEventListener('online',function(){location.reload()});setTimeout(function(){location.reload()},15000);</script>
</body></html>`;
  return new Response(html, {
    status: 503,
    statusText: 'Service Unavailable',
    headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' },
  });
}

/**
 * Network-first for API GETs. The offline fallback only serves a cached body
 * younger than API_FALLBACK_MAX_AGE_MS (judged by the server's Date header), so
 * a long-offline device never resurrects days-old data. The frontend auth layer
 * still validates tokens and redirects if the session is invalid.
 */
async function networkFirstApi(request, cacheName) {
  try {
    const response = await fetch(request);
    if (response.ok) {
      const cache = await caches.open(cacheName);
      cache.put(request, response.clone());
    }
    return response;
  } catch {
    const cached = await caches.match(request);
    const servedAt = cached ? Date.parse(cached.headers.get('date') || '') : NaN;
    if (cached && Number.isFinite(servedAt) && Date.now() - servedAt < API_FALLBACK_MAX_AGE_MS) {
      return cached;
    }
    return new Response(JSON.stringify({ error: 'Offline', offline: true }), {
      status: 503,
      headers: { 'Content-Type': 'application/json' },
    });
  }
}

// ---------- Background Sync (placeholder for future offline submissions) ----------
self.addEventListener('sync', (event) => {
  if (event.tag === 'offline-submissions') {
    event.waitUntil(syncOfflineSubmissions());
  }
});

async function syncOfflineSubmissions() {
  // Future: replay queued practice submissions from IndexedDB
}

/**
 * Clear all API caches on sign-out. Called from the main app via postMessage.
 */
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'CLEAR_AUTH_CACHE') {
    event.waitUntil(
      caches.open(API_CACHE).then((cache) =>
        cache.keys().then((keys) => Promise.all(keys.map((key) => cache.delete(key))))
      )
    );
  }
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
});

// ---------- Push Notifications ----------
self.addEventListener('push', (event) => {
  if (!event.data) return;

  try {
    const payload = event.data.json();
    const title = payload.title || 'OET with Dr Ahmed Hesham';
    const options = {
      body: payload.body || '',
      icon: '/icon-192.png',
      badge: '/icon-192.png',
      tag: payload.tag || 'oet-notification',
      data: { url: payload.url || '/dashboard' },
    };

    event.waitUntil(self.registration.showNotification(title, options));
  } catch {
    // Ignore malformed push messages
  }
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const targetUrl = event.notification.data?.url || '/dashboard';

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if (client.url.includes(targetUrl) && 'focus' in client) {
          return client.focus();
        }
      }
      return self.clients.openWindow(targetUrl);
    })
  );
});
