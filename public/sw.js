/// <reference lib="webworker" />

/**
 * OET Prep — Service Worker (L15 Offline/PWA Support)
 *
 * Strategy:
 *  - CACHE_FIRST for static assets (fonts, icons, CSS, JS chunks)
 *  - STALE_WHILE_REVALIDATE for pages (HTML navigation)
 *  - NETWORK_FIRST for API calls (never serve stale data, but fall back to cache)
 *  - Pre-caches the app shell on install
 */

const CACHE_VERSION = 'oet-v1';
const STATIC_CACHE = `${CACHE_VERSION}-static`;
const PAGES_CACHE = `${CACHE_VERSION}-pages`;
const API_CACHE = `${CACHE_VERSION}-api`;

const APP_SHELL_URLS = [
  '/',
  '/dashboard',
  '/manifest.json',
  '/icon.svg',
];

const STATIC_EXTENSIONS = /\.(js|css|woff2?|ttf|eot|svg|png|jpg|jpeg|webp|ico|json)$/i;
const API_PATH = /\/v1\//;

// ---------- Install ----------
self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(STATIC_CACHE)
      .then((cache) => cache.addAll(APP_SHELL_URLS))
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
            .filter((k) => k.startsWith('oet-') && k !== STATIC_CACHE && k !== PAGES_CACHE && k !== API_CACHE)
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

  // API requests → Network First
  if (API_PATH.test(url.pathname)) {
    event.respondWith(networkFirst(request, API_CACHE));
    return;
  }

  // Static assets → Cache First
  if (STATIC_EXTENSIONS.test(url.pathname)) {
    event.respondWith(cacheFirst(request, STATIC_CACHE));
    return;
  }

  // Navigation requests (HTML pages) → Stale While Revalidate
  if (request.mode === 'navigate' || request.headers.get('accept')?.includes('text/html')) {
    event.respondWith(staleWhileRevalidate(request, PAGES_CACHE));
    return;
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
    return cached || new Response(JSON.stringify({ error: 'Offline' }), {
      status: 503,
      headers: { 'Content-Type': 'application/json' },
    });
  }
}

async function staleWhileRevalidate(request, cacheName) {
  const cache = await caches.open(cacheName);
  const cached = await cache.match(request);

  const fetchPromise = fetch(request)
    .then((response) => {
      if (response.ok) {
        cache.put(request, response.clone());
      }
      return response;
    })
    .catch(() => cached);

  return cached || fetchPromise;
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

// ---------- Push Notifications ----------
self.addEventListener('push', (event) => {
  if (!event.data) return;

  try {
    const payload = event.data.json();
    const title = payload.title || 'OET Prep';
    const options = {
      body: payload.body || '',
      icon: '/icon.svg',
      badge: '/icon.svg',
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
